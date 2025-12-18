using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Models;
using OmniForge.Core.Utilities;
using PayPalConfig = OmniForge.Core.Configuration.PayPalSettings;

namespace OmniForge.Infrastructure.Services
{
    /// <summary>
    /// Service for verifying PayPal IPN messages and Webhook events.
    /// </summary>
    public class PayPalVerificationService : IPayPalVerificationService
    {
        private readonly HttpClient _httpClient;
        private readonly PayPalConfig _settings;
        private readonly ILogger<PayPalVerificationService> _logger;

        private string? _cachedAccessToken;
        private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

        public PayPalVerificationService(
            HttpClient httpClient,
            IOptions<PayPalConfig> settings,
            ILogger<PayPalVerificationService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        #region IPN Verification

        public async Task<PayPalVerificationResult> VerifyIpnAsync(string ipnData)
        {
            try
            {
                _logger.LogInformation("🔄 Verifying PayPal IPN message");

                // Send IPN data back to PayPal with cmd=_notify-validate prepended
                var verifyData = $"cmd=_notify-validate&{ipnData}";

                using var content = new StringContent(verifyData, Encoding.UTF8, "application/x-www-form-urlencoded");
                var response = await _httpClient.PostAsync(_settings.IpnVerifyUrl, content);

                var responseText = await response.Content.ReadAsStringAsync();

                if (responseText == "VERIFIED")
                {
                    _logger.LogInformation("✅ PayPal IPN verified successfully");
                    return PayPalVerificationResult.Success(PayPalVerificationMethod.IpnPostback);
                }
                else if (responseText == "INVALID")
                {
                    _logger.LogWarning("❌ PayPal IPN verification returned INVALID");
                    return PayPalVerificationResult.Failure("IPN verification returned INVALID", PayPalVerificationMethod.IpnPostback);
                }
                else
                {
                    _logger.LogWarning("⚠️ Unexpected IPN verification response: {Response}", responseText);
                    return PayPalVerificationResult.Failure($"Unexpected response: {responseText}", PayPalVerificationMethod.IpnPostback);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error verifying PayPal IPN");
                return PayPalVerificationResult.Failure($"Verification error: {ex.Message}", PayPalVerificationMethod.IpnPostback);
            }
        }

        public PayPalIpnMessage ParseIpnMessage(string ipnData)
        {
            var message = new PayPalIpnMessage { RawIpnData = ipnData };
            var pairs = ipnData.Split('&');

            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=', 2);
                if (keyValue.Length != 2) continue;

                var key = HttpUtility.UrlDecode(keyValue[0]);
                var value = HttpUtility.UrlDecode(keyValue[1]);

                switch (key.ToLowerInvariant())
                {
                    case "txn_id":
                        message.TxnId = value;
                        break;
                    case "payment_status":
                        message.PaymentStatus = value;
                        break;
                    case "payer_email":
                        message.PayerEmail = value;
                        break;
                    case "first_name":
                        message.FirstName = value;
                        break;
                    case "last_name":
                        message.LastName = value;
                        break;
                    case "payer_business_name":
                        message.PayerBusinessName = value;
                        break;
                    case "mc_gross":
                        if (decimal.TryParse(value, out var gross))
                            message.McGross = gross;
                        break;
                    case "mc_currency":
                        message.McCurrency = value;
                        break;
                    case "receiver_email":
                        message.ReceiverEmail = value;
                        break;
                    case "custom":
                        message.Custom = value;
                        break;
                    case "item_name":
                        message.ItemName = value;
                        break;
                    case "memo":
                        message.Memo = value;
                        break;
                    case "txn_type":
                        message.TxnType = value;
                        break;
                }
            }

            _logger.LogInformation("📝 Parsed IPN: TxnId={TxnId}, Status={Status}, Amount={Amount} {Currency}, Payer={Email}",
                message.TxnId,
                message.PaymentStatus,
                message.McGross,
                message.McCurrency,
                LogSanitizer.SanitizeEmail(message.PayerEmail));

            return message;
        }

        public PayPalDonation IpnToDonation(PayPalIpnMessage ipn, string userId)
        {
            return new PayPalDonation
            {
                UserId = userId,
                TransactionId = ipn.TxnId,
                PayerEmail = ipn.PayerEmail,
                PayerName = ipn.GetPayerDisplayName(),
                Amount = ipn.McGross,
                Currency = ipn.McCurrency,
                Message = SanitizeMessage(ipn.GetDonationMessage()),
                PaymentStatus = ipn.PaymentStatus,
                ReceiverEmail = ipn.ReceiverEmail,
                ReceivedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        #endregion

        #region Webhook Verification

        public async Task<PayPalVerificationResult> VerifyWebhookAsync(string webhookId, PayPalWebhookHeaders headers, string body)
        {
            try
            {
                _logger.LogInformation("🔄 Verifying PayPal Webhook signature");

                if (!headers.IsValid)
                {
                    _logger.LogWarning("❌ Missing required webhook headers");
                    return PayPalVerificationResult.Failure("Missing required webhook headers", PayPalVerificationMethod.WebhookSignature);
                }

                var accessToken = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    return PayPalVerificationResult.Failure("Failed to get PayPal access token", PayPalVerificationMethod.WebhookSignature);
                }

                // Parse webhook event to include in verification request
                var webhookEvent = JsonSerializer.Deserialize<PayPalWebhookEvent>(body);
                if (webhookEvent == null)
                {
                    return PayPalVerificationResult.Failure("Failed to parse webhook event", PayPalVerificationMethod.WebhookSignature);
                }

                var verifyRequest = new PayPalWebhookVerifyRequest
                {
                    AuthAlgo = headers.AuthAlgo,
                    CertUrl = headers.CertUrl,
                    TransmissionId = headers.TransmissionId,
                    TransmissionSig = headers.TransmissionSig,
                    TransmissionTime = headers.TransmissionTime,
                    WebhookId = webhookId,
                    WebhookEvent = webhookEvent
                };

                var requestJson = JsonSerializer.Serialize(verifyRequest);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ApiBaseUrl}/v1/notifications/verify-webhook-signature");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("❌ Webhook verification API call failed: {StatusCode} - {Response}",
                        response.StatusCode, responseJson);
                    return PayPalVerificationResult.Failure($"API error: {response.StatusCode}", PayPalVerificationMethod.WebhookSignature);
                }

                var verifyResponse = JsonSerializer.Deserialize<PayPalWebhookVerifyResponse>(responseJson);
                if (verifyResponse == null || !verifyResponse.IsVerified)
                {
                    _logger.LogWarning("❌ Webhook signature verification failed");
                    return PayPalVerificationResult.Failure("Signature verification failed", PayPalVerificationMethod.WebhookSignature);
                }

                _logger.LogInformation("✅ PayPal Webhook verified successfully");

                // Extract payer info from webhook
                var payerEmail = webhookEvent.Resource?.Payer?.EmailAddress;
                var payerName = webhookEvent.Resource?.Payer?.GetDisplayName();

                return PayPalVerificationResult.Success(PayPalVerificationMethod.WebhookSignature, payerEmail, payerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error verifying PayPal Webhook");
                return PayPalVerificationResult.Failure($"Verification error: {ex.Message}", PayPalVerificationMethod.WebhookSignature);
            }
        }

