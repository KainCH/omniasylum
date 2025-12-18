using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class PayPalNotificationServiceTests
    {
        private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly Mock<IPayPalRepository> _mockPayPalRepository;
        private readonly Mock<ILogger<PayPalNotificationService>> _mockLogger;
        private readonly PayPalNotificationService _service;

        public PayPalNotificationServiceTests()
        {
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockDiscordService = new Mock<IDiscordService>();
            _mockPayPalRepository = new Mock<IPayPalRepository>();
            _mockLogger = new Mock<ILogger<PayPalNotificationService>>();

            _service = new PayPalNotificationService(
                _mockTwitchClientManager.Object,
                _mockOverlayNotifier.Object,
                _mockDiscordService.Object,
                _mockPayPalRepository.Object,
                _mockLogger.Object);
        }

        #region SendDonationNotificationsAsync Tests

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldReturnFalse_WhenNotificationAlreadySent()
        {
            // Arrange
            var user = CreateTestUser();
            var donation = CreateTestDonation();
            donation.NotificationSent = true;

            // Act
            var result = await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            Assert.False(result);
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockOverlayNotifier.Verify(x => x.NotifyPayPalDonationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldSendChatNotification_WhenEnabled()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatNotifications = true;
            user.Features.PayPalSettings.OverlayAlerts = false;
            var donation = CreateTestDonation();

            // Act
            var result = await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            Assert.True(result);
            _mockTwitchClientManager.Verify(
                x => x.SendMessageAsync(user.TwitchUserId, It.Is<string>(s => s.Contains("John Doe"))),
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldNotSendChatNotification_WhenDisabled()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatNotifications = false;
            user.Features.PayPalSettings.OverlayAlerts = false;
            var donation = CreateTestDonation();

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldSendOverlayAlert_WhenEnabled()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatNotifications = false;
            user.Features.PayPalSettings.OverlayAlerts = true;
            var donation = CreateTestDonation();

            // Act
            var result = await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            Assert.True(result);
            _mockOverlayNotifier.Verify(
                x => x.NotifyPayPalDonationAsync(
                    user.TwitchUserId,
                    "John Doe",
                    25.00m,
                    "USD",
                    "Great stream!",
                    false),
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldNotSendOverlayAlert_WhenDisabled()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatNotifications = false;
            user.Features.PayPalSettings.OverlayAlerts = false;
            var donation = CreateTestDonation();

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockOverlayNotifier.Verify(
                x => x.NotifyPayPalDonationAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldSendDiscordNotification_WhenConfigured()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.DiscordNotifications = true;
            user.DiscordChannelId = "1234567890";
            user.Features.PayPalSettings.ChatNotifications = false;
            user.Features.PayPalSettings.OverlayAlerts = false;
            var donation = CreateTestDonation();

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockDiscordService.Verify(
                x => x.SendNotificationAsync(user, "paypal_donation", It.IsAny<object>()),
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldNotSendDiscordNotification_WhenNotConfigured()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.DiscordNotifications = false;
            user.DiscordChannelId = "";
            user.DiscordWebhookUrl = "";
            var donation = CreateTestDonation();

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockDiscordService.Verify(
                x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()),
                Times.Never);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldMarkNotificationSent()
        {
            // Arrange
            var user = CreateTestUser();
            var donation = CreateTestDonation();

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            Assert.True(donation.NotificationSent);
            _mockPayPalRepository.Verify(
                x => x.MarkNotificationSentAsync(donation.UserId, donation.TransactionId),
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldSendAllNotifications_WhenAllEnabled()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatNotifications = true;
            user.Features.PayPalSettings.OverlayAlerts = true;
            user.Features.DiscordNotifications = true;
            user.DiscordChannelId = "1234567890";
            var donation = CreateTestDonation();

            // Act
            var result = await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            Assert.True(result);
            _mockTwitchClientManager.Verify(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _mockOverlayNotifier.Verify(
                x => x.NotifyPayPalDonationAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "paypal_donation", It.IsAny<object>()), Times.Once);
        }

        #endregion

        #region Twitch Matching Tests

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldUseTwitchDisplayName_WhenMatched()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ShowMatchedTwitchName = true;
            var donation = CreateTestDonation();
            donation.MatchedTwitchUserId = "twitch123";
            donation.MatchedTwitchDisplayName = "TwitchGamer42";

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockTwitchClientManager.Verify(
                x => x.SendMessageAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("TwitchGamer42"))),
                Times.Once);
            _mockOverlayNotifier.Verify(
                x => x.NotifyPayPalDonationAsync(
                    It.IsAny<string>(),
                    "TwitchGamer42",
                    It.IsAny<decimal>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    true), // matchedTwitchUser should be true
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldUsePayPalName_WhenMatchingDisabled()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ShowMatchedTwitchName = false;
            var donation = CreateTestDonation();
            donation.MatchedTwitchUserId = "twitch123";
            donation.MatchedTwitchDisplayName = "TwitchGamer42";

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockTwitchClientManager.Verify(
                x => x.SendMessageAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("John Doe"))),
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldUseAnonymous_WhenNoNameAvailable()
        {
            // Arrange
            var user = CreateTestUser();
            var donation = CreateTestDonation();
            donation.PayerName = "";

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockTwitchClientManager.Verify(
                x => x.SendMessageAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("Anonymous"))),
                Times.Once);
        }

        #endregion

        #region Chat Message Template Tests

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldFormatMessageWithTemplate()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatMessageTemplate = "🎉 {name} donated ${amount} {currency}! Message: {message}";
            var donation = CreateTestDonation();

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockTwitchClientManager.Verify(
                x => x.SendMessageAsync(
                    It.IsAny<string>(),
                    It.Is<string>(s =>
                        s.Contains("John Doe") &&
                        s.Contains("25.00") &&
                        s.Contains("USD") &&
                        s.Contains("Great stream!"))),
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldUseDefaultTemplate_WhenEmpty()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatMessageTemplate = "";
            var donation = CreateTestDonation();

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockTwitchClientManager.Verify(
                x => x.SendMessageAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("Thanks"))),
                Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldHandleEmptyMessage()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatMessageTemplate = "{name} donated! {message}";
            var donation = CreateTestDonation();
            donation.Message = "";

            // Act
            await _service.SendDonationNotificationsAsync(user, donation);

            // Assert
            _mockTwitchClientManager.Verify(
                x => x.SendMessageAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("John Doe donated!"))),
                Times.Once);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldContinue_WhenChatFails()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatNotifications = true;
            user.Features.PayPalSettings.OverlayAlerts = true;
            _mockTwitchClientManager
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Chat connection failed"));
            var donation = CreateTestDonation();

            // Act
            var result = await _service.SendDonationNotificationsAsync(user, donation);

            // Assert - Overlay should still be called even if chat fails
            _mockOverlayNotifier.Verify(
                x => x.NotifyPayPalDonationAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task SendDonationNotificationsAsync_ShouldContinue_WhenOverlayFails()
        {
            // Arrange
            var user = CreateTestUser();
            user.Features.PayPalSettings.ChatNotifications = true;
            user.Features.PayPalSettings.OverlayAlerts = true;
            user.Features.DiscordNotifications = true;
            user.DiscordChannelId = "123456789";
            _mockOverlayNotifier
                .Setup(x => x.NotifyPayPalDonationAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ThrowsAsync(new Exception("Overlay connection failed"));
            var donation = CreateTestDonation();

            // Act
            var result = await _service.SendDonationNotificationsAsync(user, donation);

            // Assert - Discord should still be called even if overlay fails
            _mockDiscordService.Verify(
                x => x.SendNotificationAsync(user, "paypal_donation", It.IsAny<object>()),
                Times.Once);
        }

        #endregion

        #region Helper Methods

        private static User CreateTestUser()
        {
            return new User
            {
                TwitchUserId = "broadcaster123",
                Username = "teststreamer",
                DisplayName = "TestStreamer",
                Features = new FeatureFlags
                {
                    PayPalDonations = true,
                    PayPalSettings = new PayPalSettings
                    {
                        Enabled = true,
                        ChatNotifications = true,
                        OverlayAlerts = true,
                        ChatMessageTemplate = "💸 Thanks {name} for the ${amount} donation!",
                        EnableTwitchMatching = true,
                        ShowMatchedTwitchName = true
                    },
                    DiscordNotifications = false
                },
                DiscordChannelId = "",
                DiscordWebhookUrl = ""
            };
        }

        private static PayPalDonation CreateTestDonation()
        {
            return new PayPalDonation
            {
                UserId = "broadcaster123",
                TransactionId = "TXN123456",
                PayerEmail = "donor@example.com",
                PayerName = "John Doe",
                Amount = 25.00m,
                Currency = "USD",
                Message = "Great stream!",
                PaymentStatus = "Completed",
                NotificationSent = false,
                ReceivedAt = DateTimeOffset.UtcNow
            };
        }

        #endregion
    }
}
