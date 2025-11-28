using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests.Services;

public class WebSocketOverlayNotifierTests
{
    private readonly Mock<IWebSocketOverlayManager> _mockWebSocketManager;
    private readonly WebSocketOverlayNotifier _notifier;

    public WebSocketOverlayNotifierTests()
    {
        _mockWebSocketManager = new Mock<IWebSocketOverlayManager>();
        _notifier = new WebSocketOverlayNotifier(_mockWebSocketManager.Object);
    }

    [Fact]
    public async Task NotifyCounterUpdateAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var counter = new Counter { TwitchUserId = userId, Deaths = 5, Swears = 3 };

        // Act
        await _notifier.NotifyCounterUpdateAsync(userId, counter);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "counterUpdate", counter),
            Times.Once);
    }

    [Fact]
    public async Task NotifyMilestoneReachedAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";

        // Act
        await _notifier.NotifyMilestoneReachedAsync(userId, "deaths", 10, 10, 5);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "milestoneReached", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifySettingsUpdateAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var settings = new OverlaySettings();

        // Act
        await _notifier.NotifySettingsUpdateAsync(userId, settings);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "settingsUpdate", settings),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStreamStatusUpdateAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var status = "online";

        // Act
        await _notifier.NotifyStreamStatusUpdateAsync(userId, status);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "streamStatusUpdate", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStreamStartedAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var counter = new Counter { TwitchUserId = userId, Deaths = 0, Swears = 0 };

        // Act
        await _notifier.NotifyStreamStartedAsync(userId, counter);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "streamStarted", counter),
            Times.Once);
    }

    [Fact]
    public async Task NotifyStreamEndedAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var counter = new Counter { TwitchUserId = userId, Deaths = 10, Swears = 5 };

        // Act
        await _notifier.NotifyStreamEndedAsync(userId, counter);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "streamEnded", counter),
            Times.Once);
    }

    [Fact]
    public async Task NotifyFollowerAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var displayName = "NewFollower";

        // Act
        await _notifier.NotifyFollowerAsync(userId, displayName);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "follow", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifySubscriberAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var displayName = "NewSub";
        var tier = "1000";
        var isGift = false;

        // Act
        await _notifier.NotifySubscriberAsync(userId, displayName, tier, isGift);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "subscription", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyResubAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var displayName = "ReSub";
        var months = 6;
        var tier = "2000";
        var message = "Thanks for the stream!";

        // Act
        await _notifier.NotifyResubAsync(userId, displayName, months, tier, message);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "resub", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyGiftSubAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var gifterName = "Gifter";
        var recipientName = "Recipient";
        var tier = "1000";
        var totalGifts = 5;

        // Act
        await _notifier.NotifyGiftSubAsync(userId, gifterName, recipientName, tier, totalGifts);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "giftsub", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyBitsAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var displayName = "BitsDonor";
        var amount = 100;
        var message = "Here's some bits!";
        var totalBits = 500;

        // Act
        await _notifier.NotifyBitsAsync(userId, displayName, amount, message, totalBits);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "bits", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyRaidAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var raiderName = "Raider";
        var viewers = 50;

        // Act
        await _notifier.NotifyRaidAsync(userId, raiderName, viewers);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "raid", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyCustomAlertAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var alertType = "custom";
        var data = new { message = "Custom alert" };

        // Act
        await _notifier.NotifyCustomAlertAsync(userId, alertType, data);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "customAlert", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyTemplateChangedAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var templateStyle = "modern";
        var template = new Template { Name = "Test Template" };

        // Act
        await _notifier.NotifyTemplateChangedAsync(userId, templateStyle, template);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "templateChanged", It.IsAny<object>()),
            Times.Once);
    }
}
