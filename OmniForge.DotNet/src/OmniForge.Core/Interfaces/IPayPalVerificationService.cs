using System.Threading.Tasks;
using OmniForge.Core.Entities;
using OmniForge.Core.Models;

namespace OmniForge.Core.Interfaces
{
    /// <summary>
    /// Service for verifying PayPal IPN messages and Webhook events.
    /// Supports both legacy IPN postback verification and modern Webhook signature verification.
    /// </summary>
    public interface IPayPalVerificationService
    {
        /// <summary>
        /// Verify a PayPal IPN message using the postback method.
        /// Sends the IPN data back to PayPal to confirm authenticity.
        /// </summary>
        /// <param name="ipnData">Raw IPN form data received from PayPal.</param>
        /// <returns>Verification result.</returns>
        Task<PayPalVerificationResult> VerifyIpnAsync(string ipnData);

        /// <summary>
        /// Verify a PayPal Webhook event using signature verification.
        /// Uses PayPal's API to verify the webhook signature.
        /// </summary>
        /// <param name="webhookId">The webhook ID configured in PayPal developer dashboard.</param>
        /// <param name="headers">Webhook request headers containing signature info.</param>
        /// <param name="body">Raw webhook JSON body.</param>
        /// <returns>Verification result.</returns>
        Task<PayPalVerificationResult> VerifyWebhookAsync(string webhookId, PayPalWebhookHeaders headers, string body);

        /// <summary>
        /// Look up a PayPal capture/transaction by ID to get payer details.
        /// Used as fallback when webhook doesn't include full payer info.
        /// </summary>
        /// <param name="captureId">The capture/transaction ID.</param>
        /// <returns>Order details including payer email, or null if not found.</returns>
        Task<PayPalOrderDetails?> GetTransactionDetailsAsync(string captureId);

        /// <summary>
        /// Parse raw IPN form data into a structured message.
        /// </summary>
        /// <param name="ipnData">URL-encoded IPN data.</param>
        /// <returns>Parsed IPN message.</returns>
        PayPalIpnMessage ParseIpnMessage(string ipnData);

        /// <summary>
        /// Convert a verified IPN message to a PayPalDonation entity.
        /// </summary>
        /// <param name="ipn">Parsed IPN message.</param>
        /// <param name="userId">Broadcaster's Twitch user ID.</param>
        /// <returns>Donation entity ready for storage.</returns>
        PayPalDonation IpnToDonation(PayPalIpnMessage ipn, string userId);

        /// <summary>
        /// Convert a verified Webhook event to a PayPalDonation entity.
        /// </summary>
        /// <param name="webhookEvent">Webhook event from PayPal.</param>
        /// <param name="userId">Broadcaster's Twitch user ID.</param>
        /// <param name="payerEmail">Payer email (from webhook or API lookup).</param>
        /// <param name="payerName">Payer display name.</param>
        /// <returns>Donation entity ready for storage.</returns>
        PayPalDonation WebhookToDonation(PayPalWebhookEvent webhookEvent, string userId, string? payerEmail, string? payerName);
    }

    /// <summary>
    /// PayPal webhook headers required for signature verification.
    /// </summary>
    public class PayPalWebhookHeaders
    {
        /// <summary>PAYPAL-AUTH-ALGO header.</summary>
        public string AuthAlgo { get; set; } = string.Empty;

        /// <summary>PAYPAL-CERT-URL header.</summary>
        public string CertUrl { get; set; } = string.Empty;

        /// <summary>PAYPAL-TRANSMISSION-ID header.</summary>
        public string TransmissionId { get; set; } = string.Empty;

        /// <summary>PAYPAL-TRANSMISSION-SIG header.</summary>
        public string TransmissionSig { get; set; } = string.Empty;

        /// <summary>PAYPAL-TRANSMISSION-TIME header.</summary>
        public string TransmissionTime { get; set; } = string.Empty;

        /// <summary>
        /// Check if all required headers are present.
        /// </summary>
        public bool IsValid =>
            !string.IsNullOrEmpty(AuthAlgo) &&
            !string.IsNullOrEmpty(CertUrl) &&
            !string.IsNullOrEmpty(TransmissionId) &&
            !string.IsNullOrEmpty(TransmissionSig) &&
            !string.IsNullOrEmpty(TransmissionTime);
    }
}
