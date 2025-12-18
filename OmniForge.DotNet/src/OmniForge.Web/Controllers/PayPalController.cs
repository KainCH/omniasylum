using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Models;
using OmniForge.Core.Utilities;

namespace OmniForge.Web.Controllers
{
    /// <summary>
    /// Controller for handling PayPal IPN and Webhook notifications.
    /// </summary>
    [ApiController]
    [Route("api/paypal")]
    public class PayPalController : ControllerBase
    {
        private readonly IPayPalVerificationService _verificationService;
        private readonly IPayPalNotificationService _notificationService;
        private readonly IPayPalRepository _paypalRepository;
        private readonly IUserRepository _userRepository;
        private readonly IChatterEmailCache _chatterCache;
        private readonly ILogger<PayPalController> _logger;

        public PayPalController(
            IPayPalVerificationService verificationService,
            IPayPalNotificationService notificationService,
            IPayPalRepository paypalRepository,
            IUserRepository userRepository,
            IChatterEmailCache chatterCache,
            ILogger<PayPalController> logger)
        {
            _verificationService = verificationService;
            _notificationService = notificationService;
            _paypalRepository = paypalRepository;
            _userRepository = userRepository;
            _chatterCache = chatterCache;
            _logger = logger;
        }

        /// <summary>
        /// Receive PayPal IPN (Instant Payment Notification) - Legacy method.
        /// URL: POST /api/paypal/ipn/{userId}
        /// </summary>
        /// <param name="userId">Broadcaster's Twitch user ID.</param>
        [HttpPost("ipn/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> ReceiveIpn(string userId)
        {
            _logger.LogInformation("📨 Received PayPal IPN for user {UserId}", LogSanitizer.Sanitize(userId));

            try
            {
                // Read raw IPN body
                using var reader = new StreamReader(Request.Body);
                var ipnData = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(ipnData))
                {
                    _logger.LogWarning("❌ Empty IPN data received");
                    return BadRequest("Empty IPN data");
                }

                // Parse the IPN message first to check for duplicates
                var ipnMessage = _verificationService.ParseIpnMessage(ipnData);

                // Check if this transaction already exists (deduplication)
                if (await _paypalRepository.TransactionExistsAsync(userId, ipnMessage.TxnId))
                {
                    _logger.LogInformation("⚠️ Duplicate IPN received for transaction {TxnId}", ipnMessage.TxnId);
                    return Ok("Duplicate - already processed");
                }

                // Validate user exists and has PayPal feature enabled
                var user = await _userRepository.GetUserAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("❌ IPN received for unknown user: {UserId}", LogSanitizer.Sanitize(userId));
                    return NotFound("User not found");
                }

                if (!user.Features.PayPalDonations || !user.Features.PayPalSettings.Enabled)
                {
                    _logger.LogWarning("⚠️ PayPal donations not enabled for user {UserId}", LogSanitizer.Sanitize(userId));
                    return BadRequest("PayPal donations not enabled");
                }

                // Validate receiver email is in allowed list
                if (!IsReceiverEmailAllowed(user.Features.PayPalSettings, ipnMessage.ReceiverEmail))
                {
                    _logger.LogWarning("❌ Receiver email not in allowed list: {Email}",
                        LogSanitizer.SanitizeEmail(ipnMessage.ReceiverEmail));
                    return BadRequest("Invalid receiver email");
                }

                // Create donation record (pending verification)
                var donation = _verificationService.IpnToDonation(ipnMessage, userId);
                donation.VerificationStatus = PayPalVerificationStatus.Pending;
                await _paypalRepository.SaveDonationAsync(donation);

                // Verify IPN with PayPal (async - don't block response)
                _ = Task.Run(async () => await VerifyAndProcessIpnAsync(donation, ipnData, user));

                // PayPal expects 200 OK response quickly
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing PayPal IPN");
                // Still return 200 to prevent PayPal from retrying immediately
                return Ok();
            }
        }

