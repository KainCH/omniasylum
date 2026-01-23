using System;
using System.Text.Json;
using System.Reflection;
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
using TwitchLib.Api.Helix.Models.Channels.GetChannelInformation;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using Xunit;
using OmniForge.Tests;

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
        private readonly Mock<ITwitchAuthService> _mockAuthService;
        private readonly Mock<ITwitchApiService> _mockTwitchApiService;
        private readonly Mock<IGameLibraryRepository> _mockGameLibraryRepository;
        private readonly Mock<IGameCountersRepository> _mockGameCountersRepository;
        private readonly Mock<IGameContextRepository> _mockGameContextRepository;
        private readonly Mock<IDiscordInviteBroadcastScheduler> _mockDiscordInviteBroadcastScheduler;
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
            _mockAuthService = new Mock<ITwitchAuthService>();
            _mockTwitchApiService = new Mock<ITwitchApiService>();
            _mockGameLibraryRepository = new Mock<IGameLibraryRepository>();
            _mockGameCountersRepository = new Mock<IGameCountersRepository>();
            _mockGameContextRepository = new Mock<IGameContextRepository>();
            _mockDiscordInviteBroadcastScheduler = new Mock<IDiscordInviteBroadcastScheduler>();

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
                _mockDiscordTracker.Object,
                _mockAuthService.Object,
                _mockDiscordInviteBroadcastScheduler.Object);
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
            _mockServiceProvider.Setup(x => x.GetService(typeof(ITwitchApiService))).Returns(_mockTwitchApiService.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IGameLibraryRepository))).Returns(_mockGameLibraryRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IGameCountersRepository))).Returns(_mockGameCountersRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IGameContextRepository))).Returns(_mockGameContextRepository.Object);

            // By default, allow the stream_start Discord notification to send.
            // Individual tests can override this (e.g., to simulate dedupe suppression).
            _mockCounterRepository
                .Setup(x => x.TryClaimStreamStartDiscordNotificationAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
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
            _mockDiscordInviteBroadcastScheduler.Verify(x => x.StartAsync(It.IsAny<string>()), Times.Never);
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
            _mockDiscordInviteBroadcastScheduler.Verify(x => x.StartAsync("123"), Times.Once);
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
            _mockDiscordInviteBroadcastScheduler.Verify(x => x.StartAsync("123"), Times.Once);
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
        public async Task HandleAsync_WhenSavedCountersExistForDetectedGame_ShouldLoadCounterStateForGame()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(new Counter { TwitchUserId = "123", Deaths = 1 });
            _mockTwitchApiService.Setup(x => x.GetChannelCategoryAsync("123")).ReturnsAsync(new TwitchChannelCategoryDto
            {
                BroadcasterId = "123",
                GameId = "game-abc",
                GameName = "Test Category"
            });

            _mockGameCountersRepository.Setup(x => x.GetAsync("123", "game-abc")).ReturnsAsync(new Counter
            {
                TwitchUserId = "123",
                Deaths = 42,
                Swears = 3,
                CustomCounters = new System.Collections.Generic.Dictionary<string, int> { ["kills"] = 7 }
            });

            await _handler.HandleAsync(eventData);

            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c =>
                c.Deaths == 42 &&
                c.Swears == 3 &&
                CounterTestHelpers.HasCustomCounterValue(c, "kills", 7) &&
                c.LastCategoryName == "Test Category" &&
                c.Bits == 0 &&
                c.StreamStarted != null
            )), Times.Once);

            _mockOverlayNotifier.Verify(x => x.NotifyStreamStartedAsync("123", It.Is<Counter>(c => c.Deaths == 42)), Times.Once);
            _mockGameContextRepository.Verify(x => x.SaveAsync(It.Is<GameContext>(g => g.ActiveGameId == "game-abc" && g.ActiveGameName == "Test Category")), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenStreamOnlineRepeatedWithSameStartedAt_ShouldSendDiscordOnlyOnce()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser"",
                ""started_at"": ""2026-01-01T00:00:00Z""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            _mockCounterRepository
                .SetupSequence(x => x.TryClaimStreamStartDiscordNotificationAsync("123", It.IsAny<string>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            _mockDiscordService
                .Setup(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            await _handler.HandleAsync(eventData);
            await _handler.HandleAsync(eventData);

            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()), Times.Once);
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

            _mockDiscordService
                .Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

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
        public async Task HandleAsync_WhenGetStreamsReturnsStream_ShouldIncludeStreamDataInNotification()
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

            var startedAt = DateTime.UtcNow.AddMinutes(-5);

            var streamsResponse = new GetStreamsResponse();
            var stream = new TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream();
            SetNonPublicProperty(stream, "Title", "My Title");
            SetNonPublicProperty(stream, "GameName", "My Game");
            SetNonPublicProperty(stream, "ThumbnailUrl", "https://thumb/{width}x{height}");
            SetNonPublicProperty(stream, "ViewerCount", 42);
            SetNonPublicProperty(stream, "StartedAt", startedAt);
            SetNonPublicProperty(stream, "UserName", "TestUser");

            SetNonPublicProperty(streamsResponse, nameof(GetStreamsResponse.Streams), new[] { stream });

            _mockHelixWrapper
                .Setup(x => x.GetStreamsAsync("test_client", "token", It.IsAny<System.Collections.Generic.List<string>>()))
                .ReturnsAsync(streamsResponse);

            object? captured = null;
            _mockDiscordService
                .Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .Callback<User, string, object>((_, __, payload) => captured = payload)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            Assert.NotNull(captured);
            var json = JsonSerializer.Serialize(captured);
            Assert.Contains("My Title", json, StringComparison.Ordinal);
            Assert.Contains("My Game", json, StringComparison.Ordinal);
            Assert.Contains("\"viewerCount\":42", json, StringComparison.Ordinal);
            Assert.Contains("640", json, StringComparison.Ordinal);
            Assert.Contains("360", json, StringComparison.Ordinal);

            _mockHelixWrapper.Verify(
                x => x.GetChannelInformationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenNoStreamData_ShouldFallbackToChannelInformation()
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

            var emptyStreamsResponse = new GetStreamsResponse();
            SetNonPublicProperty(emptyStreamsResponse, nameof(GetStreamsResponse.Streams), Array.Empty<TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream>());

            _mockHelixWrapper
                .Setup(x => x.GetStreamsAsync("test_client", "token", It.IsAny<System.Collections.Generic.List<string>>()))
                .ReturnsAsync(emptyStreamsResponse);

            var channelInfo = new ChannelInformation();
            SetNonPublicProperty(channelInfo, nameof(ChannelInformation.Title), "Channel Title");
            SetNonPublicProperty(channelInfo, nameof(ChannelInformation.GameName), "Channel Game");
            SetNonPublicProperty(channelInfo, nameof(ChannelInformation.BroadcasterName), "TestUser");

            var channelInfoResponse = new GetChannelInformationResponse();
            SetNonPublicProperty(channelInfoResponse, nameof(GetChannelInformationResponse.Data), new[] { channelInfo });

            _mockHelixWrapper
                .Setup(x => x.GetChannelInformationAsync("test_client", "token", "123"))
                .ReturnsAsync(channelInfoResponse);

            object? captured = null;
            _mockDiscordService
                .Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .Callback<User, string, object>((_, __, payload) => captured = payload)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert
            Assert.NotNull(captured);
            var json = JsonSerializer.Serialize(captured);
            Assert.Contains("Channel Title", json, StringComparison.Ordinal);
            Assert.Contains("Channel Game", json, StringComparison.Ordinal);
            Assert.Contains("https://profile.jpg", json, StringComparison.Ordinal);

            _mockHelixWrapper.Verify(x => x.GetChannelInformationAsync("test_client", "token", "123"), Times.Once);
        }

        private static void SetNonPublicProperty<T>(object instance, string propertyName, T value)
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                throw new InvalidOperationException($"Property '{propertyName}' not found on type '{instance.GetType().FullName}'.");
            }

            var setter = property.GetSetMethod(true);
            if (setter == null)
            {
                throw new InvalidOperationException($"Property '{propertyName}' on type '{instance.GetType().FullName}' does not have a setter.");
            }

            setter.Invoke(instance, new object?[] { value });
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
        public async Task HandleAsync_WhenCountersNotFound_ShouldCreateNewCountersAndNotifyStreamStarted()
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
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.TwitchUserId == "123" && c.StreamStarted != null)), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyStreamStartedAsync("123", It.IsAny<Counter>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenNoAccessToken_AndNoAppToken_ShouldSkipStreamInfoFetch()
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
            _mockAuthService
                .Setup(x => x.GetAppAccessTokenAsync(It.IsAny<IReadOnlyCollection<string>?>()))
                .ReturnsAsync((string?)null);

            // Act
            await _handler.HandleAsync(eventData);

            // Assert - should not call Helix API
            _mockHelixWrapper.Verify(x => x.GetStreamsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Collections.Generic.List<string>>()), Times.Never);
            // But should still send Discord notification
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream_start", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenChannelCategoryMissing_ShouldSkipCclApply()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser", AccessToken = "token" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            _mockTwitchApiService
                .Setup(x => x.GetChannelCategoryAsync("123"))
                .ReturnsAsync((TwitchChannelCategoryDto?)null);

            _mockDiscordService
                .Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            await _handler.HandleAsync(eventData);

            _mockTwitchApiService.Verify(
                x => x.UpdateChannelInformationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenGameMissingInLibraryAndUserFallbackExists_ShouldUpsertAndApplyFallbackCcls()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User
            {
                TwitchUserId = "123",
                DisplayName = "TestUser",
                AccessToken = "token",
                Features = new FeatureFlags
                {
                    StreamSettings = new StreamSettings
                    {
                        DefaultContentClassificationLabels = new List<string> { "Gambling" }
                    }
                }
            };

            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            _mockTwitchApiService
                .Setup(x => x.GetChannelCategoryAsync("123"))
                .ReturnsAsync(new TwitchChannelCategoryDto { BroadcasterId = "123", GameId = "game1", GameName = "Test Game" });

            _mockGameLibraryRepository
                .Setup(x => x.GetAsync("123", "game1"))
                .ReturnsAsync((GameLibraryItem?)null);

            _mockGameLibraryRepository
                .Setup(x => x.UpsertAsync(It.IsAny<GameLibraryItem>()))
                .Returns(Task.CompletedTask);

            _mockTwitchApiService
                .Setup(x => x.UpdateChannelInformationAsync("123", "game1", It.IsAny<IReadOnlyCollection<string>>()))
                .Returns(Task.CompletedTask);

            _mockDiscordService
                .Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            await _handler.HandleAsync(eventData);

            _mockGameLibraryRepository.Verify(
                x => x.UpsertAsync(It.Is<GameLibraryItem>(g => g.GameId == "game1" && g.GameName == "Test Game")),
                Times.Once);

            _mockTwitchApiService.Verify(
                x => x.UpdateChannelInformationAsync("123", "game1", It.Is<IReadOnlyCollection<string>>(c => c.Contains("Gambling"))),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenAdminCclsConfigured_ShouldApplyAdminCcls()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""broadcaster_user_name"": ""TestUser""
            }").RootElement;

            var user = new User { TwitchUserId = "123", DisplayName = "TestUser", AccessToken = "token" };
            var counters = new Counter { TwitchUserId = "123" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(counters);

            _mockTwitchApiService
                .Setup(x => x.GetChannelCategoryAsync("123"))
                .ReturnsAsync(new TwitchChannelCategoryDto { BroadcasterId = "123", GameId = "game1", GameName = "Test Game" });

            _mockGameLibraryRepository
                .Setup(x => x.GetAsync("123", "game1"))
                .ReturnsAsync(new GameLibraryItem
                {
                    UserId = "global",
                    GameId = "game1",
                    GameName = "Test Game",
                    EnabledContentClassificationLabels = new List<string> { "DrugsIntoxication" }
                });

            _mockTwitchApiService
                .Setup(x => x.UpdateChannelInformationAsync("123", "game1", It.IsAny<IReadOnlyCollection<string>>()))
                .Returns(Task.CompletedTask);

            _mockDiscordService
                .Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            await _handler.HandleAsync(eventData);

            _mockGameLibraryRepository.Verify(x => x.UpsertAsync(It.IsAny<GameLibraryItem>()), Times.Never);
            _mockTwitchApiService.Verify(
                x => x.UpdateChannelInformationAsync("123", "game1", It.Is<IReadOnlyCollection<string>>(c => c.Contains("DrugsIntoxication"))),
                Times.Once);
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
        private readonly Mock<IGameContextRepository> _mockGameContextRepository;
        private readonly Mock<IGameCountersRepository> _mockGameCountersRepository;
        private readonly Mock<IDiscordInviteBroadcastScheduler> _mockDiscordInviteBroadcastScheduler;
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
            _mockGameContextRepository = new Mock<IGameContextRepository>();
            _mockGameCountersRepository = new Mock<IGameCountersRepository>();
            _mockDiscordInviteBroadcastScheduler = new Mock<IDiscordInviteBroadcastScheduler>();

            SetupDependencyInjection();

            _handler = new StreamOfflineHandler(
                _mockScopeFactory.Object,
                _mockLogger.Object,
                _mockDiscordInviteBroadcastScheduler.Object);
        }

        private void SetupDependencyInjection()
        {
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository))).Returns(_mockUserRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository))).Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IGameContextRepository))).Returns(_mockGameContextRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IGameCountersRepository))).Returns(_mockGameCountersRepository.Object);
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
            _mockDiscordInviteBroadcastScheduler.Verify(x => x.StopAsync(It.IsAny<string>()), Times.Never);
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
            _mockDiscordInviteBroadcastScheduler.Verify(x => x.StopAsync("123"), Times.Once);
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
            _mockDiscordInviteBroadcastScheduler.Verify(x => x.StopAsync("123"), Times.Once);
        }
    }

    public class FollowHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<FollowHandler>> _mockLogger;
        private readonly Mock<IAlertEventRouter> _mockAlertEventRouter;
        private readonly FollowHandler _handler;

        public FollowHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<FollowHandler>>();
            _mockAlertEventRouter = new Mock<IAlertEventRouter>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IAlertEventRouter))).Returns(_mockAlertEventRouter.Object);

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
            _mockAlertEventRouter.Verify(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenFollowReceived_ShouldNotifyOverlay()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""NewFollower""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockAlertEventRouter.Verify(x => x.RouteAsync(
                "123",
                "channel.follow",
                "follow",
                It.Is<object>(o => JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("NewFollower"))),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenEnvelopeFollowReceived_ShouldNotifyOverlay()
        {
            var eventData = JsonDocument.Parse(@"{
                ""subscription"": {
                    ""id"": ""f1c2a387-161a-49f9-a165-0f21d7a4e1c4"",
                    ""type"": ""channel.follow"",
                    ""version"": ""2"",
                    ""status"": ""enabled"",
                    ""cost"": 0,
                    ""condition"": {
                        ""broadcaster_user_id"": ""1337"",
                        ""moderator_user_id"": ""1337""
                    },
                    ""transport"": {
                        ""method"": ""webhook"",
                        ""callback"": ""https://example.com/webhooks/callback""
                    },
                    ""created_at"": ""2019-11-16T10:11:12.634234626Z""
                },
                ""event"": {
                    ""user_id"": ""1234"",
                    ""user_login"": ""cool_user"",
                    ""user_name"": ""Cool_User"",
                    ""broadcaster_user_id"": ""1337"",
                    ""broadcaster_user_login"": ""cooler_user"",
                    ""broadcaster_user_name"": ""Cooler_User"",
                    ""followed_at"": ""2020-07-15T18:16:11.17106713Z""
                }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockAlertEventRouter.Verify(x => x.RouteAsync(
                "1337",
                "channel.follow",
                "follow",
                It.Is<object>(o => JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("Cool_User"))),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenUserNameMissing_ShouldUseSomeone()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockAlertEventRouter.Verify(x => x.RouteAsync(
                "123",
                "channel.follow",
                "follow",
                It.Is<object>(o => JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("Someone"))),
                Times.Once);
        }
    }

    public class ChatNotificationEventSubHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<ChatNotificationHandler>> _mockLogger;
        private readonly Mock<IDiscordInviteSender> _mockDiscordInviteSender;
        private readonly Mock<IAlertEventRouter> _mockAlertEventRouter;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly ChatNotificationHandler _handler;

        public ChatNotificationEventSubHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<ChatNotificationHandler>>();
            _mockDiscordInviteSender = new Mock<IDiscordInviteSender>();
            _mockAlertEventRouter = new Mock<IAlertEventRouter>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IAlertEventRouter))).Returns(_mockAlertEventRouter.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new ChatNotificationHandler(_mockScopeFactory.Object, _mockLogger.Object, _mockDiscordInviteSender.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelChatNotification()
        {
            Assert.Equal("channel.chat.notification", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenResubUsesSubPlan_ShouldRouteWithReadableTierAndMonths()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""1971641"",
                ""chatter_user_name"": ""viewer23"",
                ""notice_type"": ""resub"",
                ""message"": { ""text"": """", ""fragments"": [] },
                ""resub"": {
                    ""cumulative_months"": 10,
                    ""sub_plan"": ""1000"",
                    ""is_gift"": false
                }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockAlertEventRouter.Verify(x => x.RouteAsync(
                "1971641",
                "chat_notification_resub",
                "resub",
                It.Is<object>(o =>
                    JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("Tier 1") &&
                    JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("10"))),
                Times.Once);
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
