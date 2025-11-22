using System;
using Azure;
using Azure.Data.Tables;
using System.Text.Json;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class CustomCounterConfigTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = "customCounters";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string countersConfig { get; set; } = "{}";
        public DateTimeOffset lastUpdated { get; set; }

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

        public static CustomCounterConfigTableEntity FromConfiguration(string userId, CustomCounterConfiguration config)
        {
            return new CustomCounterConfigTableEntity
            {
                PartitionKey = userId,
                countersConfig = JsonSerializer.Serialize(config),
                lastUpdated = DateTimeOffset.UtcNow
            };
        }
    }
}
