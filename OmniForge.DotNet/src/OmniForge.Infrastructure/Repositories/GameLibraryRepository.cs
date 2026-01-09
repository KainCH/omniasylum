using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Repositories
{
    public class GameLibraryRepository : IGameLibraryRepository
    {
        private const string GlobalPartitionKey = "global";
        private readonly TableClient _tableClient;

        public GameLibraryRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.GamesLibraryTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task UpsertAsync(GameLibraryItem item)
        {
            if (string.IsNullOrWhiteSpace(item.GameId))
            {
                return;
            }

            var entity = new TableEntity(GlobalPartitionKey, item.GameId)
            {
                ["gameName"] = item.GameName ?? string.Empty,
                ["boxArtUrl"] = item.BoxArtUrl ?? string.Empty,
                ["createdAt"] = item.CreatedAt,
                ["lastSeenAt"] = item.LastSeenAt
            };

            // Only persist CCL config when explicitly configured.
            // Null means "not configured" and should not modify existing config.
            if (item.EnabledContentClassificationLabels != null)
            {
                entity["enabledCcls"] = JsonSerializer.Serialize(item.EnabledContentClassificationLabels);
            }

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Merge);
        }

        public async Task<GameLibraryItem?> GetAsync(string userId, string gameId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(GlobalPartitionKey, gameId);
                return Map(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<IReadOnlyList<GameLibraryItem>> ListAsync(string userId, int take = 200)
        {
            var results = new List<GameLibraryItem>();
            var query = _tableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{GlobalPartitionKey}'");

            await foreach (var entity in query)
            {
                results.Add(Map(entity));
                if (results.Count >= take) break;
            }

            return results
                .OrderByDescending(g => g.LastSeenAt)
                .ToList();
        }

        private static GameLibraryItem Map(TableEntity entity)
        {
            List<string>? enabledCcls = null;
            if (entity.TryGetValue("enabledCcls", out var raw) && raw != null)
            {
                try
                {
                    var json = entity.GetString("enabledCcls");
                    enabledCcls = JsonSerializer.Deserialize<List<string>>(json ?? "[]", new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<string>();
                }
                catch
                {
                    enabledCcls = new List<string>();
                }
            }

            return new GameLibraryItem
            {
                UserId = entity.PartitionKey,
                GameId = entity.RowKey,
                GameName = entity.GetString("gameName") ?? string.Empty,
                BoxArtUrl = entity.GetString("boxArtUrl") ?? string.Empty,
                CreatedAt = GetDateTimeOffsetSafe(entity, "createdAt") ?? DateTimeOffset.UtcNow,
                LastSeenAt = GetDateTimeOffsetSafe(entity, "lastSeenAt") ?? DateTimeOffset.UtcNow,
                EnabledContentClassificationLabels = enabledCcls
            };
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