        /// <summary>
        /// Receive PayPal Webhook notification - Modern method.
        /// URL: POST /api/paypal/webhook/{userId}
        /// </summary>
        /// <param name="userId">Broadcaster's Twitch user ID.</param>
        [HttpPost("webhook/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> ReceiveWebhook(string userId)
        {
            _logger.LogInformation("📨 Received PayPal Webhook for user {UserId}", LogSanitizer.Sanitize(userId));

            try
            {
                // Read raw webhook body
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(body))
                {
                    _logger.LogWarning("❌ Empty webhook body received");
                    return BadRequest("Empty webhook body");
                }

                // Extract webhook headers
                var headers = ExtractWebhookHeaders();

                // Parse webhook event
                var webhookEvent = JsonSerializer.Deserialize<PayPalWebhookEvent>(body);
                if (webhookEvent == null)
                {
                    _logger.LogWarning("❌ Failed to parse webhook event");
                    return BadRequest("Invalid webhook format");
                }

                _logger.LogInformation("📝 Webhook event type: {EventType}, Resource ID: {ResourceId}",
                    webhookEvent.EventType,
                    webhookEvent.Resource?.Id ?? "unknown");

                // Only process PAYMENT.CAPTURE.COMPLETED events (successful payments)
                if (webhookEvent.EventType != "PAYMENT.CAPTURE.COMPLETED")
                {
                    _logger.LogInformation("ℹ️ Ignoring non-capture webhook event: {EventType}", webhookEvent.EventType);
                    return Ok("Event type not processed");
                }

                var transactionId = webhookEvent.Resource?.Id ?? webhookEvent.Id;

                // Check for duplicate
                if (await _paypalRepository.TransactionExistsAsync(userId, transactionId))
                {
                    _logger.LogInformation("⚠️ Duplicate webhook received for transaction {TxnId}", transactionId);
                    return Ok("Duplicate - already processed");
                }

                // Validate user
                var user = await _userRepository.GetUserAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("❌ Webhook received for unknown user: {UserId}", LogSanitizer.Sanitize(userId));
                    return NotFound("User not found");
                }

                if (!user.Features.PayPalDonations || !user.Features.PayPalSettings.Enabled)
                {
                    _logger.LogWarning("⚠️ PayPal donations not enabled for user {UserId}", LogSanitizer.Sanitize(userId));
                    return BadRequest("PayPal donations not enabled");
                }

                // Get webhook ID for verification
                var webhookId = !string.IsNullOrEmpty(user.Features.PayPalSettings.WebhookId)
                    ? user.Features.PayPalSettings.WebhookId
                    : string.Empty;

                // Verify webhook signature
                var verificationResult = await _verificationService.VerifyWebhookAsync(webhookId, headers, body);

                if (!verificationResult.IsValid)
                {
                    _logger.LogWarning("❌ Webhook verification failed: {Error}", verificationResult.ErrorMessage);
                    return BadRequest("Webhook verification failed");
                }

                // Get payer details - either from webhook or via API lookup
                var payerEmail = verificationResult.PayerEmail ?? webhookEvent.Resource?.Payer?.EmailAddress;
                var payerName = verificationResult.PayerName ?? webhookEvent.Resource?.Payer?.GetDisplayName();

                // If payer email not in webhook, try API lookup
                if (string.IsNullOrEmpty(payerEmail))
                {
                    _logger.LogInformation("🔍 Payer email not in webhook, attempting API lookup");
                    var orderDetails = await _verificationService.GetTransactionDetailsAsync(transactionId);
                    if (orderDetails?.Payer != null)
                    {
                        payerEmail = orderDetails.Payer.EmailAddress;
                        payerName = orderDetails.Payer.GetDisplayName();
                    }
                }

                // Create and save donation
                var donation = _verificationService.WebhookToDonation(webhookEvent, userId, payerEmail, payerName);

                // Check minimum amount
                if (donation.Amount < user.Features.PayPalSettings.MinimumAmount)
                {
                    _logger.LogInformation("ℹ️ Donation amount {Amount} below minimum {Min}",
                        donation.Amount, user.Features.PayPalSettings.MinimumAmount);
                    return Ok("Below minimum amount");
                }

                // Try to match payer email to Twitch user
                if (user.Features.PayPalSettings.EnableTwitchMatching && !string.IsNullOrEmpty(payerEmail))
                {
                    await TryMatchTwitchUserAsync(donation, userId, payerEmail);
                }

                await _paypalRepository.SaveDonationAsync(donation);

                // Send notifications (chat, overlay, Discord)
                await _notificationService.SendDonationNotificationsAsync(user, donation);

                _logger.LogInformation("✅ PayPal webhook processed: ${Amount} from {Payer}",
                    donation.Amount,
                    donation.MatchedTwitchDisplayName ?? donation.PayerName);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing PayPal webhook");
                return StatusCode(500, "Internal error");
            }
        }

        /// <summary>
        /// Get recent donations for the authenticated user.
        /// </summary>
        [HttpGet("donations")]
        [Authorize]
        public async Task<IActionResult> GetDonations([FromQuery] int limit = 50)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var donations = await _paypalRepository.GetRecentDonationsAsync(userId, limit);
            return Ok(donations);
        }

