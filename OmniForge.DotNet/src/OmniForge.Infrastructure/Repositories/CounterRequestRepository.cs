using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class CounterRequestRepository : ICounterRequestRepository
    {
        private readonly TableClient _tableClient;

        public CounterRequestRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.CounterRequestsTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task CreateAsync(CounterRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("RequestId is required", nameof(request));

            var entity = CounterRequestTableEntity.FromRequest(request);
            await _tableClient.AddEntityAsync(entity);
        }

        public async Task<CounterRequest?> GetAsync(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return null;

            // RequestId is RowKey; status is PartitionKey, so we must scan. This table is expected to be small.
            await foreach (var entity in _tableClient.QueryAsync<CounterRequestTableEntity>(filter: $"RowKey eq '{requestId.Replace("'", "''")}'"))
            {
                return entity.ToRequest();
            }

            return null;
        }

        public async Task<IEnumerable<CounterRequest>> ListAsync(string? status = null)
        {
            var requests = new List<CounterRequest>();
            string? filter = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                filter = $"PartitionKey eq '{status.Replace("'", "''")}'";
            }

            await foreach (var entity in _tableClient.QueryAsync<CounterRequestTableEntity>(filter: filter))
            {
                requests.Add(entity.ToRequest());
            }

            return requests.OrderByDescending(r => r.CreatedAt);
        }

        public async Task UpdateStatusAsync(string requestId, string status, string? adminNotes = null)
        {
            if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("requestId is required", nameof(requestId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("status is required", nameof(status));

            var existingEntity = await FindEntityAsync(requestId);
            if (existingEntity == null)
            {
                throw new InvalidOperationException("Request not found");
            }

            var now = DateTimeOffset.UtcNow;
            existingEntity.AdminNotes = adminNotes ?? existingEntity.AdminNotes;
            existingEntity.UpdatedAt = now;

            if (!string.Equals(existingEntity.PartitionKey, status, StringComparison.OrdinalIgnoreCase))
            {
                var oldPk = existingEntity.PartitionKey;
                var rk = existingEntity.RowKey;

                var newEntity = new CounterRequestTableEntity
                {
                    PartitionKey = status,
                    RowKey = rk,
                    RequestedByUserId = existingEntity.RequestedByUserId,
                    Name = existingEntity.Name,
                    Icon = existingEntity.Icon,
                    Description = existingEntity.Description,
                    AdminNotes = existingEntity.AdminNotes,
                    CreatedAt = existingEntity.CreatedAt,
                    UpdatedAt = existingEntity.UpdatedAt
                };

                await _tableClient.DeleteEntityAsync(oldPk, rk);
                await _tableClient.UpsertEntityAsync(newEntity, TableUpdateMode.Replace);
                return;
            }

            await _tableClient.UpsertEntityAsync(existingEntity, TableUpdateMode.Replace);
        }

        private async Task<CounterRequestTableEntity?> FindEntityAsync(string requestId)
        {
            await foreach (var entity in _tableClient.QueryAsync<CounterRequestTableEntity>(filter: $"RowKey eq '{requestId.Replace("'", "''")}'"))
            {
                return entity;
            }

            return null;
        }
    }
}
