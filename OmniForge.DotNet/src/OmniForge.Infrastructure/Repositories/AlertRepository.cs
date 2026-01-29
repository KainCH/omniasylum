using System;
using System.Collections.Generic;
using System.Text.Json;
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
    public class AlertRepository : IAlertRepository
    {
        private readonly TableClient _alertsClient;
        private readonly TableClient _usersClient;
        private readonly ILogger<AlertRepository> _logger;

        public AlertRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig, ILogger<AlertRepository> logger)
        {
            _alertsClient = tableServiceClient.GetTableClient(tableConfig.Value.AlertsTable);
            _usersClient = tableServiceClient.GetTableClient(tableConfig.Value.UsersTable);
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            await _alertsClient.CreateIfNotExistsAsync();
            await _usersClient.CreateIfNotExistsAsync();
        }

        public async Task<Alert?> GetAlertAsync(string userId, string alertId)
        {
            try
            {
                _logger.LogDebug(
                    "📥 Getting alert {AlertId} for user {UserId}",
                    alertId.Replace("\r", "\\r").Replace("\n", "\\n"),
                    userId.Replace("\r", "\\r").Replace("\n", "\\n"));
                var response = await _alertsClient.GetEntityAsync<AlertTableEntity>(userId, alertId);
                return response.Value.ToAlert();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogDebug(
                    "⚠️ Alert {AlertId} not found for user {UserId}",
                    alertId.Replace("\r", "\\r").Replace("\n", "\\n"),
                    userId.Replace("\r", "\\r").Replace("\n", "\\n"));
                return null;
            }
        }

        public async Task<IEnumerable<Alert>> GetAlertsAsync(string userId)
        {
            _logger.LogDebug("📥 Getting all alerts for user {UserId}", userId.Replace("\r", "\\r").Replace("\n", "\\n"));
            var alerts = new List<Alert>();
            var query = _alertsClient.QueryAsync<AlertTableEntity>(filter: $"PartitionKey eq '{userId}'");

            await foreach (var entity in query)
            {
                alerts.Add(entity.ToAlert());
            }

            _logger.LogDebug(
                "✅ Retrieved {Count} alerts for user {UserId}",
                alerts.Count,
                userId.Replace("\r", "\\r").Replace("\n", "\\n"));
            return alerts;
        }

        public async Task SaveAlertAsync(Alert alert)
        {
            _logger.LogDebug(
                "💾 Saving alert {AlertId} for user {UserId}",
                (alert.Id ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                (alert.UserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            var entity = AlertTableEntity.FromAlert(alert);
            await _alertsClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogDebug("✅ Saved alert {AlertId}", (alert.Id ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
        }

        public async Task DeleteAlertAsync(string userId, string alertId)
        {
            await _alertsClient.DeleteEntityAsync(userId, alertId);
        }

        public async Task<Dictionary<string, string>> GetEventMappingsAsync(string userId)
        {
            try
            {
                var response = await _usersClient.GetEntityAsync<TableEntity>(userId, "event-mappings");
                if (response.Value.TryGetValue("mappings", out var mappingsObj) && mappingsObj is string mappingsJson)
                {
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(mappingsJson) ?? new Dictionary<string, string>();
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // No mappings found
            }

            return new Dictionary<string, string>();
        }

        public async Task SaveEventMappingsAsync(string userId, Dictionary<string, string> mappings)
        {
            var entity = new TableEntity(userId, "event-mappings")
            {
                ["mappings"] = JsonSerializer.Serialize(mappings),
                ["updatedAt"] = DateTimeOffset.UtcNow
            };

            await _usersClient.UpsertEntityAsync(entity, TableUpdateMode.Merge);
        }
    }
}
