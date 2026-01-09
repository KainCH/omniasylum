using System;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class GameCustomCountersConfigTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // GameId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string countersConfig { get; set; } = "{}";
        public DateTimeOffset lastUpdated { get; set; } = DateTimeOffset.UtcNow;

        public CustomCounterConfiguration ToConfiguration()
        {
            if (string.IsNullOrEmpty(countersConfig))
            {
                return new CustomCounterConfiguration();
            }

            try
            {
                return JsonSerializer.Deserialize<CustomCounterConfiguration>(countersConfig) ?? new CustomCounterConfiguration();
            }
            catch
            {
                return new CustomCounterConfiguration();
            }
        }

        public static GameCustomCountersConfigTableEntity FromConfiguration(string userId, string gameId, CustomCounterConfiguration config)
        {
            return new GameCustomCountersConfigTableEntity
            {
                PartitionKey = userId,
                RowKey = gameId,
                countersConfig = JsonSerializer.Serialize(config),
                lastUpdated = DateTimeOffset.UtcNow
            };
        }
    }
}
