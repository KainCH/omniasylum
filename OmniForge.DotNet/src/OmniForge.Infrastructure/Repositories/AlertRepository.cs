using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class AlertRepository : IAlertRepository
    {
        private readonly TableClient _alertsClient;
        private readonly TableClient _usersClient;

        public AlertRepository(TableServiceClient tableServiceClient)
        {
            _alertsClient = tableServiceClient.GetTableClient("alerts");
            _usersClient = tableServiceClient.GetTableClient("users");
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
                var response = await _alertsClient.GetEntityAsync<AlertTableEntity>(userId, alertId);
                return response.Value.ToAlert();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<IEnumerable<Alert>> GetAlertsAsync(string userId)
        {
            var alerts = new List<Alert>();
            var query = _alertsClient.QueryAsync<AlertTableEntity>(filter: $"PartitionKey eq '{userId}'");

            await foreach (var entity in query)
            {
                alerts.Add(entity.ToAlert());
            }

            return alerts;
        }

        public async Task SaveAlertAsync(Alert alert)
        {
            var entity = AlertTableEntity.FromAlert(alert);
            await _alertsClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
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
