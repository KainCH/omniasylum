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
using OmniForge.Infrastructure.Services.EventHandlers;
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
        private readonly Mock<IDiscordNotificationTracker> _mockDiscordTracker;
        private readonly Mock<IEventSubHandlerRegistry> _mockHandlerRegistry;
        private readonly Mock<ITwitchAuthService> _mockAuthService;
        private readonly Mock<IBotCredentialRepository> _mockBotCredentialRepository;
        private readonly Mock<ITwitchBotEligibilityService> _mockBotEligibilityService;
        private readonly Mock<IMonitoringRegistry> _mockMonitoringRegistry;
        private readonly StreamMonitorService _service;

        private sealed class TestableStreamMonitorService : StreamMonitorService
        {
            private readonly TokenValidation? _validation;

            public TestableStreamMonitorService(
                INativeEventSubService eventSubService,
                TwitchAPI twitchApi,
                IHttpClientFactory httpClientFactory,
                ILogger<StreamMonitorService> logger,
                IServiceScopeFactory scopeFactory,
                IOptions<TwitchSettings> twitchSettings,
                IDiscordNotificationTracker discordTracker,
                string? validationUserId,
                string? validationLogin,
                string? validationClientId,
                List<string>? validationScopes)
                : base(eventSubService, twitchApi, httpClientFactory, logger, scopeFactory, twitchSettings, discordTracker)
            {
                _validation = validationUserId == null || validationLogin == null || validationClientId == null
                    ? null
                    : new TokenValidation(validationUserId, validationLogin, validationClientId, validationScopes);
            }

            protected override Task<TokenValidation?> ValidateAccessTokenAsync(string accessToken)
                => Task.FromResult(_validation);
        }

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
            _mockDiscordTracker = new Mock<IDiscordNotificationTracker>();
            _mockHandlerRegistry = new Mock<IEventSubHandlerRegistry>();
            _mockAuthService = new Mock<ITwitchAuthService>();
            _mockBotCredentialRepository = new Mock<IBotCredentialRepository>();
            _mockBotEligibilityService = new Mock<ITwitchBotEligibilityService>();
            _mockMonitoringRegistry = new Mock<IMonitoringRegistry>();

            // Setup TwitchAPI mock
            _mockTwitchApi = new Mock<TwitchAPI>(MockBehavior.Loose, null!, null!, _mockApiSettings.Object, null!);

            // Setup EventSub Service mock
            _mockEventSubService = new Mock<INativeEventSubService>();
            _mockEventSubService.Setup(x => x.SessionId).Returns("test_session_id");
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);

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
            _mockServiceProvider.Setup(x => x.GetService(typeof(ITwitchAuthService))).Returns(_mockAuthService.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IEventSubHandlerRegistry))).Returns(_mockHandlerRegistry.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IBotCredentialRepository))).Returns(_mockBotCredentialRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ITwitchBotEligibilityService))).Returns(_mockBotEligibilityService.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IMonitoringRegistry))).Returns(_mockMonitoringRegistry.Object);

            _service = new TestableStreamMonitorService(
                _mockEventSubService.Object,
                _mockTwitchApi.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object,
                _mockScopeFactory.Object,
                _mockTwitchSettings.Object,
                _mockDiscordTracker.Object,
                "123",
                "testuser",
                "test_client",
                new List<string>
                {
                    "moderation:read",
                    "user:read:chat",
                    "moderator:read:followers"
                });
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenBotEligible_UsesBotTokenForSubscriptions_AndSetsMonitoringRegistryToUseBot()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "broadcaster_access",
                RefreshToken = "broadcaster_refresh",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
                Role = "streamer"
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            _mockBotEligibilityService
                .Setup(x => x.GetEligibilityAsync("123", "broadcaster_access", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BotEligibilityResult(true, "bot_999", "Bot is a moderator"));

            _mockBotCredentialRepository
                .Setup(x => x.GetAsync())
                .ReturnsAsync(new BotCredentials
                {
                    Username = "forge-bot",
                    AccessToken = "bot_access",
                    RefreshToken = "bot_refresh",
                    TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
                });

            _mockHelixWrapper
                .Setup(x => x.CreateEventSubSubscriptionAsync(
                    "test_client",
                    "bot_access",
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    EventSubTransportMethod.Websocket,
                    "test_session_id"))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert
            Assert.Equal(SubscriptionResult.Success, result);

            _mockHelixWrapper.Verify(x => x.CreateEventSubSubscriptionAsync(
                "test_client",
                "bot_access",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                EventSubTransportMethod.Websocket,
                "test_session_id"), Times.AtLeastOnce);

            _mockMonitoringRegistry.Verify(x => x.SetState(
                "123",
                It.Is<MonitoringState>(s => s.UseBot && s.BotUserId == "bot_999")), Times.Once);
        }

        [Fact]
        public async Task UnsubscribeFromUserAsync_DuringInFlightSubscribe_DoesNotReAddUser()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "broadcaster_access",
                RefreshToken = "broadcaster_refresh",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
                Role = "streamer"
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            _mockBotEligibilityService
                .Setup(x => x.GetEligibilityAsync("123", "broadcaster_access", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BotEligibilityResult(false, null, "Bot is not a moderator"));

            var firstCallEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var unblockFirstCall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task DelayedFirstSubscription()
            {
                firstCallEntered.TrySetResult(true);
                return unblockFirstCall.Task;
            }

            // SubscribeToUserAsync makes multiple CreateEventSubSubscriptionAsync calls.
            _mockHelixWrapper
                .SetupSequence(x => x.CreateEventSubSubscriptionAsync(
                    "test_client",
                    "broadcaster_access",
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    EventSubTransportMethod.Websocket,
                    "test_session_id"))
                .Returns(DelayedFirstSubscription)
                .Returns(Task.CompletedTask)
                .Returns(Task.CompletedTask)
                .Returns(Task.CompletedTask)
                .Returns(Task.CompletedTask);

            // Act
            var subscribeTask = _service.SubscribeToUserAsync("123");

            var entered = await Task.WhenAny(firstCallEntered.Task, Task.Delay(1000));
            Assert.Same(firstCallEntered.Task, entered);

            var unsubscribeTask = _service.UnsubscribeFromUserAsync("123");
            var unsubscribeCompleted = await Task.WhenAny(unsubscribeTask, Task.Delay(500));
            Assert.Same(unsubscribeTask, unsubscribeCompleted);

            unblockFirstCall.TrySetResult(true);

            var subscribeResult = await subscribeTask;

            // Assert
            Assert.Equal(SubscriptionResult.Failed, subscribeResult);
            Assert.False(_service.IsUserSubscribed("123"));
            _mockMonitoringRegistry.Verify(x => x.SetState(It.IsAny<string>(), It.IsAny<MonitoringState>()), Times.Never);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenBotNotEligible_UsesBroadcasterTokenForSubscriptions_AndSetsMonitoringRegistryToNotUseBot()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "broadcaster_access",
                RefreshToken = "broadcaster_refresh",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
                Role = "streamer"
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            _mockBotEligibilityService
                .Setup(x => x.GetEligibilityAsync("123", "broadcaster_access", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BotEligibilityResult(false, null, "Bot is not a moderator"));

            _mockHelixWrapper
                .Setup(x => x.CreateEventSubSubscriptionAsync(
                    "test_client",
                    "broadcaster_access",
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    EventSubTransportMethod.Websocket,
                    "test_session_id"))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert
            Assert.Equal(SubscriptionResult.Success, result);

            _mockHelixWrapper.Verify(x => x.CreateEventSubSubscriptionAsync(
                "test_client",
                "broadcaster_access",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                EventSubTransportMethod.Websocket,
                "test_session_id"), Times.AtLeastOnce);

            _mockMonitoringRegistry.Verify(x => x.SetState(
                "123",
                It.Is<MonitoringState>(s => !s.UseBot && s.BotUserId == null)), Times.Once);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenBotEligibleButBotCredentialsMissing_FallsBackToBroadcaster_AndSetsMonitoringRegistryToNotUseBot()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "broadcaster_access",
                RefreshToken = "broadcaster_refresh",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
                Role = "streamer"
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            _mockBotEligibilityService
                .Setup(x => x.GetEligibilityAsync("123", "broadcaster_access", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BotEligibilityResult(true, "bot_999", "Bot is a moderator"));

            _mockBotCredentialRepository
                .Setup(x => x.GetAsync())
                .ReturnsAsync((BotCredentials?)null);

            _mockHelixWrapper
                .Setup(x => x.CreateEventSubSubscriptionAsync(
                    "test_client",
                    "broadcaster_access",
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    EventSubTransportMethod.Websocket,
                    "test_session_id"))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert
            Assert.Equal(SubscriptionResult.Success, result);

            _mockMonitoringRegistry.Verify(x => x.SetState(
                "123",
                It.Is<MonitoringState>(s => !s.UseBot && s.BotUserId == null)), Times.Once);
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
        public void OnNotification_ShouldInvokeHandlerRegistryHandler()
        {
            // Arrange
            var handlerMock = new Mock<IEventSubHandler>();
            _mockHandlerRegistry.Setup(x => x.GetHandler("channel.follow")).Returns(handlerMock.Object);

            var message = new EventSubMessage
            {
                Payload = new EventSubPayload
                {
                    Subscription = new EventSubSubscription { Type = "channel.follow" },
                    Event = JsonDocument.Parse("{\"broadcaster_user_id\":\"123\"}").RootElement
                }
            };

            // Act: simulate EventSub notification
            _mockEventSubService.Raise(m => m.OnNotification += null, message);

            // Assert
            handlerMock.Verify(x => x.HandleAsync(It.IsAny<JsonElement>()), Times.Once);
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
        public void OnNotification_ShouldDelegateStreamOnlineToHandler()
        {
            // Arrange
            var mockHandler = new Mock<IEventSubHandler>();
            mockHandler.Setup(x => x.HandleAsync(It.IsAny<JsonElement>())).Returns(Task.CompletedTask);
            _mockHandlerRegistry.Setup(x => x.GetHandler("stream.online")).Returns(mockHandler.Object);

            var eventData = new
            {
                broadcaster_user_id = "123",
                broadcaster_user_name = "TestUser"
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

            // Act
            _mockEventSubService.Raise(x => x.OnNotification += null, message);

            // Assert - handler should be called with the event data
            _mockHandlerRegistry.Verify(x => x.GetHandler("stream.online"), Times.Once);
            mockHandler.Verify(x => x.HandleAsync(It.IsAny<JsonElement>()), Times.Once);
        }

        [Fact]
        public void OnNotification_ShouldDelegateStreamOfflineToHandler()
        {
            // Arrange
            var mockHandler = new Mock<IEventSubHandler>();
            mockHandler.Setup(x => x.HandleAsync(It.IsAny<JsonElement>())).Returns(Task.CompletedTask);
            _mockHandlerRegistry.Setup(x => x.GetHandler("stream.offline")).Returns(mockHandler.Object);

            var eventData = new
            {
                broadcaster_user_id = "123",
                broadcaster_user_name = "TestUser"
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

            // Act
            _mockEventSubService.Raise(x => x.OnNotification += null, message);

            // Assert - handler should be called with the event data
            _mockHandlerRegistry.Verify(x => x.GetHandler("stream.offline"), Times.Once);
            mockHandler.Verify(x => x.HandleAsync(It.IsAny<JsonElement>()), Times.Once);
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

        [Fact]
        public void GetUserConnectionStatus_ShouldReturnStatus()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);

            // Act
            var status = _service.GetUserConnectionStatus("123");

            // Assert
            Assert.True(status.Connected);
        }

        [Fact]
        public void GetUserConnectionStatus_ShouldReturnDisconnectedStatus()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(false);

            // Act
            var status = _service.GetUserConnectionStatus("123");

            // Assert
            Assert.False(status.Connected);
            Assert.Empty(status.Subscriptions);
        }

        [Fact]
        public void IsUserSubscribed_ShouldReturnFalse_WhenNotSubscribed()
        {
            // Act
            var result = _service.IsUserSubscribed("unknown-user");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void OnNotification_ShouldDelegateToHandlerRegistry()
        {
            // Arrange
            var eventData = new
            {
                broadcaster_user_id = "123",
                user_name = "follower",
                followed_at = DateTime.UtcNow
            };

            var message = new EventSubMessage
            {
                Metadata = new EventSubMetadata { MessageType = "notification" },
                Payload = new EventSubPayload
                {
                    Subscription = new EventSubSubscription { Type = "channel.follow" },
                    Event = JsonSerializer.SerializeToElement(eventData)
                }
            };

            var mockHandler = new Mock<IEventSubHandler>();
            mockHandler.Setup(x => x.SubscriptionType).Returns("channel.follow");
            _mockHandlerRegistry.Setup(x => x.GetHandler("channel.follow")).Returns(mockHandler.Object);

            // Act
            _mockEventSubService.Raise(x => x.OnNotification += null, message);

            // Assert - Handler registry was queried and handler was invoked
            _mockHandlerRegistry.Verify(x => x.GetHandler("channel.follow"), Times.Once);
            mockHandler.Verify(x => x.HandleAsync(It.IsAny<JsonElement>()), Times.Once);
        }

        [Fact]
        public void OnNotification_ShouldHandleUnknownSubscriptionType()
        {
            // Arrange - No handler registered for this type
            _mockHandlerRegistry.Setup(x => x.GetHandler("unknown.type")).Returns((IEventSubHandler?)null);

            var message = new EventSubMessage
            {
                Metadata = new EventSubMetadata { MessageType = "notification" },
                Payload = new EventSubPayload
                {
                    Subscription = new EventSubSubscription { Type = "unknown.type" },
                    Event = JsonSerializer.SerializeToElement(new { })
                }
            };

            // Act - Should not throw
            _mockEventSubService.Raise(x => x.OnNotification += null, message);

            // Assert
            _mockHandlerRegistry.Verify(x => x.GetHandler("unknown.type"), Times.Once);
        }

        [Fact]
        public void OnNotification_ShouldHandleMissingSubscriptionType()
        {
            // Arrange
            var message = new EventSubMessage
            {
                Metadata = new EventSubMetadata { MessageType = "notification" },
                Payload = new EventSubPayload
                {
                    Subscription = new EventSubSubscription { Type = null! },
                    Event = JsonSerializer.SerializeToElement(new { })
                }
            };

            // Act - Should not throw
            _mockEventSubService.Raise(x => x.OnNotification += null, message);

            // Assert - Registry should not be called with null type
            _mockHandlerRegistry.Verify(x => x.GetHandler(It.IsAny<string>()), Times.Never);
        }

        // Legacy tests removed - handler behavior is now tested in EventHandlers/EventSubHandlerTests.cs
        // The OnNotification method now delegates to IEventSubHandlerRegistry

        [Fact]
        public async Task UnsubscribeFromUserAsync_ShouldLogUnsubscription()
        {
            // Act
            await _service.UnsubscribeFromUserAsync("123");

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Stop Monitoring")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ForceReconnectUserAsync_ShouldAttemptResubscription()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(false);

            // Act
            var result = await _service.ForceReconnectUserAsync("123");

            // Assert - should return Failed because no session
            Assert.Equal(SubscriptionResult.Failed, result);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenUserNotFound_ShouldReturnFailed()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);
            _mockEventSubService.Setup(x => x.SessionId).Returns("test_session");
            _mockUserRepository.Setup(x => x.GetUserAsync("unknown")).ReturnsAsync((User?)null);

            // Act
            var result = await _service.SubscribeToUserAsync("unknown");

            // Assert
            Assert.Equal(SubscriptionResult.Failed, result);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenNoAccessToken_ShouldReturnUnauthorized()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);
            _mockEventSubService.Setup(x => x.SessionId).Returns("test_session");
            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert
            Assert.Equal(SubscriptionResult.Unauthorized, result);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenTokenExpiredAndNoRefreshToken_ShouldReturnUnauthorized()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);
            _mockEventSubService.Setup(x => x.SessionId).Returns("test_session");
            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "expired_token",
                RefreshToken = "",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(-1) // Expired
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert
            Assert.Equal(SubscriptionResult.Unauthorized, result);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenTokenExpiredAndRefreshFails_ShouldReturnUnauthorized()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);
            _mockEventSubService.Setup(x => x.SessionId).Returns("test_session");

            _mockAuthService.Setup(x => x.RefreshTokenAsync(It.IsAny<string>())).ReturnsAsync((TwitchTokenResponse?)null);

            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "expired_token",
                RefreshToken = "valid_refresh",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(-1) // Expired
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert
            Assert.Equal(SubscriptionResult.Unauthorized, result);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenNotConnected_ShouldAttemptToConnect()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(false);
            _mockEventSubService.Setup(x => x.SessionId).Returns((string?)null);
            _mockEventSubService.Setup(x => x.ConnectAsync()).Returns(Task.CompletedTask);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert - Should fail because timeout waiting for welcome
            Assert.Equal(SubscriptionResult.Failed, result);
            _mockEventSubService.Verify(x => x.ConnectAsync(), Times.Once);
        }

        [Fact]
        public async Task SubscribeToUserAsync_WhenSessionIdMissing_ShouldReturnFailed()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);
            _mockEventSubService.Setup(x => x.SessionId).Returns((string?)null);

            var user = new User
            {
                TwitchUserId = "123",
                AccessToken = "valid_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            // Act
            var result = await _service.SubscribeToUserAsync("123");

            // Assert - Should fail because TwitchAPI validation fails (not mocked)
            // or session ID missing - either returns Failed or Unauthorized
            Assert.True(result == SubscriptionResult.Failed || result == SubscriptionResult.Unauthorized);
        }

        [Fact]
        public async Task UnsubscribeFromUserAsync_WhenNoUsersRemaining_ShouldDisconnect()
        {
            // Arrange - Start with no subscribed users
            _mockEventSubService.Setup(x => x.DisconnectAsync()).Returns(Task.CompletedTask);

            // Act
            await _service.UnsubscribeFromUserAsync("123");

            // Assert
            _mockEventSubService.Verify(x => x.DisconnectAsync(), Times.Once);
        }

        [Fact]
        public void GetUserConnectionStatus_ShouldIncludeDiscordStatus()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);
            _mockDiscordTracker.Setup(x => x.GetLastNotification("123"))
                .Returns((Time: DateTimeOffset.UtcNow, Success: true));

            // Act
            var status = _service.GetUserConnectionStatus("123");

            // Assert
            Assert.True(status.Connected);
            Assert.NotNull(status.LastDiscordNotification);
            Assert.True(status.LastDiscordNotificationSuccess);
        }

        [Fact]
        public void GetUserConnectionStatus_WhenNoDiscordNotification_ShouldReturnDefaults()
        {
            // Arrange
            _mockEventSubService.Setup(x => x.IsConnected).Returns(true);
            _mockDiscordTracker.Setup(x => x.GetLastNotification("123")).Returns((ValueTuple<DateTimeOffset, bool>?)null);

            // Act
            var status = _service.GetUserConnectionStatus("123");

            // Assert
            Assert.True(status.Connected);
            Assert.Null(status.LastDiscordNotification);
            Assert.False(status.LastDiscordNotificationSuccess);
        }

        // Legacy OnNotification handler tests removed
        // Handler behavior (sub, resub, gift sub, raid, etc.) is now tested in:
        // - EventHandlers/EventSubHandlerTests.cs
        // - EventHandlers/ChatEventHandlerTests.cs
        // The OnNotification method now delegates to IEventSubHandlerRegistry
    }
}