        public async Task<PayPalOrderDetails?> GetTransactionDetailsAsync(string captureId)
        {
            try
            {
                _logger.LogInformation("🔍 Looking up PayPal transaction details for capture: {CaptureId}",
                    LogSanitizer.Sanitize(captureId));

                var accessToken = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("❌ Failed to get access token for transaction lookup");
                    return null;
                }

                // First try to get capture details
                using var captureRequest = new HttpRequestMessage(HttpMethod.Get, $"{_settings.ApiBaseUrl}/v2/payments/captures/{captureId}");
                captureRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var captureResponse = await _httpClient.SendAsync(captureRequest);
                var captureJson = await captureResponse.Content.ReadAsStringAsync();

                if (captureResponse.IsSuccessStatusCode)
                {
                    var capture = JsonSerializer.Deserialize<PayPalCaptureDetails>(captureJson);

                    // Find the order link to get full payer details
                    var orderLink = capture?.Links?.Find(l => l.Rel == "up");
                    if (orderLink != null)
                    {
                        return await GetOrderDetailsAsync(orderLink.Href, accessToken);
                    }

                    _logger.LogInformation("📋 Retrieved capture details but no order link found");
                    return null;
                }

                _logger.LogWarning("⚠️ Failed to get capture details: {StatusCode}", captureResponse.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting PayPal transaction details");
                return null;
            }
        }

