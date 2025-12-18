using System;

namespace OmniForge.Core.Entities
{
    /// <summary>
    /// Represents a PayPal donation received via IPN (Instant Payment Notification).
    /// Used for tracking donations and deduplication of IPN retries.
    /// </summary>
    public class PayPalDonation
    {
        /// <summary>
        /// The broadcaster/streamer's Twitch user ID who received the donation.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// PayPal transaction ID - unique identifier for deduplication.
        /// </summary>
        public string TransactionId { get; set; } = string.Empty;

        /// <summary>
        /// PayPal payer email address (used for Twitch user matching).
        /// </summary>
        public string PayerEmail { get; set; } = string.Empty;

        /// <summary>
        /// Donor's name from PayPal (first_name + last_name or payer_business_name).
        /// </summary>
        public string PayerName { get; set; } = string.Empty;

        /// <summary>
        /// Donation amount (gross amount before fees).
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Currency code (e.g., "USD", "EUR", "GBP").
        /// </summary>
        public string Currency { get; set; } = "USD";

        /// <summary>
        /// Optional message/note from the donor (sanitized).
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// PayPal payment status (e.g., "Completed", "Pending", "Refunded").
        /// </summary>
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary>
        /// Receiver email (the streamer's PayPal email).
        /// Used for validation against allowed receiver list.
        /// </summary>
        public string ReceiverEmail { get; set; } = string.Empty;

        /// <summary>
        /// Matched Twitch user ID if email was found in chatter cache.
        /// Null if no match found.
        /// </summary>
        public string? MatchedTwitchUserId { get; set; }

        /// <summary>
        /// Matched Twitch display name if found.
        /// </summary>
        public string? MatchedTwitchDisplayName { get; set; }

        /// <summary>
        /// Whether notifications (chat/overlay) have been sent for this donation.
        /// </summary>
        public bool NotificationSent { get; set; } = false;

        /// <summary>
        /// IPN verification status.
        /// </summary>
        public PayPalVerificationStatus VerificationStatus { get; set; } = PayPalVerificationStatus.Pending;

        /// <summary>
        /// When the IPN was received.
        /// </summary>
        public DateTimeOffset ReceivedAt { get; set; }

        /// <summary>
        /// When the donation record was last updated.
        /// </summary>
        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>
    /// PayPal IPN verification status.
    /// </summary>
    public enum PayPalVerificationStatus
    {
        /// <summary>Verification not yet attempted.</summary>
        Pending = 0,

        /// <summary>PayPal confirmed the IPN is valid.</summary>
        Verified = 1,

        /// <summary>PayPal reported the IPN as invalid.</summary>
        Invalid = 2,

        /// <summary>Verification request failed (network error, timeout, etc.).</summary>
        Failed = 3
    }
}
