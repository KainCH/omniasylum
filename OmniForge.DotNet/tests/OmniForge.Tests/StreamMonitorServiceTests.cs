using System;
using System.Collections.Generic;
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
using OmniForge.Infrastructure.Services;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using Xunit;

namespace OmniForge.Tests
{
    public class TestStreamOnlineArgs : StreamOnlineArgs
    {
        public object? Event { get; set; }
    }

    public class TestStreamOfflineArgs : StreamOfflineArgs
    {
        public object? Event { get; set; }
    }

    public class TestEventData
    {
        public string BroadcasterUserId { get; set; } = string.Empty;
        public string BroadcasterUserName { get; set; } = string.Empty;
    }

    public class StreamMonitorServiceTests
    {
        private readonly Mock<IEventSubWebsocketClientWrapper> _mockEventSubClient;
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

            // Setup TwitchAPI mock
            _mockTwitchApi = new Mock<TwitchAPI>(MockBehavior.Loose, null!, null!, _mockApiSettings.Object, null!);

            // Setup EventSub Client mock
            _mockEventSubClient = new Mock<IEventSubWebsocketClientWrapper>();
            _mockEventSubClient.Setup(x => x.SessionId).Returns("test_session_id");

            // Setup Settings
            _mockTwitchSettings.Setup(x => x.Value).Returns(new TwitchSettings
            {
                ClientId = "test_client",
                ClientSecret = "test_secret"
            });

