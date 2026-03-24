using System;
using System.Collections.Generic;
using System.Text.Json;
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
    public class BroadcastProfileRepository : IBroadcastProfileRepository
    {
        private readonly TableClient _tableClient;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public BroadcastProfileRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.BroadcastProfilesTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<BroadcastProfile?> GetAsync(string userId, string profileId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<BroadcastProfileTableEntity>(userId, profileId);
                return ToDomain(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<List<BroadcastProfile>> GetAllAsync(string userId)
        {
            var profiles = new List<BroadcastProfile>();
            await foreach (var entity in _tableClient.QueryAsync<BroadcastProfileTableEntity>(filter: $"PartitionKey eq '{userId}'"))
            {
                profiles.Add(ToDomain(entity));
            }
            return profiles;
        }

        public async Task SaveAsync(BroadcastProfile profile)
        {
            var entity = new BroadcastProfileTableEntity
            {
                PartitionKey = profile.UserId,
                RowKey = profile.ProfileId,
                ProfileJson = JsonSerializer.Serialize(profile, _jsonOptions),
                CreatedAt = profile.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task DeleteAsync(string userId, string profileId)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(userId, profileId);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already deleted
            }
        }

        private static BroadcastProfile ToDomain(BroadcastProfileTableEntity entity)
        {
            try
            {
                var profile = JsonSerializer.Deserialize<BroadcastProfile>(entity.ProfileJson, _jsonOptions) ?? new BroadcastProfile();
                profile.UserId = entity.PartitionKey;
                profile.ProfileId = entity.RowKey;
                return profile;
            }
            catch
            {
                return new BroadcastProfile { UserId = entity.PartitionKey, ProfileId = entity.RowKey };
            }
        }
    }
}
