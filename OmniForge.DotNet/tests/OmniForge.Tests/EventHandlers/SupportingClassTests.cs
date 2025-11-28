using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Services.EventHandlers;
using Xunit;

namespace OmniForge.Tests.EventHandlers
{
    public class EventSubHandlerRegistryTests
    {
        [Fact]
        public void GetHandler_WhenHandlerExists_ShouldReturnHandler()
        {
            // Arrange
            var mockHandler = new Mock<IEventSubHandler>();
            mockHandler.Setup(x => x.SubscriptionType).Returns("stream.online");

            var registry = new EventSubHandlerRegistry(new[] { mockHandler.Object });

            // Act
            var result = registry.GetHandler("stream.online");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("stream.online", result.SubscriptionType);
        }

        [Fact]
        public void GetHandler_WhenHandlerNotExists_ShouldReturnNull()
        {
            // Arrange
            var mockHandler = new Mock<IEventSubHandler>();
            mockHandler.Setup(x => x.SubscriptionType).Returns("stream.online");

            var registry = new EventSubHandlerRegistry(new[] { mockHandler.Object });

            // Act
            var result = registry.GetHandler("unknown.type");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetHandler_ShouldBeCaseInsensitive()
        {
            // Arrange
            var mockHandler = new Mock<IEventSubHandler>();
            mockHandler.Setup(x => x.SubscriptionType).Returns("stream.online");

            var registry = new EventSubHandlerRegistry(new[] { mockHandler.Object });

            // Act
            var result = registry.GetHandler("STREAM.ONLINE");

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void GetAllHandlers_ShouldReturnAllRegisteredHandlers()
        {
            // Arrange
            var mockHandler1 = new Mock<IEventSubHandler>();
            mockHandler1.Setup(x => x.SubscriptionType).Returns("stream.online");
            var mockHandler2 = new Mock<IEventSubHandler>();
            mockHandler2.Setup(x => x.SubscriptionType).Returns("stream.offline");

            var registry = new EventSubHandlerRegistry(new[] { mockHandler1.Object, mockHandler2.Object });

            // Act
            var result = registry.GetAllHandlers().ToList();

            // Assert
            Assert.Equal(2, result.Count);
        }
    }

    public class DiscordNotificationTrackerTests
    {
        [Fact]
        public void RecordNotification_ShouldStoreNotification()
        {
            // Arrange
            var tracker = new DiscordNotificationTracker();

            // Act
            tracker.RecordNotification("user123", true);

            // Assert
            var result = tracker.GetLastNotification("user123");
            Assert.NotNull(result);
            Assert.True(result.Value.Success);
        }

        [Fact]
        public void GetLastNotification_WhenNoNotification_ShouldReturnNull()
        {
            // Arrange
            var tracker = new DiscordNotificationTracker();

            // Act
            var result = tracker.GetLastNotification("unknown");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void RecordNotification_ShouldUpdateExisting()
        {
            // Arrange
            var tracker = new DiscordNotificationTracker();

            // Act
            tracker.RecordNotification("user123", false);
            tracker.RecordNotification("user123", true);

            // Assert
            var result = tracker.GetLastNotification("user123");
            Assert.True(result!.Value.Success);
        }

        [Fact]
        public void RecordNotification_ShouldUpdateTime()
        {
            // Arrange
            var tracker = new DiscordNotificationTracker();

            // Act
            tracker.RecordNotification("user123", true);
            var firstTime = tracker.GetLastNotification("user123")!.Value.Time;

            System.Threading.Thread.Sleep(10); // Small delay
            tracker.RecordNotification("user123", false);
            var secondTime = tracker.GetLastNotification("user123")!.Value.Time;

            // Assert
            Assert.True(secondTime >= firstTime);
        }
    }

    public class DiscordInviteSenderTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<ILogger<DiscordInviteSender>> _mockLogger;
        private readonly Mock<IOptions<TwitchSettings>> _mockTwitchSettings;
        private readonly Mock<IDiscordNotificationTracker> _mockTracker;
        private readonly Mock<IUserRepository> _mockUserRepository;

        public DiscordInviteSenderTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockLogger = new Mock<ILogger<DiscordInviteSender>>();
            _mockTwitchSettings = new Mock<IOptions<TwitchSettings>>();
            _mockTracker = new Mock<IDiscordNotificationTracker>();
            _mockUserRepository = new Mock<IUserRepository>();

            _mockTwitchSettings.Setup(x => x.Value).Returns(new TwitchSettings { ClientId = "test" });

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository))).Returns(_mockUserRepository.Object);
        }

        [Fact]
        public async Task SendDiscordInviteAsync_WhenThrottled_ShouldNotSend()
        {
            // Arrange
            _mockTracker.Setup(x => x.GetLastNotification("123"))
                .Returns((DateTimeOffset.UtcNow.AddMinutes(-1), true)); // Within 5 minute throttle

            var sender = new DiscordInviteSender(
                _mockScopeFactory.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockTwitchSettings.Object,
                _mockTracker.Object);

            // Act
            await sender.SendDiscordInviteAsync("123");

            // Assert
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SendDiscordInviteAsync_WhenUserNotFound_ShouldNotSend()
        {
            // Arrange
            _mockTracker.Setup(x => x.GetLastNotification("123")).Returns((ValueTuple<DateTimeOffset, bool>?)null);
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync((User?)null);

            var sender = new DiscordInviteSender(
                _mockScopeFactory.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockTwitchSettings.Object,
                _mockTracker.Object);

            // Act
            await sender.SendDiscordInviteAsync("123");

            // Assert
            _mockTracker.Verify(x => x.RecordNotification(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task SendDiscordInviteAsync_WhenUserHasNoToken_ShouldNotSend()
        {
            // Arrange
            _mockTracker.Setup(x => x.GetLastNotification("123")).Returns((ValueTuple<DateTimeOffset, bool>?)null);
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(new User { TwitchUserId = "123" });

            var sender = new DiscordInviteSender(
                _mockScopeFactory.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockTwitchSettings.Object,
                _mockTracker.Object);

            // Act
            await sender.SendDiscordInviteAsync("123");

            // Assert
            _mockTracker.Verify(x => x.RecordNotification(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task SendDiscordInviteAsync_WhenSuccessful_ShouldRecordSuccess()
        {
            // Arrange
            _mockTracker.Setup(x => x.GetLastNotification("123")).Returns((ValueTuple<DateTimeOffset, bool>?)null);
            _mockUserRepository.Setup(x => x.GetUserAsync("123"))
                .ReturnsAsync(new User { TwitchUserId = "123", AccessToken = "token123" });

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var sender = new DiscordInviteSender(
                _mockScopeFactory.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockTwitchSettings.Object,
                _mockTracker.Object);

            // Act
            await sender.SendDiscordInviteAsync("123");

            // Assert
            _mockTracker.Verify(x => x.RecordNotification("123", true), Times.Once);
        }

        [Fact]
        public async Task SendDiscordInviteAsync_WhenFails_ShouldRecordFailure()
        {
            // Arrange
            _mockTracker.Setup(x => x.GetLastNotification("123")).Returns((ValueTuple<DateTimeOffset, bool>?)null);
            _mockUserRepository.Setup(x => x.GetUserAsync("123"))
                .ReturnsAsync(new User { TwitchUserId = "123", AccessToken = "token123" });

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.BadRequest });

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var sender = new DiscordInviteSender(
                _mockScopeFactory.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockTwitchSettings.Object,
                _mockTracker.Object);

            // Act
            await sender.SendDiscordInviteAsync("123");

            // Assert
            _mockTracker.Verify(x => x.RecordNotification("123", false), Times.Once);
        }

        [Fact]
        public async Task SendDiscordInviteAsync_WhenUserHasCustomDiscordLink_ShouldUseIt()
        {
            // Arrange
            _mockTracker.Setup(x => x.GetLastNotification("123")).Returns((ValueTuple<DateTimeOffset, bool>?)null);
            _mockUserRepository.Setup(x => x.GetUserAsync("123"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "123",
                    AccessToken = "token123",
                    DiscordInviteLink = "https://discord.gg/customlink"
                });

            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var sender = new DiscordInviteSender(
                _mockScopeFactory.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockTwitchSettings.Object,
                _mockTracker.Object);

            // Act
            await sender.SendDiscordInviteAsync("123");

            // Assert
            Assert.NotNull(capturedRequest);
            var content = await capturedRequest!.Content!.ReadAsStringAsync();
            Assert.Contains("customlink", content);
        }
    }
}