        private async Task<PayPalOrderDetails?> GetOrderDetailsAsync(string orderUrl, string accessToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, orderUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var order = JsonSerializer.Deserialize<PayPalOrderDetails>(json);
                    _logger.LogInformation("📋 Retrieved order details: OrderId={OrderId}, Payer={PayerEmail}",
                        order?.Id,
                        LogSanitizer.SanitizeEmail(order?.Payer?.EmailAddress ?? "unknown"));
                    return order;
                }

                _logger.LogWarning("⚠️ Failed to get order details: {StatusCode}", response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting order details");
                return null;
            }
        }

        public PayPalDonation WebhookToDonation(PayPalWebhookEvent webhookEvent, string userId, string? payerEmail, string? payerName)
        {
            var resource = webhookEvent.Resource;
            var amount = resource?.Amount?.GetDecimalValue() ?? 0m;
            var currency = resource?.Amount?.CurrencyCode ?? "USD";
            var transactionId = resource?.Id ?? webhookEvent.Id;

            // Try to get message from various webhook fields
            var message = resource?.NoteToPayee ?? resource?.SoftDescriptor ?? string.Empty;

            return new PayPalDonation
            {
                UserId = userId,
                TransactionId = transactionId,
                PayerEmail = payerEmail ?? string.Empty,
                PayerName = payerName ?? "Anonymous",
                Amount = amount,
                Currency = currency,
                Message = SanitizeMessage(message),
                PaymentStatus = resource?.Status ?? "COMPLETED",
                ReceiverEmail = resource?.Payee?.EmailAddress ?? string.Empty,
                ReceivedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                VerificationStatus = PayPalVerificationStatus.Verified
            };
        }

        #endregion

        #region OAuth Token Management

        private async Task<string?> GetAccessTokenAsync()
        {
            // Return cached token if still valid
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _cachedAccessToken;
            }

            try
            {
                _logger.LogInformation("🔑 Requesting new PayPal access token");

                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ApiBaseUrl}/v1/oauth2/token");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ Failed to get PayPal access token: {StatusCode} - {Response}",
                        response.StatusCode, json);
                    return null;
                }

                var tokenResponse = JsonSerializer.Deserialize<PayPalTokenResponse>(json);
                if (tokenResponse == null)
                {
                    _logger.LogError("❌ Failed to parse PayPal token response");
                    return null;
                }

                _cachedAccessToken = tokenResponse.AccessToken;
                // Expire 5 minutes early to account for clock skew
                _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 300);

                _logger.LogInformation("✅ PayPal access token obtained, expires in {ExpiresIn}s", tokenResponse.ExpiresIn);
                return _cachedAccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting PayPal access token");
                return null;
            }
        }

        #endregion

        #region Helpers

        private static string SanitizeMessage(string? message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            // Remove potentially harmful content from user messages
            // Basic sanitization - expand as needed
            return message
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Trim();
        }

        #endregion
    }
}
