using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Services;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services;

public class CompositeOverlayNotifierTests
{
    private readonly Mock<IOverlayNotifier> _mock1;
    private readonly Mock<IOverlayNotifier> _mock2;
    private readonly CompositeOverlayNotifier _composite;

    public CompositeOverlayNotifierTests()
    {
        _mock1 = new Mock<IOverlayNotifier>();
        _mock2 = new Mock<IOverlayNotifier>();
        _composite = new CompositeOverlayNotifier(NullLogger<CompositeOverlayNotifier>.Instance, _mock1.Object, _mock2.Object);
    }

    [Fact]
    public async Task NotifyCounterUpdateAsync_ForwardsToBothNotifiers()
    {
        var counter = new Counter { TwitchUserId = "user1", Deaths = 5 };
        await _composite.NotifyCounterUpdateAsync("user1", counter);

        _mock1.Verify(n => n.NotifyCounterUpdateAsync("user1", counter), Times.Once);
        _mock2.Verify(n => n.NotifyCounterUpdateAsync("user1", counter), Times.Once);
    }

    [Fact]
    public async Task NotifyFollowerAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifyFollowerAsync("user1", "TestFollower");

        _mock1.Verify(n => n.NotifyFollowerAsync("user1", "TestFollower"), Times.Once);
        _mock2.Verify(n => n.NotifyFollowerAsync("user1", "TestFollower"), Times.Once);
    }

    [Fact]
    public async Task NotifySettingsUpdateAsync_ForwardsToBothNotifiers()
    {
        var settings = new OverlaySettings();
        await _composite.NotifySettingsUpdateAsync("user1", settings);

        _mock1.Verify(n => n.NotifySettingsUpdateAsync("user1", settings), Times.Once);
        _mock2.Verify(n => n.NotifySettingsUpdateAsync("user1", settings), Times.Once);
    }

    [Fact]
    public async Task NotifyStreamStartedAsync_ForwardsToBothNotifiers()
    {
        var counter = new Counter { TwitchUserId = "user1" };
        await _composite.NotifyStreamStartedAsync("user1", counter);

        _mock1.Verify(n => n.NotifyStreamStartedAsync("user1", counter), Times.Once);
        _mock2.Verify(n => n.NotifyStreamStartedAsync("user1", counter), Times.Once);
    }

    [Fact]
    public async Task NotifyStreamEndedAsync_ForwardsToBothNotifiers()
    {
        var counter = new Counter { TwitchUserId = "user1" };
        await _composite.NotifyStreamEndedAsync("user1", counter);

        _mock1.Verify(n => n.NotifyStreamEndedAsync("user1", counter), Times.Once);
        _mock2.Verify(n => n.NotifyStreamEndedAsync("user1", counter), Times.Once);
    }

    [Fact]
    public async Task NotifySubscriberAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifySubscriberAsync("user1", "Sub", "1000", false);

        _mock1.Verify(n => n.NotifySubscriberAsync("user1", "Sub", "1000", false), Times.Once);
        _mock2.Verify(n => n.NotifySubscriberAsync("user1", "Sub", "1000", false), Times.Once);
    }

    [Fact]
    public async Task NotifyBitsAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifyBitsAsync("user1", "Donor", 100, "msg", 500);

        _mock1.Verify(n => n.NotifyBitsAsync("user1", "Donor", 100, "msg", 500), Times.Once);
        _mock2.Verify(n => n.NotifyBitsAsync("user1", "Donor", 100, "msg", 500), Times.Once);
    }

    [Fact]
    public async Task NotifyRaidAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifyRaidAsync("user1", "Raider", 50);

        _mock1.Verify(n => n.NotifyRaidAsync("user1", "Raider", 50), Times.Once);
        _mock2.Verify(n => n.NotifyRaidAsync("user1", "Raider", 50), Times.Once);
    }

    [Fact]
    public async Task NotifyCustomAlertAsync_ForwardsToBothNotifiers()
    {
        var data = new { msg = "test" };
        await _composite.NotifyCustomAlertAsync("user1", "custom", data);

        _mock1.Verify(n => n.NotifyCustomAlertAsync("user1", "custom", data), Times.Once);
        _mock2.Verify(n => n.NotifyCustomAlertAsync("user1", "custom", data), Times.Once);
    }

    [Fact]
    public async Task NotifyTemplateChangedAsync_ForwardsToBothNotifiers()
    {
        var template = new Template { Name = "Test" };
        await _composite.NotifyTemplateChangedAsync("user1", "modern", template);

        _mock1.Verify(n => n.NotifyTemplateChangedAsync("user1", "modern", template), Times.Once);
        _mock2.Verify(n => n.NotifyTemplateChangedAsync("user1", "modern", template), Times.Once);
    }

    [Fact]
    public async Task NotifyMilestoneReachedAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifyMilestoneReachedAsync("user1", "deaths", 10, 10, 5);

        _mock1.Verify(n => n.NotifyMilestoneReachedAsync("user1", "deaths", 10, 10, 5), Times.Once);
        _mock2.Verify(n => n.NotifyMilestoneReachedAsync("user1", "deaths", 10, 10, 5), Times.Once);
    }

    [Fact]
    public async Task NotifyStreamStatusUpdateAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifyStreamStatusUpdateAsync("user1", "live");

        _mock1.Verify(n => n.NotifyStreamStatusUpdateAsync("user1", "live"), Times.Once);
        _mock2.Verify(n => n.NotifyStreamStatusUpdateAsync("user1", "live"), Times.Once);
    }

    [Fact]
    public async Task NotifyResubAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifyResubAsync("user1", "ReSub", 6, "2000", "Thanks!");

        _mock1.Verify(n => n.NotifyResubAsync("user1", "ReSub", 6, "2000", "Thanks!"), Times.Once);
        _mock2.Verify(n => n.NotifyResubAsync("user1", "ReSub", 6, "2000", "Thanks!"), Times.Once);
    }

    [Fact]
    public async Task NotifyGiftSubAsync_ForwardsToBothNotifiers()
    {
        await _composite.NotifyGiftSubAsync("user1", "Gifter", "Recipient", "1000", 5);

        _mock1.Verify(n => n.NotifyGiftSubAsync("user1", "Gifter", "Recipient", "1000", 5), Times.Once);
        _mock2.Verify(n => n.NotifyGiftSubAsync("user1", "Gifter", "Recipient", "1000", 5), Times.Once);
    }

    [Fact]
    public async Task NotifyCounterUpdateAsync_FirstNotifierThrows_SecondStillReceivesCall()
    {
        var mock1 = new Mock<IOverlayNotifier>();
        var mock2 = new Mock<IOverlayNotifier>();

        mock1.Setup(n => n.NotifyCounterUpdateAsync(It.IsAny<string>(), It.IsAny<Counter>()))
            .ThrowsAsync(new InvalidOperationException("WebSocket hang"));

        var composite = new CompositeOverlayNotifier(NullLogger<CompositeOverlayNotifier>.Instance, mock1.Object, mock2.Object);
        var counter = new Counter { TwitchUserId = "user1", Deaths = 5 };

        // Should not throw
        await composite.NotifyCounterUpdateAsync("user1", counter);

        // Second notifier still called despite first throwing
        mock2.Verify(n => n.NotifyCounterUpdateAsync("user1", counter), Times.Once);
    }

    [Fact]
    public async Task NotifyStreamStartedAsync_FirstNotifierThrows_SecondStillReceivesCall()
    {
        var mock1 = new Mock<IOverlayNotifier>();
        var mock2 = new Mock<IOverlayNotifier>();

        mock1.Setup(n => n.NotifyStreamStartedAsync(It.IsAny<string>(), It.IsAny<Counter>()))
            .ThrowsAsync(new Exception("Connection reset"));

        var composite = new CompositeOverlayNotifier(NullLogger<CompositeOverlayNotifier>.Instance, mock1.Object, mock2.Object);
        var counter = new Counter { TwitchUserId = "user1" };

        await composite.NotifyStreamStartedAsync("user1", counter);

        mock2.Verify(n => n.NotifyStreamStartedAsync("user1", counter), Times.Once);
    }
}