        /// <summary>
        /// Get a specific donation by transaction ID.
        /// </summary>
        [HttpGet("donations/{transactionId}")]
        [Authorize]
        public async Task<IActionResult> GetDonation(string transactionId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var donation = await _paypalRepository.GetDonationAsync(userId, transactionId);
            if (donation == null)
            {
                return NotFound();
            }

            return Ok(donation);
        }

        #region Private Methods

        private PayPalWebhookHeaders ExtractWebhookHeaders()
        {
            return new PayPalWebhookHeaders
            {
                AuthAlgo = Request.Headers["PAYPAL-AUTH-ALGO"].ToString(),
                CertUrl = Request.Headers["PAYPAL-CERT-URL"].ToString(),
                TransmissionId = Request.Headers["PAYPAL-TRANSMISSION-ID"].ToString(),
                TransmissionSig = Request.Headers["PAYPAL-TRANSMISSION-SIG"].ToString(),
                TransmissionTime = Request.Headers["PAYPAL-TRANSMISSION-TIME"].ToString()
            };
        }

        private async Task VerifyAndProcessIpnAsync(PayPalDonation donation, string ipnData, User user)
        {
            try
            {
                // Verify IPN with PayPal
                var result = await _verificationService.VerifyIpnAsync(ipnData);

                if (result.IsValid)
                {
                    await _paypalRepository.UpdateVerificationStatusAsync(
                        donation.UserId, donation.TransactionId, PayPalVerificationStatus.Verified);

                    // Check minimum amount
                    if (donation.Amount < user.Features.PayPalSettings.MinimumAmount)
                    {
                        _logger.LogInformation("ℹ️ Donation amount {Amount} below minimum {Min}",
                            donation.Amount, user.Features.PayPalSettings.MinimumAmount);
                        return;
                    }

                    // Only process completed payments
                    if (!donation.PaymentStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("ℹ️ Ignoring non-completed payment status: {Status}",
                            donation.PaymentStatus);
                        return;
                    }

                    // Try Twitch user matching
                    if (user.Features.PayPalSettings.EnableTwitchMatching && !string.IsNullOrEmpty(donation.PayerEmail))
                    {
                        await TryMatchTwitchUserAsync(donation, donation.UserId, donation.PayerEmail);
                        await _paypalRepository.SaveDonationAsync(donation);
                    }

                    // Send notifications (chat, overlay, Discord)
                    await _notificationService.SendDonationNotificationsAsync(user, donation);

                    _logger.LogInformation("✅ PayPal IPN verified and processed: ${Amount} from {Payer}",
                        donation.Amount,
                        donation.MatchedTwitchDisplayName ?? donation.PayerName);
                }
                else
                {
                    _logger.LogWarning("❌ IPN verification failed: {Error}", result.ErrorMessage);
                    await _paypalRepository.UpdateVerificationStatusAsync(
                        donation.UserId, donation.TransactionId, PayPalVerificationStatus.Invalid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in background IPN verification");
                await _paypalRepository.UpdateVerificationStatusAsync(
                    donation.UserId, donation.TransactionId, PayPalVerificationStatus.Failed);
            }
        }

        private async Task TryMatchTwitchUserAsync(PayPalDonation donation, string broadcasterId, string payerEmail)
        {
            try
            {
                var cachedUser = _chatterCache.GetUserByEmail(broadcasterId, payerEmail);

                if (cachedUser != null)
                {
                    donation.MatchedTwitchUserId = cachedUser.UserId;
                    donation.MatchedTwitchDisplayName = cachedUser.DisplayName;
                    _logger.LogInformation("✅ Matched PayPal donor to Twitch user: {DisplayName}",
                        cachedUser.DisplayName);
                }
                else
                {
                    _logger.LogInformation("ℹ️ No Twitch user match found for PayPal email");

                    // Store as pending for potential future match
                    _chatterCache.StorePendingDonation(broadcasterId, new PendingPayPalDonation
                    {
                        TransactionId = donation.TransactionId,
                        PayerEmail = payerEmail,
                        PayerName = donation.PayerName,
                        Amount = donation.Amount,
                        ReceivedAt = donation.ReceivedAt
                    });
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error matching Twitch user");
            }
        }

        private static bool IsReceiverEmailAllowed(Core.Entities.PayPalSettings settings, string receiverEmail)
        {
            if (settings.AllowedReceiverEmails.Count == 0)
            {
                // No restriction configured - allow all
                return true;
            }

            return settings.AllowedReceiverEmails.Exists(
                e => e.Equals(receiverEmail, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
