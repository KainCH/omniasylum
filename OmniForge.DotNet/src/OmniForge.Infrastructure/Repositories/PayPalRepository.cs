using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for PayPal donation records using Azure Table Storage.
    /// </summary>
    public class PayPalRepository : IPayPalRepository
    {
        private readonly TableClient _donationsClient;
        private readonly ILogger<PayPalRepository> _logger;
        private const string TableName = "paypaldonations";

        public PayPalRepository(TableServiceClient tableServiceClient, ILogger<PayPalRepository> logger)
        {
            _donationsClient = tableServiceClient.GetTableClient(TableName);
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            await _donationsClient.CreateIfNotExistsAsync();
            _logger.LogInformation("✅ PayPal donations table initialized");
        }

        public async Task<PayPalDonation?> GetDonationAsync(string userId, string transactionId)
        {
            try
            {
                _logger.LogDebug("📥 Getting donation {TransactionId} for user {UserId}", transactionId, userId);
                var response = await _donationsClient.GetEntityAsync<PayPalDonationTableEntity>(userId, transactionId);
                return response.Value.ToDonation();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogDebug("⚠️ Donation {TransactionId} not found for user {UserId}", transactionId, userId);
                return null;
            }
        }

        public async Task<bool> TransactionExistsAsync(string userId, string transactionId)
        {
            try
            {
                await _donationsClient.GetEntityAsync<PayPalDonationTableEntity>(userId, transactionId);
                _logger.LogDebug("🔍 Transaction {TransactionId} exists for user {UserId}", transactionId, userId);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
        }

        public async Task SaveDonationAsync(PayPalDonation donation)
        {
            _logger.LogDebug("💾 Saving donation {TransactionId} for user {UserId}", donation.TransactionId, donation.UserId);
            donation.UpdatedAt = DateTimeOffset.UtcNow;
            var entity = PayPalDonationTableEntity.FromDonation(donation);
            await _donationsClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogDebug("✅ Saved donation {TransactionId}", donation.TransactionId);
        }

        public async Task<IEnumerable<PayPalDonation>> GetRecentDonationsAsync(string userId, int limit = 50)
        {
            _logger.LogDebug("📥 Getting recent donations for user {UserId} (limit: {Limit})", userId, limit);
            var donations = new List<PayPalDonation>();
            var query = _donationsClient.QueryAsync<PayPalDonationTableEntity>(
                filter: $"PartitionKey eq '{userId}'",
                maxPerPage: limit);

            await foreach (var entity in query)
            {
                donations.Add(entity.ToDonation());
                if (donations.Count >= limit) break;
            }

            // Sort by ReceivedAt descending (most recent first)
            var sorted = donations.OrderByDescending(d => d.ReceivedAt).ToList();
            _logger.LogDebug("✅ Retrieved {Count} donations for user {UserId}", sorted.Count, userId);
            return sorted;
        }

        public async Task<IEnumerable<PayPalDonation>> GetPendingNotificationsAsync(string userId)
        {
            _logger.LogDebug("📥 Getting pending notification donations for user {UserId}", userId);
            var donations = new List<PayPalDonation>();
            var query = _donationsClient.QueryAsync<PayPalDonationTableEntity>(
                filter: $"PartitionKey eq '{userId}' and NotificationSent eq false and VerificationStatus eq {(int)PayPalVerificationStatus.Verified}");

            await foreach (var entity in query)
            {
                donations.Add(entity.ToDonation());
            }

            _logger.LogDebug("✅ Found {Count} pending notification donations for user {UserId}", donations.Count, userId);
            return donations;
        }

        public async Task MarkNotificationSentAsync(string userId, string transactionId)
        {
            var donation = await GetDonationAsync(userId, transactionId);
            if (donation != null)
            {
                donation.NotificationSent = true;
                await SaveDonationAsync(donation);
                _logger.LogDebug("✅ Marked notification sent for donation {TransactionId}", transactionId);
            }
        }

        public async Task UpdateVerificationStatusAsync(string userId, string transactionId, PayPalVerificationStatus status)
        {
            var donation = await GetDonationAsync(userId, transactionId);
            if (donation != null)
            {
                donation.VerificationStatus = status;
                await SaveDonationAsync(donation);
                _logger.LogDebug("✅ Updated verification status to {Status} for donation {TransactionId}", status, transactionId);
            }
        }

        public async Task<int> DeleteOldDonationsAsync(string userId, int olderThanDays = 90)
        {
            _logger.LogDebug("🗑️ Deleting donations older than {Days} days for user {UserId}", olderThanDays, userId);
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-olderThanDays);
            var deleted = 0;

            var query = _donationsClient.QueryAsync<PayPalDonationTableEntity>(
                filter: $"PartitionKey eq '{userId}'");

            await foreach (var entity in query)
            {
                if (entity.ReceivedAt < cutoffDate)
                {
                    await _donationsClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
                    deleted++;
                }
            }

            _logger.LogInformation("🗑️ Deleted {Count} old donations for user {UserId}", deleted, userId);
            return deleted;
        }
    }
}
