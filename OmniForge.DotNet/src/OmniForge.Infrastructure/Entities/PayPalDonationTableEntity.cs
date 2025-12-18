using System;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    /// <summary>
    /// Azure Table Storage entity for PayPal donations.
    /// PartitionKey = UserId (broadcaster), RowKey = TransactionId.
    /// </summary>
    public class PayPalDonationTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // TransactionId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string PayerEmail { get; set; } = string.Empty;
        public string PayerName { get; set; } = string.Empty;
        public double Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public string Message { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string ReceiverEmail { get; set; } = string.Empty;
        public string? MatchedTwitchUserId { get; set; }
        public string? MatchedTwitchDisplayName { get; set; }
        public bool NotificationSent { get; set; }
        public int VerificationStatus { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public PayPalDonation ToDonation()
        {
            return new PayPalDonation
            {
                UserId = PartitionKey,
                TransactionId = RowKey,
                PayerEmail = PayerEmail,
                PayerName = PayerName,
                Amount = (decimal)Amount,
                Currency = Currency,
                Message = Message,
                PaymentStatus = PaymentStatus,
                ReceiverEmail = ReceiverEmail,
                MatchedTwitchUserId = MatchedTwitchUserId,
                MatchedTwitchDisplayName = MatchedTwitchDisplayName,
                NotificationSent = NotificationSent,
                VerificationStatus = (PayPalVerificationStatus)VerificationStatus,
                ReceivedAt = ReceivedAt,
                UpdatedAt = UpdatedAt
            };
        }

        public static PayPalDonationTableEntity FromDonation(PayPalDonation donation)
        {
            return new PayPalDonationTableEntity
            {
                PartitionKey = donation.UserId,
                RowKey = donation.TransactionId,
                PayerEmail = donation.PayerEmail,
                PayerName = donation.PayerName,
                Amount = (double)donation.Amount,
                Currency = donation.Currency,
                Message = donation.Message,
                PaymentStatus = donation.PaymentStatus,
                ReceiverEmail = donation.ReceiverEmail,
                MatchedTwitchUserId = donation.MatchedTwitchUserId,
                MatchedTwitchDisplayName = donation.MatchedTwitchDisplayName,
                NotificationSent = donation.NotificationSent,
                VerificationStatus = (int)donation.VerificationStatus,
                ReceivedAt = donation.ReceivedAt,
                UpdatedAt = donation.UpdatedAt
            };
        }
    }
}
