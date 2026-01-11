using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Repositories
{
    public class GameContextRepository : IGameContextRepository
    {
        private const string RowKey = "active";
        private readonly TableClient _tableClient;

        public GameContextRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.GameContextTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<GameContext?> GetAsync(string userId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(userId, RowKey);
                var entity = response.Value;
                return new GameContext
                {
                    UserId = userId,
                    ActiveGameId = entity.GetString("activeGameId"),
                    ActiveGameName = entity.GetString("activeGameName"),
                    UpdatedAt = GetDateTimeOffsetSafe(entity, "updatedAt") ?? DateTimeOffset.UtcNow
                };
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveAsync(GameContext context)
        {
            var entity = new TableEntity(context.UserId, RowKey)
            {
                ["activeGameId"] = context.ActiveGameId ?? string.Empty,
                ["activeGameName"] = context.ActiveGameName ?? string.Empty,
                ["updatedAt"] = context.UpdatedAt
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task ClearAsync(string userId)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(userId, RowKey);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // no-op
            }
        }

        private static DateTimeOffset? GetDateTimeOffsetSafe(TableEntity entity, string key)
        {
            if (entity.TryGetValue(key, out var value))
            {
                if (value is DateTimeOffset dto) return dto;
                if (value is DateTime dt) return new DateTimeOffset(dt);
                if (value is string s && DateTimeOffset.TryParse(s, out var result)) return result;
            }
            return null;
        }
    }
}
