using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Services;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace OmniForge.Tests.Services;

public class WebSocketOverlayNotifierTests
{
    private readonly Mock<IWebSocketOverlayManager> _mockWebSocketManager;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IAlertRepository> _mockAlertRepository;
    private readonly Mock<ILogger<WebSocketOverlayNotifier>> _mockLogger;
    private readonly WebSocketOverlayNotifier _notifier;

    public WebSocketOverlayNotifierTests()
    {
        _mockWebSocketManager = new Mock<IWebSocketOverlayManager>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockAlertRepository = new Mock<IAlertRepository>();
        _mockLogger = new Mock<ILogger<WebSocketOverlayNotifier>>();

        _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
        _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IAlertRepository))).Returns(_mockAlertRepository.Object);

        // Default: no templates available, notifier should passthrough.
        _mockAlertRepository.Setup(x => x.GetAlertsAsync(It.IsAny<string>())).ReturnsAsync(new List<Alert>());

        _notifier = new WebSocketOverlayNotifier(
            _mockWebSocketManager.Object,
            _mockScopeFactory.Object,
            _mockLogger.Object);
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
    public async Task NotifyCustomAlertAsync_WhenMatchingAlertExists_ShouldHydrateTemplateAndSubstitutePlaceholders()
    {
        // Arrange
        var userId = "user123";
        var alertType = "subscription";

        _mockAlertRepository.Setup(x => x.GetAlertsAsync(userId)).ReturnsAsync(new List<Alert>
        {
            new Alert
            {
                Id = "user123_alert1",
                UserId = userId,
                Type = alertType,
                Name = "Sub Alert",
                TextPrompt = "Thanks [User] for the [Tier]!",
                IsEnabled = true,
                Effects = "{}"
            }
        });

        var data = new { displayName = "Alice", tier = "Tier 1" };

        // Act
        await _notifier.NotifyCustomAlertAsync(userId, alertType, data);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "customAlert",
                It.Is<object>(o =>
                    JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("subscription") &&
                    JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("Thanks Alice for the Tier 1!")
                )),
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

    [Fact]
    public async Task NotifyPayPalDonationAsync_ShouldCallWebSocketManager()
    {
        // Arrange
        var userId = "user123";
        var donorName = "GenerousDonor";
        var amount = 25.00m;
        var currency = "USD";
        var message = "Thanks for the stream!";
        var matchedTwitchUser = false;

        // Act
        await _notifier.NotifyPayPalDonationAsync(userId, donorName, amount, currency, message, matchedTwitchUser);

        // Assert - Uses "paypal_donation" event type (not "customAlert")
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "paypal_donation", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyPayPalDonationAsync_ShouldIncludeDonationDetails()
    {
        // Arrange
        var userId = "user123";
        var donorName = "BigSpender";
        var amount = 100.50m;
        var currency = "USD";
        var message = "Great content!";
        var matchedTwitchUser = true;

        // Act
        await _notifier.NotifyPayPalDonationAsync(userId, donorName, amount, currency, message, matchedTwitchUser);

        // Assert - Verify donation details in payload
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "paypal_donation",
                It.Is<object>(o =>
                    JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("BigSpender") &&
                    JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("100.5")
                )),
            Times.Once);
    }

    [Fact]
    public async Task NotifyPayPalDonationAsync_ShouldHandleEmptyMessage()
    {
        // Arrange
        var userId = "user123";
        var donorName = "SilentDonor";
        var amount = 5.00m;
        var currency = "USD";
        var message = "";
        var matchedTwitchUser = false;

        // Act
        await _notifier.NotifyPayPalDonationAsync(userId, donorName, amount, currency, message, matchedTwitchUser);

        // Assert
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "paypal_donation", It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyPayPalDonationAsync_ShouldIndicateTwitchMatch()
    {
        // Arrange
        var userId = "user123";
        var donorName = "TwitchUser42";
        var amount = 50.00m;
        var currency = "EUR";
        var message = "From Europe!";
        var matchedTwitchUser = true;

        // Act
        await _notifier.NotifyPayPalDonationAsync(userId, donorName, amount, currency, message, matchedTwitchUser);

        // Assert - matchedTwitchUser should be True in payload
        _mockWebSocketManager.Verify(
            m => m.SendToUserAsync(userId, "paypal_donation",
                It.Is<object>(o =>
                    JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("True", StringComparison.OrdinalIgnoreCase)
                )),
            Times.Once);
    }
}