            // Setup DI
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository))).Returns(_mockUserRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository))).Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IDiscordService))).Returns(_mockDiscordService.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ITwitchHelixWrapper))).Returns(_mockHelixWrapper.Object);

            _service = new StreamMonitorService(
                _mockEventSubClient.Object,
                _mockTwitchApi.Object,
                _mockLogger.Object,
                _mockScopeFactory.Object,
                _mockTwitchSettings.Object);
        }

        [Fact]
        public async Task StartAsync_ShouldConnectEventSub()
        {
            // Act
            await _service.StartAsync(CancellationToken.None);

            // Assert
            _mockEventSubClient.Verify(x => x.ConnectAsync(null), Times.Once);
        }

        [Fact]
        public async Task StopAsync_ShouldDisconnectEventSub()
        {
            // Act
            await _service.StopAsync(CancellationToken.None);

            // Assert
            _mockEventSubClient.Verify(x => x.DisconnectAsync(), Times.Once);
        }

        [Fact]
        public void OnWebsocketConnected_ShouldSubscribeToEvents()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "123", DisplayName = "User1", AccessToken = "token1" },
                new User { TwitchUserId = "456", DisplayName = "User2", AccessToken = "token2" }
            };
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            _mockEventSubClient.Raise(x => x.WebsocketConnected += null, new WebsocketConnectedArgs());

            // Assert
            _mockHelixWrapper.Verify(x => x.CreateEventSubSubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), "stream.online", "1",
                It.Is<Dictionary<string, string>>(d => d["broadcaster_user_id"] == "123"),
                EventSubTransportMethod.Websocket, "test_session_id"), Times.Once);

            _mockHelixWrapper.Verify(x => x.CreateEventSubSubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), "stream.offline", "1",
                It.Is<Dictionary<string, string>>(d => d["broadcaster_user_id"] == "123"),
                EventSubTransportMethod.Websocket, "test_session_id"), Times.Once);

            _mockUserRepository.Verify(x => x.GetAllUsersAsync(), Times.Once);
        }

        [Fact]
        public void OnWebsocketDisconnected_ShouldLogWarning()
        {
            // Act
            _mockEventSubClient.Raise(x => x.WebsocketDisconnected += null, new WebsocketDisconnectedArgs());

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Websocket disconnected")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void OnWebsocketReconnected_ShouldLogInformation()
        {
            // Act
            _mockEventSubClient.Raise(x => x.WebsocketReconnected += null, new WebsocketReconnectedArgs());

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Websocket reconnected")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void OnErrorOccurred_ShouldLogError()
        {
            // Arrange
            var exception = new Exception("Test exception");
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            var args = (ErrorOccuredArgs)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ErrorOccuredArgs));
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            typeof(ErrorOccuredArgs).GetProperty("Exception")?.SetValue(args, exception);
            typeof(ErrorOccuredArgs).GetProperty("Message")?.SetValue(args, "Test error");

            // Act
            _mockEventSubClient.Raise(x => x.ErrorOccurred += null, args);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Websocket error: Test error")),
                    exception,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void OnStreamOnline_ShouldUpdateCounterAndNotifyDiscord()
        {
            // Arrange
            var userId = "123";
            var userName = "TestUser";

            var eventData = new TestEventData
            {
                BroadcasterUserId = userId,
                BroadcasterUserName = userName
            };

            var args = new TestStreamOnlineArgs
            {
                Event = eventData
            };

            // Mock User Repository
            var user = new User { TwitchUserId = userId, DisplayName = userName };
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // Mock Counter Repository
            var counters = new Counter { TwitchUserId = userId };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            // Act
            _mockEventSubClient.Raise(x => x.StreamOnline += null, args);

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

            var eventData = new TestEventData
            {
                BroadcasterUserId = userId,
                BroadcasterUserName = userName
            };

            var args = new TestStreamOfflineArgs
            {
                Event = eventData
            };

            // Mock User Repository
            var user = new User { TwitchUserId = userId, DisplayName = userName };
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // Mock Counter Repository
            var counters = new Counter { TwitchUserId = userId, StreamStarted = DateTimeOffset.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            // Act
            _mockEventSubClient.Raise(x => x.StreamOffline += null, args);

            // Assert
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted == null)), Times.Once);
        }

        [Fact]
        public void OnWebsocketConnected_ShouldNotSubscribe_WhenReconnectRequested()
        {
            // Arrange
            var args = new WebsocketConnectedArgs { IsRequestedReconnect = true };

            // Act
            _mockEventSubClient.Raise(x => x.WebsocketConnected += null, args);

            // Assert
            _mockUserRepository.Verify(x => x.GetAllUsersAsync(), Times.Never);
        }

        [Fact]
        public void OnWebsocketConnected_ShouldLogError_WhenSubscriptionFails()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "123", DisplayName = "User1", AccessToken = "token1" }
            };
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);
            _mockHelixWrapper.Setup(x => x.CreateEventSubSubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<EventSubTransportMethod>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Subscription failed"));

            // Act
            _mockEventSubClient.Raise(x => x.WebsocketConnected += null, new WebsocketConnectedArgs());

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to subscribe to events for user User1")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void OnStreamOnline_ShouldHandleNotificationPayload()
        {
            // Arrange
            var userId = "123";
            var userName = "TestUser";

            dynamic eventData = new System.Dynamic.ExpandoObject();
            eventData.BroadcasterUserId = userId;
            eventData.BroadcasterUserName = userName;

            dynamic payload = new System.Dynamic.ExpandoObject();
            payload.Event = eventData;

            dynamic notification = new System.Dynamic.ExpandoObject();
            notification.Payload = payload;

            var args = new TestStreamOnlineArgsWithNotification
            {
                Notification = notification
            };

            // Mock User Repository
            var user = new User { TwitchUserId = userId, DisplayName = userName };
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(new Counter { TwitchUserId = userId });

            // Act
            _mockEventSubClient.Raise(x => x.StreamOnline += null, args);

            // Assert
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted != null)), Times.Once);
        }

        [Fact]
        public void OnStreamOffline_ShouldHandleNotificationPayload()
        {
            // Arrange
            var userId = "123";
            var userName = "TestUser";

            dynamic eventData = new System.Dynamic.ExpandoObject();
            eventData.BroadcasterUserId = userId;
            eventData.BroadcasterUserName = userName;

            dynamic payload = new System.Dynamic.ExpandoObject();
            payload.Event = eventData;

            dynamic notification = new System.Dynamic.ExpandoObject();
            notification.Payload = payload;

            var args = new TestStreamOfflineArgsWithNotification
            {
                Notification = notification
            };

            // Mock User Repository
            var user = new User { TwitchUserId = userId, DisplayName = userName };
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(new Counter { TwitchUserId = userId, StreamStarted = DateTimeOffset.UtcNow });

            // Act
            _mockEventSubClient.Raise(x => x.StreamOffline += null, args);

            // Assert
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted == null)), Times.Once);
        }

        [Fact]
        public void OnStreamOnline_ShouldLogWarning_WhenBroadcasterIdMissing()
        {
            // Arrange
            var args = new TestStreamOnlineArgs(); // No Event or Notification set

            // Act
            _mockEventSubClient.Raise(x => x.StreamOnline += null, args);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Could not extract broadcaster ID")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void OnWebsocketConnected_ShouldContinue_WhenSubscriptionFailsForOneUser()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "123", DisplayName = "User1", AccessToken = "token1" },
                new User { TwitchUserId = "456", DisplayName = "User2", AccessToken = "token2" }
            };
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

            // Fail for User1
            _mockHelixWrapper.Setup(x => x.CreateEventSubSubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.Is<Dictionary<string, string>>(d => d["broadcaster_user_id"] == "123"),
                It.IsAny<EventSubTransportMethod>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Subscription failed"));

            // Succeed for User2
            _mockHelixWrapper.Setup(x => x.CreateEventSubSubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.Is<Dictionary<string, string>>(d => d["broadcaster_user_id"] == "456"),
                It.IsAny<EventSubTransportMethod>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // Act
            _mockEventSubClient.Raise(x => x.WebsocketConnected += null, new WebsocketConnectedArgs());

            // Assert
            // Should log error for User1
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to subscribe to events for user User1")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            // Should subscribe for User2 (called twice: online and offline)
            _mockHelixWrapper.Verify(x => x.CreateEventSubSubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), "stream.online", "1",
                It.Is<Dictionary<string, string>>(d => d["broadcaster_user_id"] == "456"),
                EventSubTransportMethod.Websocket, "test_session_id"), Times.Once);
        }
    }

    public class TestStreamOnlineArgsWithNotification : StreamOnlineArgs
    {
        public object? Notification { get; set; }
    }

    public class TestStreamOfflineArgsWithNotification : StreamOfflineArgs
    {
        public object? Notification { get; set; }
    }
}
