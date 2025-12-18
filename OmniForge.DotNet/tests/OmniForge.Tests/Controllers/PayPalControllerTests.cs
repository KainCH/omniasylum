using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Models;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class PayPalControllerTests
    {
        private readonly Mock<IPayPalVerificationService> _mockVerificationService;
        private readonly Mock<IPayPalNotificationService> _mockNotificationService;
        private readonly Mock<IPayPalRepository> _mockPaypalRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IChatterEmailCache> _mockChatterCache;
        private readonly Mock<ILogger<PayPalController>> _mockLogger;
        private readonly PayPalController _controller;

        public PayPalControllerTests()
        {
            _mockVerificationService = new Mock<IPayPalVerificationService>();
            _mockNotificationService = new Mock<IPayPalNotificationService>();
            _mockPaypalRepository = new Mock<IPayPalRepository>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockChatterCache = new Mock<IChatterEmailCache>();
            _mockLogger = new Mock<ILogger<PayPalController>>();

            _controller = new PayPalController(
                _mockVerificationService.Object,
                _mockNotificationService.Object,
                _mockPaypalRepository.Object,
                _mockUserRepository.Object,
                _mockChatterCache.Object,
                _mockLogger.Object);

            // Setup default HTTP context
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        #region IPN Endpoint Tests

        [Fact]
        public async Task ReceiveIpn_ShouldReturnBadRequest_WhenBodyEmpty()
        {
            // Arrange
            SetupRequestBody("");

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveIpn_ShouldReturnOk_WhenDuplicateTransaction()
        {
            // Arrange
            var ipnData = "txn_id=TXN123&payment_status=Completed";
            SetupRequestBody(ipnData);

            var ipnMessage = new PayPalIpnMessage { TxnId = "TXN123" };
            _mockVerificationService.Setup(x => x.ParseIpnMessage(ipnData)).Returns(ipnMessage);
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync("user123", "TXN123")).ReturnsAsync(true);

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("Duplicate", okResult.Value?.ToString());
        }

        [Fact]
        public async Task ReceiveIpn_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            // Arrange
            var ipnData = "txn_id=TXN123&payment_status=Completed";
            SetupRequestBody(ipnData);

            var ipnMessage = new PayPalIpnMessage { TxnId = "TXN123" };
            _mockVerificationService.Setup(x => x.ParseIpnMessage(ipnData)).Returns(ipnMessage);
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync((User?)null);

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveIpn_ShouldReturnBadRequest_WhenPayPalNotEnabled()
        {
            // Arrange
            var ipnData = "txn_id=TXN123&payment_status=Completed";
            SetupRequestBody(ipnData);

            var ipnMessage = new PayPalIpnMessage { TxnId = "TXN123" };
            _mockVerificationService.Setup(x => x.ParseIpnMessage(ipnData)).Returns(ipnMessage);
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(paypalEnabled: false);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveIpn_ShouldReturnBadRequest_WhenPayPalSettingsDisabled()
        {
            // Arrange - Features.PayPalDonations = true but Settings.Enabled = false
            var ipnData = "txn_id=TXN123&payment_status=Completed";
            SetupRequestBody(ipnData);

            var ipnMessage = new PayPalIpnMessage { TxnId = "TXN123" };
            _mockVerificationService.Setup(x => x.ParseIpnMessage(ipnData)).Returns(ipnMessage);
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUserWithSettingsDisabled();
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveIpn_ShouldReturnBadRequest_WhenReceiverEmailNotAllowed()
        {
            // Arrange
            var ipnData = "txn_id=TXN123&payment_status=Completed&receiver_email=wrong@example.com";
            SetupRequestBody(ipnData);

            var ipnMessage = new PayPalIpnMessage
            {
                TxnId = "TXN123",
                ReceiverEmail = "wrong@example.com"
            };
            _mockVerificationService.Setup(x => x.ParseIpnMessage(ipnData)).Returns(ipnMessage);
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(receiverEmail: "correct@example.com");
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveIpn_ShouldReturnOk_WhenValidIpn()
        {
            // Arrange
            var ipnData = "txn_id=TXN123&payment_status=Completed&receiver_email=streamer@example.com";
            SetupRequestBody(ipnData);

            var ipnMessage = new PayPalIpnMessage
            {
                TxnId = "TXN123",
                ReceiverEmail = "streamer@example.com",
                PaymentStatus = "Completed"
            };
            _mockVerificationService.Setup(x => x.ParseIpnMessage(ipnData)).Returns(ipnMessage);
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            _mockVerificationService.Setup(x => x.IpnToDonation(ipnMessage, "user123")).Returns(CreateTestDonation());

            var user = CreateTestUser();
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert
            Assert.IsType<OkResult>(result);
            _mockPaypalRepository.Verify(x => x.SaveDonationAsync(It.IsAny<PayPalDonation>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveIpn_ShouldAllowAnyReceiverEmail_WhenAllowedListEmpty()
        {
            // Arrange
            var ipnData = "txn_id=TXN123&payment_status=Completed&receiver_email=any@example.com";
            SetupRequestBody(ipnData);

            var ipnMessage = new PayPalIpnMessage
            {
                TxnId = "TXN123",
                ReceiverEmail = "any@example.com",
                PaymentStatus = "Completed"
            };
            _mockVerificationService.Setup(x => x.ParseIpnMessage(ipnData)).Returns(ipnMessage);
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            _mockVerificationService.Setup(x => x.IpnToDonation(ipnMessage, "user123")).Returns(CreateTestDonation());

            // Create user with EMPTY allowed emails list (allows all)
            var user = CreateTestUserWithEmptyReceiverEmails();
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert - Should succeed because empty list allows all
            Assert.IsType<OkResult>(result);
            _mockPaypalRepository.Verify(x => x.SaveDonationAsync(It.IsAny<PayPalDonation>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveIpn_ShouldReturnOk_OnException()
        {
            // Arrange - Return OK even on exception to prevent PayPal retries
            SetupRequestBody("invalid");
            _mockVerificationService.Setup(x => x.ParseIpnMessage(It.IsAny<string>())).Throws(new Exception("Parse error"));

            // Act
            var result = await _controller.ReceiveIpn("user123");

            // Assert - Should return OK to prevent PayPal retries
            Assert.IsType<OkResult>(result);
        }

        #endregion

        #region Webhook Endpoint Tests

        [Fact]
        public async Task ReceiveWebhook_ShouldReturnBadRequest_WhenBodyEmpty()
        {
            // Arrange
            SetupRequestBody("");

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldReturn500_WhenInvalidJson()
        {
            // Arrange - Invalid JSON causes deserialization to fail
            SetupRequestBody("not-json");

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert - Controller catches exception and returns 500
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldIgnoreNonCaptureEvents()
        {
            // Arrange
            var webhookEvent = new PayPalWebhookEvent { EventType = "PAYMENT.AUTHORIZATION.CREATED" };
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("not processed", okResult.Value?.ToString());
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldReturnOk_WhenDuplicateTransaction()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync("user123", It.IsAny<string>())).ReturnsAsync(true);

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("Duplicate", okResult.Value?.ToString());
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync((User?)null);

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldReturnBadRequest_WhenPayPalNotEnabled()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(paypalEnabled: false);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldUseWebhookIdFromSettings()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            // User with custom webhook ID
            var user = CreateTestUserWithWebhookId("custom-webhook-id");
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync("custom-webhook-id", It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature));
            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(CreateTestDonation());

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<OkResult>(result);
            _mockVerificationService.Verify(x => x.VerifyWebhookAsync("custom-webhook-id", It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldReturnBadRequest_WhenVerificationFails()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser();
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Failure("Signature mismatch", PayPalVerificationMethod.WebhookSignature));

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldReturnOk_WhenBelowMinimumAmount()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent("0.50"); // Below default minimum of 1.00
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(minimumAmount: 1.00m);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature));
            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(new PayPalDonation { Amount = 0.50m });

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("minimum", okResult.Value?.ToString());
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldProcessValidWebhook()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser();
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature));
            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(CreateTestDonation());

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<OkResult>(result);
            _mockPaypalRepository.Verify(x => x.SaveDonationAsync(It.IsAny<PayPalDonation>()), Times.Once);
            _mockNotificationService.Verify(x => x.SendDonationNotificationsAsync(user, It.IsAny<PayPalDonation>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldMatchTwitchUser_WhenEnabled()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(enableTwitchMatching: true);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature, "donor@example.com", "Test Donor"));

            var donation = CreateTestDonation();
            donation.PayerEmail = "donor@example.com";
            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(donation);

            // Setup Twitch user match
            var cachedUser = new CachedTwitchUser
            {
                UserId = "twitch123",
                DisplayName = "TwitchDonor",
                Email = "donor@example.com"
            };
            _mockChatterCache.Setup(x => x.GetUserByEmail("user123", "donor@example.com")).Returns(cachedUser);

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<OkResult>(result);
            _mockChatterCache.Verify(x => x.GetUserByEmail("user123", "donor@example.com"), Times.Once);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldStorePendingDonation_WhenNoTwitchMatch()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(enableTwitchMatching: true);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature, "donor@example.com", "Test Donor"));

            var donation = CreateTestDonation();
            donation.PayerEmail = "donor@example.com";
            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(donation);

            // Setup NO Twitch user match
            _mockChatterCache.Setup(x => x.GetUserByEmail("user123", "donor@example.com")).Returns((CachedTwitchUser?)null);

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<OkResult>(result);
            _mockChatterCache.Verify(x => x.StorePendingDonation("user123", It.IsAny<PendingPayPalDonation>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldLookupPayerEmail_WhenNotInVerificationResult()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            webhookEvent.Resource!.Payer = null; // No payer info in webhook
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser();
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            // Verification succeeds but no payer email
            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature));

            // Setup transaction details lookup
            var orderDetails = new PayPalOrderDetails
            {
                Id = "ORDER123",
                Payer = new PayPalPayer
                {
                    EmailAddress = "looked-up@example.com",
                    Name = new PayPalName { GivenName = "Lookup", Surname = "User" }
                }
            };
            _mockVerificationService.Setup(x => x.GetTransactionDetailsAsync(It.IsAny<string>())).ReturnsAsync(orderDetails);

            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), "looked-up@example.com", "Lookup User"))
                .Returns(CreateTestDonation());

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<OkResult>(result);
            _mockVerificationService.Verify(x => x.GetTransactionDetailsAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldHandleTwitchMatchException()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(enableTwitchMatching: true);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature, "donor@example.com", "Test Donor"));

            var donation = CreateTestDonation();
            donation.PayerEmail = "donor@example.com";
            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(donation);

            // Setup Twitch cache to throw exception
            _mockChatterCache.Setup(x => x.GetUserByEmail(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new Exception("Cache error"));

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert - Should still succeed and continue processing despite cache error
            Assert.IsType<OkResult>(result);
            _mockPaypalRepository.Verify(x => x.SaveDonationAsync(It.IsAny<PayPalDonation>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldNotMatchTwitch_WhenDisabled()
        {
            // Arrange
            var webhookEvent = CreateValidWebhookEvent();
            SetupRequestBody(JsonSerializer.Serialize(webhookEvent));
            SetupWebhookHeaders();
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var user = CreateTestUser(enableTwitchMatching: false); // Twitch matching disabled
            _mockUserRepository.Setup(x => x.GetUserAsync("user123")).ReturnsAsync(user);

            _mockVerificationService.Setup(x => x.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<PayPalWebhookHeaders>(), It.IsAny<string>()))
                .ReturnsAsync(PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature, "donor@example.com", "Test Donor"));
            _mockVerificationService.Setup(x => x.WebhookToDonation(It.IsAny<PayPalWebhookEvent>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(CreateTestDonation());

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            Assert.IsType<OkResult>(result);
            _mockChatterCache.Verify(x => x.GetUserByEmail(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ReceiveWebhook_ShouldReturnServerError_OnException()
        {
            // Arrange
            SetupRequestBody("{\"event_type\": \"PAYMENT.CAPTURE.COMPLETED\"}");
            _mockPaypalRepository.Setup(x => x.TransactionExistsAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new Exception("DB error"));

            // Act
            var result = await _controller.ReceiveWebhook("user123");

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region Donations Endpoint Tests

        [Fact]
        public async Task GetDonations_ShouldReturnUnauthorized_WhenNoUserId()
        {
            // Arrange - No claims

            // Act
            var result = await _controller.GetDonations();

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetDonations_ShouldReturnDonations_WhenAuthorized()
        {
            // Arrange
            SetupAuthenticatedUser("user123");
            var donations = new List<PayPalDonation>
            {
                CreateTestDonation(),
                CreateTestDonation()
            };
            _mockPaypalRepository.Setup(x => x.GetRecentDonationsAsync("user123", 50)).ReturnsAsync(donations);

            // Act
            var result = await _controller.GetDonations();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedDonations = Assert.IsType<List<PayPalDonation>>(okResult.Value);
            Assert.Equal(2, returnedDonations.Count);
        }

        [Fact]
        public async Task GetDonations_ShouldRespectLimit()
        {
            // Arrange
            SetupAuthenticatedUser("user123");
            _mockPaypalRepository.Setup(x => x.GetRecentDonationsAsync("user123", 10)).ReturnsAsync(new List<PayPalDonation>());

            // Act
            var result = await _controller.GetDonations(10);

            // Assert
            _mockPaypalRepository.Verify(x => x.GetRecentDonationsAsync("user123", 10), Times.Once);
        }

        [Fact]
        public async Task GetDonation_ShouldReturnUnauthorized_WhenNoUserId()
        {
            // Arrange - No claims

            // Act
            var result = await _controller.GetDonation("TXN123");

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetDonation_ShouldReturnNotFound_WhenDonationDoesNotExist()
        {
            // Arrange
            SetupAuthenticatedUser("user123");
            _mockPaypalRepository.Setup(x => x.GetDonationAsync("user123", "TXN123")).ReturnsAsync((PayPalDonation?)null);

            // Act
            var result = await _controller.GetDonation("TXN123");

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetDonation_ShouldReturnDonation_WhenExists()
        {
            // Arrange
            SetupAuthenticatedUser("user123");
            var donation = CreateTestDonation();
            _mockPaypalRepository.Setup(x => x.GetDonationAsync("user123", "TXN123")).ReturnsAsync(donation);

            // Act
            var result = await _controller.GetDonation("TXN123");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(donation, okResult.Value);
        }

        #endregion

        #region Helper Methods

        private void SetupRequestBody(string body)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            _controller.ControllerContext.HttpContext.Request.Body = stream;
        }

        private void SetupWebhookHeaders()
        {
            var headers = _controller.ControllerContext.HttpContext.Request.Headers;
            headers["PAYPAL-AUTH-ALGO"] = "SHA256withRSA";
            headers["PAYPAL-CERT-URL"] = "https://api.paypal.com/v1/notifications/certs/test";
            headers["PAYPAL-TRANSMISSION-ID"] = "test-transmission-id";
            headers["PAYPAL-TRANSMISSION-SIG"] = "test-signature";
            headers["PAYPAL-TRANSMISSION-TIME"] = DateTime.UtcNow.ToString("O");
        }

        private void SetupAuthenticatedUser(string userId)
        {
            var claims = new List<Claim> { new Claim("userId", userId) };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            _controller.ControllerContext.HttpContext.User = principal;
        }

        private static User CreateTestUser(
            bool paypalEnabled = true,
            string receiverEmail = "streamer@example.com",
            decimal minimumAmount = 0.01m,
            bool enableTwitchMatching = true)
        {
            return new User
            {
                TwitchUserId = "user123",
                Username = "teststreamer",
                Features = new FeatureFlags
                {
                    PayPalDonations = paypalEnabled,
                    PayPalSettings = new PayPalSettings
                    {
                        Enabled = paypalEnabled,
                        AllowedReceiverEmails = new List<string> { receiverEmail },
                        MinimumAmount = minimumAmount,
                        ChatNotifications = true,
                        OverlayAlerts = true,
                        EnableTwitchMatching = enableTwitchMatching
                    }
                }
            };
        }

        private static User CreateTestUserWithEmptyReceiverEmails()
        {
            return new User
            {
                TwitchUserId = "user123",
                Username = "teststreamer",
                Features = new FeatureFlags
                {
                    PayPalDonations = true,
                    PayPalSettings = new PayPalSettings
                    {
                        Enabled = true,
                        AllowedReceiverEmails = new List<string>(), // Empty list = allow all
                        MinimumAmount = 0.01m,
                        ChatNotifications = true,
                        OverlayAlerts = true,
                        EnableTwitchMatching = true
                    }
                }
            };
        }

        private static User CreateTestUserWithSettingsDisabled()
        {
            return new User
            {
                TwitchUserId = "user123",
                Username = "teststreamer",
                Features = new FeatureFlags
                {
                    PayPalDonations = true, // Feature enabled
                    PayPalSettings = new PayPalSettings
                    {
                        Enabled = false, // But settings disabled
                        AllowedReceiverEmails = new List<string> { "streamer@example.com" },
                        MinimumAmount = 0.01m,
                        ChatNotifications = true,
                        OverlayAlerts = true,
                        EnableTwitchMatching = true
                    }
                }
            };
        }

        private static User CreateTestUserWithWebhookId(string webhookId)
        {
            return new User
            {
                TwitchUserId = "user123",
                Username = "teststreamer",
                Features = new FeatureFlags
                {
                    PayPalDonations = true,
                    PayPalSettings = new PayPalSettings
                    {
                        Enabled = true,
                        WebhookId = webhookId,
                        AllowedReceiverEmails = new List<string> { "streamer@example.com" },
                        MinimumAmount = 0.01m,
                        ChatNotifications = true,
                        OverlayAlerts = true,
                        EnableTwitchMatching = true
                    }
                }
            };
        }

        private static PayPalDonation CreateTestDonation()
        {
            return new PayPalDonation
            {
                UserId = "user123",
                TransactionId = "TXN123",
                PayerEmail = "donor@example.com",
                PayerName = "Test Donor",
                Amount = 25.00m,
                Currency = "USD",
                PaymentStatus = "Completed",
                ReceiverEmail = "streamer@example.com",
                Message = "Great stream!",
                ReceivedAt = DateTime.UtcNow
            };
        }

        private static PayPalWebhookEvent CreateValidWebhookEvent(string amount = "25.00")
        {
            return new PayPalWebhookEvent
            {
                Id = "WH-123",
                EventType = "PAYMENT.CAPTURE.COMPLETED",
                Resource = new PayPalWebhookResource
                {
                    Id = "CAPTURE123",
                    Amount = new PayPalAmount { Value = amount, CurrencyCode = "USD" },
                    Payer = new PayPalPayer
                    {
                        EmailAddress = "donor@example.com",
                        Name = new PayPalName { GivenName = "Test", Surname = "Donor" }
                    }
                }
            };
        }

        #endregion
    }
}
