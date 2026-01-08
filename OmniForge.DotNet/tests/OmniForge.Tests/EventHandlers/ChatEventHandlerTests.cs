using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using OmniForge.Infrastructure.Services.EventHandlers;
using Xunit;

namespace OmniForge.Tests.EventHandlers
{
    public class ChatMessageHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<ILogger<ChatMessageHandler>> _mockLogger;
        private readonly Mock<IDiscordInviteSender> _mockDiscordInviteSender;
        private readonly Mock<IChatCommandProcessor> _mockChatCommandProcessor;
        private readonly Mock<ITwitchApiService> _mockTwitchApiService;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ITwitchBotEligibilityService> _mockBotEligibilityService;
        private readonly ChatMessageHandler _handler;

        public ChatMessageHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockLogger = new Mock<ILogger<ChatMessageHandler>>();
            _mockDiscordInviteSender = new Mock<IDiscordInviteSender>();
            _mockChatCommandProcessor = new Mock<IChatCommandProcessor>();
            _mockTwitchApiService = new Mock<ITwitchApiService>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockBotEligibilityService = new Mock<ITwitchBotEligibilityService>();

            _handler = new ChatMessageHandler(
                _mockScopeFactory.Object,
                _mockLogger.Object,
                _mockDiscordInviteSender.Object,
                _mockChatCommandProcessor.Object,
                _mockTwitchApiService.Object,
                _mockUserRepository.Object,
                _mockBotEligibilityService.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelChatMessage()
        {
            Assert.Equal("channel.chat.message", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenBroadcasterIdMissing_ShouldReturnEarly()
        {
            var eventData = JsonDocument.Parse("{}").RootElement;
            await _handler.HandleAsync(eventData);
            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenMessageContainsDiscordCommand_ShouldSendInvite()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""message"": { ""text"": ""Hey everyone !discord"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync("123"), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenMessageContainsDiscordLink_ShouldSendInvite()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""message"": { ""text"": ""Can I get the discord link please?"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync("123"), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenMessageIsNormal_ShouldNotSendInvite()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""message"": { ""text"": ""Hello everyone!"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenMessageEmpty_ShouldReturnEarly()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""message"": { ""text"": """" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync(It.IsAny<string>()), Times.Never);
        }
    }

    public class SubscriptionMessageHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<SubscriptionMessageHandler>> _mockLogger;
        private readonly Mock<IDiscordInviteSender> _mockDiscordInviteSender;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly SubscriptionMessageHandler _handler;

        public SubscriptionMessageHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<SubscriptionMessageHandler>>();
            _mockDiscordInviteSender = new Mock<IDiscordInviteSender>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new SubscriptionMessageHandler(
                _mockScopeFactory.Object,
                _mockLogger.Object,
                _mockDiscordInviteSender.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelSubscriptionMessage()
        {
            Assert.Equal("channel.subscription.message", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenResubWithMonths_ShouldNotifyOverlay()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""LongTimeSub"",
                ""cumulative_months"": 24,
                ""tier"": ""2000"",
                ""message"": { ""text"": ""2 years strong!"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyResubAsync("123", "LongTimeSub", 24, "Tier 2", "2 years strong!"), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenMessageContainsDiscord_ShouldSendInvite()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""user_name"": ""LongTimeSub"",
                ""cumulative_months"": 12,
                ""tier"": ""1000"",
                ""message"": { ""text"": ""!discord please"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync("123"), Times.Once);
        }
    }

    public class ChatNotificationHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<ChatNotificationHandler>> _mockLogger;
        private readonly Mock<IDiscordInviteSender> _mockDiscordInviteSender;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly ChatNotificationHandler _handler;

        public ChatNotificationHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<ChatNotificationHandler>>();
            _mockDiscordInviteSender = new Mock<IDiscordInviteSender>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(_mockOverlayNotifier.Object);

            _handler = new ChatNotificationHandler(
                _mockScopeFactory.Object,
                _mockLogger.Object,
                _mockDiscordInviteSender.Object);
        }

        [Fact]
        public void SubscriptionType_ShouldBeChannelChatNotification()
        {
            Assert.Equal("channel.chat.notification", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenSubNotice_ShouldNotifySubscriber()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""NewSub"",
                ""notice_type"": ""sub"",
                ""sub"": { ""sub_tier"": ""1000"", ""is_prime"": false }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifySubscriberAsync("123", "NewSub", "Tier 1", false), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenResubNotice_ShouldNotifyResub()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""LongTimeSub"",
                ""notice_type"": ""resub"",
                ""resub"": { ""cumulative_months"": 12, ""sub_tier"": ""2000"" },
                ""message"": { ""text"": ""One year!"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyResubAsync("123", "LongTimeSub", 12, "Tier 2", "One year!"), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenSubGiftNotice_ShouldNotifyGiftSub()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""Gifter"",
                ""notice_type"": ""sub_gift"",
                ""sub_gift"": { ""sub_tier"": ""1000"", ""recipient_user_name"": ""LuckyViewer"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyGiftSubAsync("123", "Gifter", "LuckyViewer", "Tier 1", 1), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenCommunitySubGiftNotice_ShouldNotifyGiftSub()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""GenerousGifter"",
                ""notice_type"": ""community_sub_gift"",
                ""community_sub_gift"": { ""total"": 10, ""sub_tier"": ""3000"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyGiftSubAsync("123", "GenerousGifter", "Community", "Tier 3", 10), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenRaidNotice_ShouldNotifyRaid()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""RaiderChannel"",
                ""notice_type"": ""raid"",
                ""raid"": { ""viewer_count"": 100 }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyRaidAsync("123", "RaiderChannel", 100), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenMessageContainsDiscord_ShouldSendInvite()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""Viewer"",
                ""notice_type"": ""announcement"",
                ""message"": { ""text"": ""Join our !discord"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync("123"), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenNoBroadcasterId_ShouldReturn()
        {
            var eventData = JsonDocument.Parse(@"{
                ""chatter_user_name"": ""Viewer"",
                ""notice_type"": ""sub""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifySubscriberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenOverlayNotifierIsNull_ShouldReturn()
        {
            _mockServiceProvider.Setup(x => x.GetService(typeof(IOverlayNotifier))).Returns(null!);

            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""Viewer"",
                ""notice_type"": ""sub"",
                ""sub"": { ""sub_tier"": ""1000"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifySubscriberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenAnnouncementNotice_ShouldNotCallNotifier()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""Viewer"",
                ""notice_type"": ""announcement"",
                ""message"": { ""text"": ""Regular announcement"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            // Announcement doesn't trigger subscriber notifications
            _mockOverlayNotifier.Verify(x => x.NotifySubscriberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenMessageContainsDiscordLink_ShouldSendInvite()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""Viewer"",
                ""notice_type"": ""announcement"",
                ""message"": { ""text"": ""Can you share the discord link?"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockDiscordInviteSender.Verify(x => x.SendDiscordInviteAsync("123"), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenUnknownNoticeType_ShouldNotThrow()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""Viewer"",
                ""notice_type"": ""unknown_type""
            }").RootElement;

            var exception = await Record.ExceptionAsync(() => _handler.HandleAsync(eventData));

            Assert.Null(exception);
        }

        [Fact]
        public async Task HandleAsync_WhenResubWithoutMessage_ShouldUseEmptyMessage()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""chatter_user_name"": ""Subscriber"",
                ""notice_type"": ""resub"",
                ""resub"": { ""cumulative_months"": 6, ""sub_tier"": ""1000"" }
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockOverlayNotifier.Verify(x => x.NotifyResubAsync("123", "Subscriber", 6, "Tier 1", ""), Times.Once);
        }
    }
}
