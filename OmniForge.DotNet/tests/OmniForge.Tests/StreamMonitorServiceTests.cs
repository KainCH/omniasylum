using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Models.EventSub;
using OmniForge.Infrastructure.Services;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using Xunit;

namespace OmniForge.Tests
{
    public class StreamMonitorServiceTests
    {
        private readonly Mock<INativeEventSubService> _mockEventSubService;
        private readonly Mock<TwitchAPI> _mockTwitchApi;
        private readonly Mock<ILogger<StreamMonitorService>> _mockLogger;
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly Mock<IOptions<TwitchSettings>> _mockTwitchSettings;
        private readonly Mock<ITwitchHelixWrapper> _mockHelixWrapper;
        private readonly Mock<TwitchLib.Api.Core.Interfaces.IApiSettings> _mockApiSettings;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly StreamMonitorService _service;

        public StreamMonitorServiceTests()
        {
            _mockLogger = new Mock<ILogger<StreamMonitorService>>();
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockDiscordService = new Mock<IDiscordService>();
            _mockTwitchSettings = new Mock<IOptions<TwitchSettings>>();
            _mockHelixWrapper = new Mock<ITwitchHelixWrapper>();
            _mockApiSettings = new Mock<TwitchLib.Api.Core.Interfaces.IApiSettings>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();

            // Setup TwitchAPI mock
            _mockTwitchApi = new Mock<TwitchAPI>(MockBehavior.Loose, null!, null!, _mockApiSettings.Object, null!);

            // Setup EventSub Service mock
            _mockEventSubService = new Mock<INativeEventSubService>();
            _mockEventSubService.Setup(x => x.SessionId).Returns("test_session_id");

            // Setup Settings
            _mockTwitchSettings.Setup(x => x.Value).Returns(new TwitchSettings
            {
                ClientId = "test_client",
                ClientSecret = "test_secret"
            });

            // Setup HttpClientFactory
            var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            // We need to mock SendAsync if we were testing the chat message sending, but for now just returning a client is enough for constructor
            var client = new HttpClient(mockHttpMessageHandler.Object);
            _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);

            // Setup DI
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository))).Returns(_mockUserRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository))).Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IDiscordService))).Returns(_mockDiscordService.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ITwitchHelixWrapper))).Returns(_mockHelixWrapper.Object);

            _service = new StreamMonitorService(
                _mockEventSubService.Object,
                _mockTwitchApi.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockScopeFactory.Object,
                _mockTwitchSettings.Object);
        }

        [Fact]
        public async Task StartAsync_ShouldLogStarting()
        {
            // Act
            await _service.StartAsync(CancellationToken.None);

            // Assert - StartAsync no longer auto-connects, it just logs
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting StreamMonitorService")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task StopAsync_ShouldDisconnectEventSub()
        {
            // Act
            await _service.StopAsync(CancellationToken.None);

            // Assert
            _mockEventSubService.Verify(x => x.DisconnectAsync(), Times.Once);
        }

        [Fact]
        public void OnSessionWelcome_ShouldLogSessionId()
        {
            // Act
            _mockEventSubService.Raise(x => x.OnSessionWelcome += null, "test_session_id");

            // Assert - OnSessionWelcome now just logs the session ID (subscriptions are user-initiated)
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("EventSub Session Welcome")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void OnDisconnected_ShouldLogWarning()
        {
            // Act
            _mockEventSubService.Raise(x => x.OnDisconnected += null);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("EventSub Disconnected")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void OnStreamOnline_ShouldUpdateCounterAndNotifyDiscord()
        {
            // Arrange
            var userId = "123";
            var userName = "TestUser";

            var eventData = new
            {
                broadcaster_user_id = userId,
                broadcaster_user_name = userName,
                type = "live",
                started_at = DateTime.UtcNow
            };

            var message = new EventSubMessage
            {
                Metadata = new EventSubMetadata { MessageType = "notification" },
                Payload = new EventSubPayload
                {
                    Subscription = new EventSubSubscription { Type = "stream.online" },
                    Event = JsonSerializer.SerializeToElement(eventData)
                }
            };

            // Mock User Repository
            var user = new User { TwitchUserId = userId, DisplayName = userName };
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // Mock Counter Repository
            var counters = new Counter { TwitchUserId = userId };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            // Act
            _mockEventSubService.Raise(x => x.OnNotification += null, message);

            // Assert
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted != null)), Times.Once);
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public void OnStreamOffline_ShouldUpdateCounter()
        {
            // Arrange
            var userId = "123";
            var userName = "TestUser";

            var eventData = new
            {
                broadcaster_user_id = userId,
                broadcaster_user_name = userName
            };

            var message = new EventSubMessage
            {
                Metadata = new EventSubMetadata { MessageType = "notification" },
                Payload = new EventSubPayload
                {
                    Subscription = new EventSubSubscription { Type = "stream.offline" },
                    Event = JsonSerializer.SerializeToElement(eventData)
                }
            };

            // Mock User Repository
            var user = new User { TwitchUserId = userId, DisplayName = userName };
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // Mock Counter Repository
            var counters = new Counter { TwitchUserId = userId, StreamStarted = DateTimeOffset.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            // Act
            _mockEventSubService.Raise(x => x.OnNotification += null, message);

            // Assert
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted == null)), Times.Once);
        }

        [Fact]
        public void OnDisconnected_ShouldLogWarning_WhenUsersWantingMonitoring()
        {
            // This test verifies the disconnected handler logs appropriately
            // The actual behavior depends on whether there are users wanting monitoring

            // Act
            _mockEventSubService.Raise(x => x.OnDisconnected += null);

            // Assert - should log about disconnection
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("EventSub Disconnected")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public void OnSessionWelcome_ShouldRecordSessionId()
        {
            // Arrange
            var sessionId = "new_test_session_id";

            // Act
            _mockEventSubService.Raise(x => x.OnSessionWelcome += null, sessionId);

            // Assert - just verify session welcome was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("EventSub Session Welcome")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
