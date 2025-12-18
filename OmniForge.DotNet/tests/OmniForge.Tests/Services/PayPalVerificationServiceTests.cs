using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Models;
using OmniForge.Infrastructure.Services;
using Xunit;
using PayPalConfig = OmniForge.Core.Configuration.PayPalSettings;

namespace OmniForge.Tests.Services
{
    public class PayPalVerificationServiceTests
    {
        private readonly Mock<HttpMessageHandler> _mockHttpHandler;
        private readonly HttpClient _httpClient;
        private readonly Mock<ILogger<PayPalVerificationService>> _mockLogger;
        private readonly PayPalConfig _settings;
        private readonly PayPalVerificationService _service;

        public PayPalVerificationServiceTests()
        {
            _mockHttpHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_mockHttpHandler.Object);
            _mockLogger = new Mock<ILogger<PayPalVerificationService>>();
            _settings = new PayPalConfig
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                UseSandbox = true
            };

            var options = Options.Create(_settings);
            _service = new PayPalVerificationService(_httpClient, options, _mockLogger.Object);
        }

        #region IPN Verification Tests

        [Fact]
        public async Task VerifyIpnAsync_ShouldReturnSuccess_WhenPayPalReturnsVerified()
        {
            // Arrange
            var ipnData = "txn_id=TEST123&payment_status=Completed&mc_gross=10.00";
            SetupHttpResponse("VERIFIED", HttpStatusCode.OK);

            // Act
            var result = await _service.VerifyIpnAsync(ipnData);

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(PayPalVerificationMethod.IpnPostback, result.Method);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public async Task VerifyIpnAsync_ShouldReturnFailure_WhenPayPalReturnsInvalid()
        {
            // Arrange
            var ipnData = "txn_id=TEST123&payment_status=Completed";
            SetupHttpResponse("INVALID", HttpStatusCode.OK);

            // Act
            var result = await _service.VerifyIpnAsync(ipnData);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(PayPalVerificationMethod.IpnPostback, result.Method);
            Assert.Contains("INVALID", result.ErrorMessage);
        }

        [Fact]
        public async Task VerifyIpnAsync_ShouldReturnFailure_WhenUnexpectedResponse()
        {
            // Arrange
            var ipnData = "txn_id=TEST123";
            SetupHttpResponse("UNEXPECTED_RESPONSE", HttpStatusCode.OK);

            // Act
            var result = await _service.VerifyIpnAsync(ipnData);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Unexpected response", result.ErrorMessage);
        }

        [Fact]
        public async Task VerifyIpnAsync_ShouldReturnFailure_WhenHttpExceptionOccurs()
        {
            // Arrange
            var ipnData = "txn_id=TEST123";
            _mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act
            var result = await _service.VerifyIpnAsync(ipnData);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("error", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region IPN Parsing Tests

        [Fact]
        public void ParseIpnMessage_ShouldParseAllFields()
        {
            // Arrange
            var ipnData = "txn_id=TXN123456&payment_status=Completed&payer_email=donor%40example.com" +
                          "&first_name=John&last_name=Doe&mc_gross=25.50&mc_currency=USD" +
                          "&receiver_email=streamer%40example.com&item_name=Donation&memo=Great%20stream!";

            // Act
            var message = _service.ParseIpnMessage(ipnData);

            // Assert
            Assert.Equal("TXN123456", message.TxnId);
            Assert.Equal("Completed", message.PaymentStatus);
            Assert.Equal("donor@example.com", message.PayerEmail);
            Assert.Equal("John", message.FirstName);
            Assert.Equal("Doe", message.LastName);
            Assert.Equal(25.50m, message.McGross);
            Assert.Equal("USD", message.McCurrency);
            Assert.Equal("streamer@example.com", message.ReceiverEmail);
            Assert.Equal("Donation", message.ItemName);
            Assert.Equal("Great stream!", message.Memo);
        }

        [Fact]
        public void ParseIpnMessage_ShouldHandleMissingFields()
        {
            // Arrange
            var ipnData = "txn_id=TXN123&payment_status=Completed";

            // Act
            var message = _service.ParseIpnMessage(ipnData);

            // Assert
            Assert.Equal("TXN123", message.TxnId);
            Assert.Equal("Completed", message.PaymentStatus);
            Assert.Equal(string.Empty, message.PayerEmail); // Missing fields return empty string
            Assert.Equal(string.Empty, message.FirstName);
            Assert.Equal(0m, message.McGross);
        }

        [Fact]
        public void ParseIpnMessage_ShouldHandleUrlEncodedValues()
        {
            // Arrange
            var ipnData = "payer_email=test%2Bemail%40example.com&memo=Thanks%20%26%20cheers!";

            // Act
            var message = _service.ParseIpnMessage(ipnData);

            // Assert
            Assert.Equal("test+email@example.com", message.PayerEmail);
            Assert.Equal("Thanks & cheers!", message.Memo);
        }

        [Fact]
        public void ParseIpnMessage_ShouldHandleBusinessName()
        {
            // Arrange
            var ipnData = "payer_business_name=Acme%20Corp&first_name=&last_name=";

            // Act
            var message = _service.ParseIpnMessage(ipnData);

            // Assert
            Assert.Equal("Acme Corp", message.PayerBusinessName);
        }

        [Fact]
        public void ParseIpnMessage_ShouldStoreRawData()
        {
            // Arrange
            var ipnData = "txn_id=TEST123&payment_status=Pending";

            // Act
            var message = _service.ParseIpnMessage(ipnData);

            // Assert
            Assert.Equal(ipnData, message.RawIpnData);
        }

        #endregion

        #region IPN to Donation Conversion Tests

        [Fact]
        public void IpnToDonation_ShouldMapAllFields()
        {
            // Arrange
            var ipn = new PayPalIpnMessage
            {
                TxnId = "TXN789",
                PayerEmail = "donor@test.com",
                FirstName = "Jane",
                LastName = "Smith",
                McGross = 50.00m,
                McCurrency = "USD",
                PaymentStatus = "Completed",
                ReceiverEmail = "streamer@test.com",
                Memo = "For the charity stream!"
            };

            // Act
            var donation = _service.IpnToDonation(ipn, "user123");

            // Assert
            Assert.Equal("user123", donation.UserId);
            Assert.Equal("TXN789", donation.TransactionId);
            Assert.Equal("donor@test.com", donation.PayerEmail);
            Assert.Equal("Jane Smith", donation.PayerName);
            Assert.Equal(50.00m, donation.Amount);
            Assert.Equal("USD", donation.Currency);
            Assert.Equal("Completed", donation.PaymentStatus);
            Assert.Equal("streamer@test.com", donation.ReceiverEmail);
            Assert.Contains("charity", donation.Message);
        }

        [Fact]
        public void IpnToDonation_ShouldUseBusinessName_WhenFirstLastNameEmpty()
        {
            // Arrange
            var ipn = new PayPalIpnMessage
            {
                TxnId = "TXN999",
                PayerBusinessName = "Gaming Corp LLC",
                FirstName = "",
                LastName = "",
                McGross = 100.00m,
                McCurrency = "USD"
            };

            // Act
            var donation = _service.IpnToDonation(ipn, "user456");

            // Assert
            Assert.Equal("Gaming Corp LLC", donation.PayerName);
        }

        #endregion

        #region WebhookToDonation Tests

        [Fact]
        public void WebhookToDonation_ShouldMapAllFields()
        {
            // Arrange
            var webhookEvent = new PayPalWebhookEvent
            {
                Id = "WH-123",
                EventType = "PAYMENT.CAPTURE.COMPLETED",
                Resource = new PayPalWebhookResource
                {
                    Id = "CAPTURE456",
                    Amount = new PayPalAmount { Value = "75.50", CurrencyCode = "EUR" },
                    Status = "COMPLETED",
                    NoteToPayee = "Keep up the great content!",
                    Payee = new PayPalPayee { EmailAddress = "streamer@example.com" }
                }
            };

            // Act
            var donation = _service.WebhookToDonation(webhookEvent, "user789", "donor@test.com", "John Doe");

            // Assert
            Assert.Equal("user789", donation.UserId);
            Assert.Equal("CAPTURE456", donation.TransactionId);
            Assert.Equal("donor@test.com", donation.PayerEmail);
            Assert.Equal("John Doe", donation.PayerName);
            Assert.Equal(75.50m, donation.Amount);
            Assert.Equal("EUR", donation.Currency);
            Assert.Equal("COMPLETED", donation.PaymentStatus);
            Assert.Equal("streamer@example.com", donation.ReceiverEmail);
            Assert.Contains("great content", donation.Message);
            Assert.Equal(PayPalVerificationStatus.Verified, donation.VerificationStatus);
        }

        [Fact]
        public void WebhookToDonation_ShouldHandleNullResource()
        {
            // Arrange
            var webhookEvent = new PayPalWebhookEvent
            {
                Id = "WH-EMPTY",
                EventType = "PAYMENT.CAPTURE.COMPLETED",
                Resource = null
            };

            // Act
            var donation = _service.WebhookToDonation(webhookEvent, "user123", null, null);

            // Assert
            Assert.Equal("WH-EMPTY", donation.TransactionId);
            Assert.Equal(0m, donation.Amount);
            Assert.Equal("USD", donation.Currency);
            Assert.Empty(donation.PayerEmail);
            Assert.Equal("Anonymous", donation.PayerName);
        }

        [Fact]
        public void WebhookToDonation_ShouldUseSoftDescriptor_WhenNoteToPayeeEmpty()
        {
            // Arrange
            var webhookEvent = new PayPalWebhookEvent
            {
                Id = "WH-DESC",
                EventType = "PAYMENT.CAPTURE.COMPLETED",
                Resource = new PayPalWebhookResource
                {
                    Id = "CAPTURE789",
                    Amount = new PayPalAmount { Value = "10.00", CurrencyCode = "USD" },
                    SoftDescriptor = "StreamSupport",
                    NoteToPayee = null
                }
            };

            // Act
            var donation = _service.WebhookToDonation(webhookEvent, "user123", "email@test.com", "Tester");

            // Assert
            Assert.Equal("StreamSupport", donation.Message);
        }

        [Fact]
        public void WebhookToDonation_ShouldSanitizeMessage_WithHtmlCharacters()
        {
            // Arrange
            var webhookEvent = new PayPalWebhookEvent
            {
                Id = "WH-HTML",
                EventType = "PAYMENT.CAPTURE.COMPLETED",
                Resource = new PayPalWebhookResource
                {
                    Id = "CAPTURE-HTML",
                    Amount = new PayPalAmount { Value = "5.00", CurrencyCode = "USD" },
                    NoteToPayee = "<script>alert('xss')</script>Great stream!"
                }
            };

            // Act
            var donation = _service.WebhookToDonation(webhookEvent, "user123", "email@test.com", "Hacker");

            // Assert
            Assert.DoesNotContain("<script>", donation.Message);
            Assert.Contains("&lt;script&gt;", donation.Message);
        }

        #endregion

        #region GetTransactionDetails Tests

        [Fact]
        public async Task GetTransactionDetailsAsync_ShouldReturnNull_WhenAccessTokenFails()
        {
            // Arrange
            SetupHttpResponse("error", HttpStatusCode.Unauthorized);

            // Act
            var result = await _service.GetTransactionDetailsAsync("CAPTURE123");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetTransactionDetailsAsync_ShouldReturnNull_WhenCaptureRequestFails()
        {
            // Arrange
            SetupHttpResponseSequence(new[]
            {
                (JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 }), HttpStatusCode.OK),
                ("{}", HttpStatusCode.NotFound)
            });

            // Act
            var result = await _service.GetTransactionDetailsAsync("NONEXISTENT");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetTransactionDetailsAsync_ShouldReturnNull_WhenNoOrderLink()
        {
            // Arrange
            var captureDetails = new
            {
                id = "CAPTURE123",
                status = "COMPLETED",
                links = new[] { new { rel = "self", href = "https://api.paypal.com/v2/payments/captures/CAPTURE123" } }
            };

            SetupHttpResponseSequence(new[]
            {
                (JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 }), HttpStatusCode.OK),
                (JsonSerializer.Serialize(captureDetails), HttpStatusCode.OK)
            });

            // Act
            var result = await _service.GetTransactionDetailsAsync("CAPTURE123");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetTransactionDetailsAsync_ShouldReturnOrderDetails_WhenOrderLinkExists()
        {
            // Arrange
            var captureDetails = new
            {
                id = "CAPTURE123",
                status = "COMPLETED",
                links = new[] { new { rel = "up", href = "https://api.paypal.com/v2/checkout/orders/ORDER456" } }
            };

            var orderDetails = new
            {
                id = "ORDER456",
                status = "COMPLETED",
                payer = new { email_address = "payer@example.com", name = new { given_name = "John", surname = "Doe" } }
            };

            SetupHttpResponseSequence(new[]
            {
                (JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 }), HttpStatusCode.OK),
                (JsonSerializer.Serialize(captureDetails), HttpStatusCode.OK),
                (JsonSerializer.Serialize(orderDetails), HttpStatusCode.OK)
            });

            // Act
            var result = await _service.GetTransactionDetailsAsync("CAPTURE123");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("ORDER456", result.Id);
        }

        [Fact]
        public async Task GetTransactionDetailsAsync_ShouldReturnNull_WhenOrderRequestFails()
        {
            // Arrange
            var captureDetails = new
            {
                id = "CAPTURE123",
                status = "COMPLETED",
                links = new[] { new { rel = "up", href = "https://api.paypal.com/v2/checkout/orders/ORDER456" } }
            };

            SetupHttpResponseSequence(new[]
            {
                (JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 }), HttpStatusCode.OK),
                (JsonSerializer.Serialize(captureDetails), HttpStatusCode.OK),
                ("{}", HttpStatusCode.InternalServerError)
            });

            // Act
            var result = await _service.GetTransactionDetailsAsync("CAPTURE123");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetTransactionDetailsAsync_ShouldHandleException()
        {
            // Arrange
            _mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act
            var result = await _service.GetTransactionDetailsAsync("CAPTURE123");

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region Access Token Caching Tests

        [Fact]
        public async Task VerifyWebhookAsync_ShouldUseCachedToken_WhenStillValid()
        {
            // Arrange
            var headers = CreateValidWebhookHeaders();
            var webhookBody = CreateSampleWebhookEvent();

            // First call gets a new token, then verification
            // Second call should skip token request and just do verification
            var callCount = 0;
            _mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken ct) =>
                {
                    callCount++;

                    if (request.RequestUri!.PathAndQuery.Contains("oauth2/token"))
                    {
                        // Token request
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 }))
                        };
                    }
                    else
                    {
                        // Verification request
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(JsonSerializer.Serialize(new { verification_status = "SUCCESS" }))
                        };
                    }
                });

            // Act - First call gets token + verifies
            await _service.VerifyWebhookAsync("webhook-id", headers, webhookBody);
            var callsAfterFirst = callCount;

            // Second call should use cached token + just verify
            await _service.VerifyWebhookAsync("webhook-id", headers, webhookBody);
            var callsAfterSecond = callCount;

            // Assert
            // First call: 1 token request + 1 verify = 2 calls
            Assert.Equal(2, callsAfterFirst);
            // Second call: 0 token request (cached) + 1 verify = 1 call
            Assert.Equal(3, callsAfterSecond); // Total 3 calls (2 from first + 1 verify from second)
        }

        [Fact]
        public async Task GetAccessToken_ShouldReturnNull_WhenParseResponseFails()
        {
            // Arrange
            var headers = CreateValidWebhookHeaders();

            // Return non-JSON garbage that will fail to parse
            SetupHttpResponse("not-json-response", HttpStatusCode.OK);

            // Act
            var result = await _service.VerifyWebhookAsync("webhook-id", headers, "{}");

            // Assert - Should fail because token parsing failed
            Assert.False(result.IsValid);
        }

        #endregion

        #region Webhook Verification Tests

        [Fact]
        public async Task VerifyWebhookAsync_ShouldReturnFailure_WhenHeadersInvalid()
        {
            // Arrange
            var headers = new PayPalWebhookHeaders
            {
                AuthAlgo = string.Empty,
                CertUrl = string.Empty,
                TransmissionId = string.Empty,
                TransmissionSig = string.Empty,
                TransmissionTime = string.Empty
            };

            // Act
            var result = await _service.VerifyWebhookAsync("webhook-id", headers, "{}");

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("headers", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task VerifyWebhookAsync_ShouldReturnFailure_WhenAccessTokenFails()
        {
            // Arrange
            var headers = CreateValidWebhookHeaders();
            SetupHttpResponse("error", HttpStatusCode.Unauthorized);

            // Act
            var result = await _service.VerifyWebhookAsync("webhook-id", headers, "{}");

            // Assert
            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task VerifyWebhookAsync_ShouldReturnSuccess_WhenVerificationSucceeds()
        {
            // Arrange
            var headers = CreateValidWebhookHeaders();
            var webhookBody = CreateSampleWebhookEvent();

            // Setup token response
            SetupHttpResponseSequence(new[]
            {
                (JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 }), HttpStatusCode.OK),
                (JsonSerializer.Serialize(new { verification_status = "SUCCESS" }), HttpStatusCode.OK)
            });

            // Act
            var result = await _service.VerifyWebhookAsync("webhook-id", headers, webhookBody);

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(PayPalVerificationMethod.WebhookSignature, result.Method);
        }

        [Fact]
        public async Task VerifyWebhookAsync_ShouldReturnFailure_WhenVerificationFails()
        {
            // Arrange
            var headers = CreateValidWebhookHeaders();
            var webhookBody = CreateSampleWebhookEvent();

            SetupHttpResponseSequence(new[]
            {
                (JsonSerializer.Serialize(new { access_token = "test-token", expires_in = 3600 }), HttpStatusCode.OK),
                (JsonSerializer.Serialize(new { verification_status = "FAILURE" }), HttpStatusCode.OK)
            });

            // Act
            var result = await _service.VerifyWebhookAsync("webhook-id", headers, webhookBody);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("verification failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Helper Methods

        private void SetupHttpResponse(string content, HttpStatusCode statusCode)
        {
            _mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(content)
                });
        }

        private void SetupHttpResponseSequence((string Content, HttpStatusCode StatusCode)[] responses)
        {
            var callIndex = 0;
            _mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    var response = responses[Math.Min(callIndex, responses.Length - 1)];
                    callIndex++;
                    return new HttpResponseMessage
                    {
                        StatusCode = response.StatusCode,
                        Content = new StringContent(response.Content)
                    };
                });
        }

        private static PayPalWebhookHeaders CreateValidWebhookHeaders()
        {
            return new PayPalWebhookHeaders
            {
                AuthAlgo = "SHA256withRSA",
                CertUrl = "https://api.paypal.com/v1/notifications/certs/test-cert",
                TransmissionId = "test-transmission-id",
                TransmissionSig = "test-signature",
                TransmissionTime = "2024-01-01T12:00:00Z"
            };
        }

        private static string CreateSampleWebhookEvent()
        {
            return JsonSerializer.Serialize(new
            {
                id = "WH-123",
                event_type = "PAYMENT.SALE.COMPLETED",
                resource = new
                {
                    id = "CAPTURE123",
                    amount = new { value = "10.00", currency_code = "USD" },
                    payer = new
                    {
                        email_address = "donor@test.com",
                        name = new { given_name = "Test", surname = "User" }
                    }
                }
            });
        }

        #endregion
    }
}
