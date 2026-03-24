using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class AlertEventRouterTests
    {
        private readonly Mock<IAlertRepository> _alertRepo = new();
        private readonly Mock<IOverlayNotifier> _overlay = new();
        private readonly AlertEventRouter _router;

        public AlertEventRouterTests()
        {
            _alertRepo.Setup(x => x.GetEventMappingsAsync(It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, string>());
            _router = new AlertEventRouter(_alertRepo.Object, _overlay.Object, new Mock<ILogger<AlertEventRouter>>().Object);
        }

        [Fact]
        public async Task RouteAsync_WhenMappingOverridesType_ShouldNotifyOverlayWithConfiguredType()
        {
            _alertRepo.Setup(x => x.GetEventMappingsAsync("user1"))
                .ReturnsAsync(new Dictionary<string, string> { { "event.key", "configured_type" } });

            await _router.RouteAsync("user1", "event.key", "default_type", new { value = 1 });

            _overlay.Verify(x => x.NotifyCustomAlertAsync("user1", "configured_type", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenMappingSuppressesAlert_ShouldNotNotifyOverlay()
        {
            _alertRepo.Setup(x => x.GetEventMappingsAsync("user1"))
                .ReturnsAsync(new Dictionary<string, string> { { "event.key", "none" } });

            await _router.RouteAsync("user1", "event.key", "default_type", new { value = 1 });

            _overlay.Verify(x => x.NotifyCustomAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task RouteAsync_WhenEmptyUserId_ShouldReturnWithoutCalling()
        {
            await _router.RouteAsync("", "event.key", "follow", new { displayName = "Alice" });

            _overlay.Verify(x => x.NotifyFollowerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RouteAsync_WhenNoMapping_Follow_ShouldCallNotifyFollower()
        {
            var data = JsonSerializer.SerializeToElement(new { displayName = "Alice" });
            await _router.RouteAsync("user1", "channel.follow", "follow", data);
            _overlay.Verify(x => x.NotifyFollowerAsync("user1", "Alice"), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenNoMapping_Subscription_ShouldCallNotifySubscriber()
        {
            var data = JsonSerializer.SerializeToElement(new { displayName = "Bob", tier = "Tier 2", isGift = false });
            await _router.RouteAsync("user1", "channel.subscribe", "subscription", data);
            _overlay.Verify(x => x.NotifySubscriberAsync("user1", "Bob", "Tier 2", false), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenNoMapping_Resub_ShouldCallNotifyResub()
        {
            var data = JsonSerializer.SerializeToElement(new { displayName = "Carol", months = 12, tier = "Tier 1", message = "Resub!" });
            await _router.RouteAsync("user1", "channel.subscription.message", "resub", data);
            _overlay.Verify(x => x.NotifyResubAsync("user1", "Carol", 12, "Tier 1", "Resub!"), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenNoMapping_GiftSub_ShouldCallNotifyGiftSub()
        {
            var data = JsonSerializer.SerializeToElement(new { gifterName = "Dave", recipientName = "Eve", tier = "Tier 1", totalGifts = 5 });
            await _router.RouteAsync("user1", "channel.subscription.gift", "giftsub", data);
            _overlay.Verify(x => x.NotifyGiftSubAsync("user1", "Dave", "Eve", "Tier 1", 5), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenNoMapping_Bits_ShouldCallNotifyBits()
        {
            var data = JsonSerializer.SerializeToElement(new { displayName = "Frank", amount = 100, message = "Nice", totalBits = 500 });
            await _router.RouteAsync("user1", "channel.cheer", "bits", data);
            _overlay.Verify(x => x.NotifyBitsAsync("user1", "Frank", 100, "Nice", 500), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenNoMapping_Raid_ShouldCallNotifyRaid()
        {
            var data = JsonSerializer.SerializeToElement(new { raiderName = "Grace", viewers = 50 });
            await _router.RouteAsync("user1", "channel.raid", "raid", data);
            _overlay.Verify(x => x.NotifyRaidAsync("user1", "Grace", 50), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenNoMapping_UnknownType_ShouldCallCustomAlert()
        {
            var data = new { value = 42 };
            await _router.RouteAsync("user1", "custom.event", "my_custom", data);
            _overlay.Verify(x => x.NotifyCustomAlertAsync("user1", "my_custom", data), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenMappingToCoreType_Follow_ShouldCallNotifyFollower()
        {
            _alertRepo.Setup(x => x.GetEventMappingsAsync("user1"))
                .ReturnsAsync(new Dictionary<string, string> { { "event.key", "follow" } });
            var data = JsonSerializer.SerializeToElement(new { displayName = "Heidi" });

            await _router.RouteAsync("user1", "event.key", "bits", data);

            _overlay.Verify(x => x.NotifyFollowerAsync("user1", "Heidi"), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenRepositoryThrows_ShouldFallBackToDefault()
        {
            _alertRepo.Setup(x => x.GetEventMappingsAsync("user1")).ThrowsAsync(new System.Exception("DB error"));
            var data = JsonSerializer.SerializeToElement(new { displayName = "Ivan" });

            await _router.RouteAsync("user1", "channel.follow", "follow", data);

            _overlay.Verify(x => x.NotifyFollowerAsync("user1", "Ivan"), Times.Once);
        }
    }
}
