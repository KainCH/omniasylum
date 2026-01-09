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

namespace OmniForge.Infrastructure.Repositories
{
    public class GameCountersRepository : IGameCountersRepository
    {
        private readonly TableClient _tableClient;

        public GameCountersRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.GameCountersTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<Counter?> GetAsync(string userId, string gameId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(userId, gameId);
                var entity = response.Value;

                var customCounters = ParseCustomCounters(entity);

                return new Counter
                {
                    TwitchUserId = userId,
                    Deaths = entity.GetInt32("Deaths") ?? entity.GetInt32("deaths") ?? 0,
                    Swears = entity.GetInt32("Swears") ?? entity.GetInt32("swears") ?? 0,
                    Screams = entity.GetInt32("Screams") ?? entity.GetInt32("screams") ?? 0,
                    Bits = entity.GetInt32("Bits") ?? entity.GetInt32("bits") ?? 0,
                    LastUpdated = GetDateTimeOffsetSafe(entity, "LastUpdated") ?? GetDateTimeOffsetSafe(entity, "lastUpdated") ?? DateTimeOffset.UtcNow,
                    StreamStarted = null,
                    LastNotifiedStreamId = null,
                    CustomCounters = customCounters
                };
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveAsync(string userId, string gameId, Counter counters)
        {
            var entity = new TableEntity(userId, gameId)
            {
                ["Deaths"] = counters.Deaths,
                ["Swears"] = counters.Swears,
                ["Screams"] = counters.Screams,
                ["Bits"] = counters.Bits,
                ["LastUpdated"] = counters.LastUpdated,
                ["customCounters"] = JsonSerializer.Serialize(counters.CustomCounters ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        private static Dictionary<string, int> ParseCustomCounters(TableEntity entity)
        {
            try
            {
                if (entity.TryGetValue("customCounters", out var raw) && raw is string json && !string.IsNullOrWhiteSpace(json))
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                    return parsed != null
                        ? new Dictionary<string, int>(parsed, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // ignore parsing errors for backward compatibility
            }

            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private static DateTimeOffset? GetDateTimeOffsetSafe(TableEntity entity, string key)
        {
            if (entity.TryGetValue(key, out var value))
            {
                if (value is DateTimeOffset dto) return dto;
                if (value is DateTime dt) return new DateTimeOffset(dt);
                if (value is string s && DateTimeOffset.TryParse(s, out var parsedDto)) return parsedDto;
            }
            return null;
        }
    }
}
