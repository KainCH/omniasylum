using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class NotificationServiceTests
    {
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<NotificationService>> _mockLogger;
        private readonly NotificationService _service;

        public NotificationServiceTests()
        {
            _mockDiscordService = new Mock<IDiscordService>();
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockLogger = new Mock<ILogger<NotificationService>>();

            _service = new NotificationService(
                _mockDiscordService.Object,
                _mockTwitchClientManager.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task CheckAndSendMilestoneNotificationsAsync_ShouldDoNothing_WhenSettingsAreNull()
        {
            var user = new User { DiscordSettings = null! };
            await _service.CheckAndSendMilestoneNotificationsAsync(user, "deaths", 9, 10);

            _mockDiscordService.Verify(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CheckAndSendMilestoneNotificationsAsync_ShouldDoNothing_WhenNoMilestoneCrossed()
        {
            var user = CreateUserWithSettings(deathsThresholds: new List<int> { 10, 20 });

            // 11 -> 12 (no milestone crossed)
            await _service.CheckAndSendMilestoneNotificationsAsync(user, "deaths", 11, 12);

            _mockDiscordService.Verify(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task CheckAndSendMilestoneNotificationsAsync_ShouldSendNotifications_WhenMilestoneCrossed()
        {
            var user = CreateUserWithSettings(deathsThresholds: new List<int> { 10 });

            // 9 -> 10 (milestone 10 crossed)
            await _service.CheckAndSendMilestoneNotificationsAsync(user, "deaths", 9, 10);

            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "death_milestone", It.IsAny<object>()), Times.Once);
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(user.TwitchUserId, It.Is<string>(s => s.Contains("MILESTONE REACHED"))), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyMilestoneReachedAsync(user.TwitchUserId, "deaths", 10, 10, 0), Times.Once);
        }

        [Fact]
        public async Task CheckAndSendMilestoneNotificationsAsync_ShouldRespectDisabledNotifications()
        {
            var user = CreateUserWithSettings(deathsThresholds: new List<int> { 10 });
            user.DiscordSettings.EnabledNotifications.DeathMilestone = false;
            user.DiscordSettings.EnableChannelNotifications = false;

            await _service.CheckAndSendMilestoneNotificationsAsync(user, "deaths", 9, 10);

            _mockDiscordService.Verify(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // Overlay might still be sent? The code returns early if both are disabled.
            _mockOverlayNotifier.Verify(x => x.NotifyMilestoneReachedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task CheckAndSendMilestoneNotificationsAsync_ShouldHandleMultipleMilestones()
        {
            // Jumping from 9 to 20 (crossing 10 and 20)
            var user = CreateUserWithSettings(deathsThresholds: new List<int> { 10, 20 });

            await _service.CheckAndSendMilestoneNotificationsAsync(user, "deaths", 9, 20);

            // Should trigger for both 10 and 20
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "death_milestone", It.IsAny<object>()), Times.Exactly(2));
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(user.TwitchUserId, It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CheckAndSendMilestoneNotificationsAsync_ShouldHandleDifferentCounterTypes()
        {
            var user = CreateUserWithSettings(swearsThresholds: new List<int> { 50 });

            await _service.CheckAndSendMilestoneNotificationsAsync(user, "swears", 49, 50);

            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "swear_milestone", It.IsAny<object>()), Times.Once);
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(user.TwitchUserId, It.Is<string>(s => s.Contains("SWEARS") || s.Contains("swears"))), Times.Once);
        }

        private User CreateUserWithSettings(
            List<int>? deathsThresholds = null,
            List<int>? swearsThresholds = null,
            List<int>? screamsThresholds = null)
        {
            return new User
            {
                TwitchUserId = "12345",
                Username = "testuser",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/...",
                DiscordSettings = new DiscordSettings
                {
                    EnableChannelNotifications = true,
                    EnabledNotifications = new DiscordEnabledNotifications
                    {
                        DeathMilestone = true,
                        SwearMilestone = true,
                        ScreamMilestone = true
                    },
                    MilestoneThresholds = new DiscordMilestoneThresholds
                    {
                        Deaths = deathsThresholds ?? new List<int>(),
                        Swears = swearsThresholds ?? new List<int>(),
                        Screams = screamsThresholds ?? new List<int>()
                    }
                }
            };
        }
    }
}
