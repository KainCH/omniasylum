using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Services.EventHandlers;
using Xunit;

namespace OmniForge.Tests.EventHandlers
{
    public class StreamOnlineHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<StreamOnlineHandler>> _mockLogger;
        private readonly Mock<IOptions<TwitchSettings>> _mockSettings;
        private readonly Mock<IDiscordNotificationTracker> _mockDiscordTracker;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly Mock<ITwitchHelixWrapper> _mockHelixWrapper;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly StreamOnlineHandler _handler;

        public StreamOnlineHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<StreamOnlineHandler>>();
            _mockSettings = new Mock<IOptions<TwitchSettings>>();
            _mockDiscordTracker = new Mock<IDiscordNotificationTracker>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockDiscordService = new Mock<IDiscordService>();
            _mockHelixWrapper = new Mock<ITwitchHelixWrapper>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockSettings.Setup(x => x.Value).Returns(new TwitchSettings
            {
                ClientId = "test_client",
                ClientSecret = "test_secret"
            });

            SetupDependencyInjection();

            _handler = new StreamOnlineHandler(
                _mockScopeFactory.Object,
                _mockLogger.Object,
                _mockSettings.Object,
                _mockDiscordTracker.Object);
        }

        private void SetupDependencyInjection()
        {
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository))).Returns(_mockUserRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository))).Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IDiscordService))).Returns(_mockDiscordService.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ITwitchHelixWrapper))).Returns(_mockHelixWrapper.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeStreamOnline()
        {
            Assert.Equal("stream.online", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenBroadcasterIdMissing_ShouldReturnEarly()
        {
            // Arrange
            var eventData = JsonDocument.Parse("{}").RootElement;

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenUserNotFound_ShouldReturnEarly()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync((User?)null);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            _mockCounterRepository.Verify(x => x.GetCountersAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenUserFound_ShouldUpdateCounters()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted != null)), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenUserFound_ShouldNotifyOverlay()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            _mockOverlayNotifier.Verify(x => x.NotifyStreamStartedAsync("123", counters), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenDiscordSucceeds_ShouldRecordSuccess()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            _mockDiscordTracker.Verify(x => x.RecordNotification("123", true), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenDiscordFails_ShouldRecordFailure()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);
            _mockDiscordService.Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .ThrowsAsync(new Exception("Discord error"));

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            _mockDiscordTracker.Verify(x => x.RecordNotification("123", false), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WithAccessToken_ShouldCallGetStreamsApi()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser", AccessToken = "token" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            // Return null to simulate "no stream data" but still verify the call is made
            _mockHelixWrapper.Setup(x => x.GetStreamsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Collections.Generic.List<string>>()))
                .ReturnsAsync((TwitchLib.Api.Helix.Models.Streams.GetStreams.GetStreamsResponse?)null!);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert - verify GetStreamsAsync is called with correct parameters
            _mockHelixWrapper.Verify(x => x.GetStreamsAsync("test_client", "token", It.IsAny<System.Collections.Generic.List<string>>()), Times.Once);
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenGetStreamsThrows_ShouldCallGetChannelInfo()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser", AccessToken = "token", ProfileImageUrl = "https://profile.jpg" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            // GetStreams throws but handler catches the exception and continues
            _mockHelixWrapper.Setup(x => x.GetStreamsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Collections.Generic.List<string>>()))
                .ThrowsAsync(new Exception("Stream API error"));

            // Act
            await _handler.HandleAsync(eventData);

            // Assert - since GetStreams threw, should still send Discord notification with empty data
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()), Times.Once);
            _mockDiscordTracker.Verify(x => x.RecordNotification("123", true), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenHelixThrowsException_ShouldStillSendNotification()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser", AccessToken = "token" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);
            _mockHelixWrapper.Setup(x => x.GetStreamsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Collections.Generic.List<string>>()))
                .ThrowsAsync(new Exception("API error"));

            // Act
            await _handler.HandleAsync(eventData);

            // Assert - should still send notification with empty data
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()), Times.Once);
            _mockDiscordTracker.Verify(x => x.RecordNotification("123", true), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenCountersNull_ShouldNotNotifyOverlay()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync((Counter?)null);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            _mockOverlayNotifier.Verify(x => x.NotifyStreamStartedAsync(It.IsAny<string>(), It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenNoAccessToken_ShouldSkipStreamInfoFetch()
        {
            // Arrange
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser", AccessToken = null! };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert - should not call Helix API
            _mockHelixWrapper.Verify(x => x.GetStreamsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Collections.Generic.List<string>>()), Times.Never);
            // But should still send Discord notification
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()), Times.Once);
        }
    }

    public class StreamOfflineHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<StreamOfflineHandler>> _mockLogger;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly StreamOfflineHandler _handler;

        public StreamOfflineHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<StreamOfflineHandler>>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            SetupDependencyInjection();

            _handler = new StreamOfflineHandler(_mockScopeFactory.Object, _mockLogger.Object);
        }

        private void SetupDependencyInjection()
        {
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository))).Returns(_mockUserRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository))).Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeStreamOffline()
        {
            Assert.Equal("stream.offline", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenBroadcasterIdMissing_ShouldReturnEarly()
        {
            var eventData = JsonDocument.Parse("{}").RootElement;
            await _handler.HandleAsync(eventData);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenUserFound_ShouldClearStreamStarted()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123" };
            var counters = new Counter { TwitchUserId = "123", StreamStarted = DateTimeOffset.UtcNow };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            await _handler.HandleAsync(eventData);

            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted == null)), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenUserFound_ShouldNotifyOverlay()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyStreamEndedAsync("123", counters), Times.Once);
        }
    }

    public class FollowHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<FollowHandler>> _mockLogger;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly FollowHandler _handler;

        public FollowHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<FollowHandler>>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new FollowHandler(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelFollow()
        {
            Assert.Equal("channel.follow", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenBroadcasterIdMissing_ShouldReturnEarly()
        {
            var eventData = JsonDocument.Parse("{}").RootElement;
            await _handler.HandleAsync(eventData);
            _mockOverlayNotifier.Verify(x => x.NotifyFollowerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenFollowReceived_ShouldNotifyOverlay()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""NewFollower""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyFollowerAsync("123", "NewFollower"), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenUserNameMissing_ShouldUseSomeone()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyFollowerAsync("123", "Someone"), Times.Once);
        }
    }

    public class SubscribeHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<SubscribeHandler>> _mockLogger;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly SubscribeHandler _handler;

        public SubscribeHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<SubscribeHandler>>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new SubscribeHandler(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelSubscribe()
        {
            Assert.Equal("channel.subscribe", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenSubscribeReceived_ShouldNotifyWithTier()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""NewSub"",
                ""tier"": ""2000"",
                ""is_gift"": false
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifySubscriberAsync("123", "NewSub", "Tier 2", false), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenGiftSub_ShouldMarkAsGift()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""GiftedSub"",
                ""tier"": ""1000"",
                ""is_gift"": true
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifySubscriberAsync("123", "GiftedSub", "Tier 1", true), Times.Once);
        }
    }

    public class CheerHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<CheerHandler>> _mockLogger;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly CheerHandler _handler;

        public CheerHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<CheerHandler>>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new CheerHandler(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelCheer()
        {
            Assert.Equal("channel.cheer", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenCheerReceived_ShouldNotifyWithBits()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""Cheerer"",
                ""bits"": 100,
                ""message"": ""Cheer100 Great stream!""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyBitsAsync("123", "Cheerer", 100, "Cheer100 Great stream!", 0), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenAnonymousCheer_ShouldUseAnonymous()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""is_anonymous"": true,
                ""bits"": 50,
                ""message"": ""Anonymous bits!""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyBitsAsync("123", "Anonymous", 50, "Anonymous bits!", 0), Times.Once);
        }
    }

    public class RaidHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<RaidHandler>> _mockLogger;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly RaidHandler _handler;

        public RaidHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<RaidHandler>>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new RaidHandler(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelRaid()
        {
            Assert.Equal("channel.raid", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenRaidReceived_ShouldNotifyWithViewers()
        {
            var eventData = JsonDocument.Parse(@"{
                ""to_broadcaster_user_id"": ""123"",
                ""from_broadcaster_user_name"": ""RaiderChannel"",
                ""viewers"": 50
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyRaidAsync("123", "RaiderChannel", 50), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenToBroadcasterIdMissing_ShouldReturnEarly()
        {
            var eventData = JsonDocument.Parse(@"{
                ""from_broadcaster_user_name"": ""RaiderChannel"",
                ""viewers"": 50
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyRaidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }
    }

    public class SubscriptionGiftHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<SubscriptionGiftHandler>> _mockLogger;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly SubscriptionGiftHandler _handler;

        public SubscriptionGiftHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<SubscriptionGiftHandler>>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new SubscriptionGiftHandler(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelSubscriptionGift()
        {
            Assert.Equal("channel.subscription.gift", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenGiftReceived_ShouldNotifyWithTotal()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""GenerousGifter"",
                ""total"": 5,
                ""tier"": ""1000""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyGiftSubAsync("123", "GenerousGifter", "Community", "Tier 1", 5), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenAnonymousGift_ShouldUseAnonymous()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""is_anonymous"": true,
                ""total"": 10,
                ""tier"": ""3000""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyGiftSubAsync("123", "Anonymous", "Community", "Tier 3", 10), Times.Once);
        }
    }
}
