using System;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class CounterLibraryTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "counter";
        public string RowKey { get; set; } = string.Empty; // CounterId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int IncrementBy { get; set; } = 1;
        public int DecrementBy { get; set; } = 1;
        public string MilestonesJson { get; set; } = "[]";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

        public CounterLibraryItem ToItem(int[] milestones)
        {
            return new CounterLibraryItem
            {
                CounterId = RowKey,
                Name = Name,
                Icon = Icon,
                IncrementBy = IncrementBy,
                DecrementBy = DecrementBy,
                Milestones = milestones,
                CreatedAt = CreatedAt,
                LastUpdated = LastUpdated
            };
        }

        public static CounterLibraryTableEntity FromItem(CounterLibraryItem item, string milestonesJson)
        {
            return new CounterLibraryTableEntity
            {
                PartitionKey = "counter",
                RowKey = item.CounterId,
                Name = item.Name,
                Icon = item.Icon,
                IncrementBy = item.IncrementBy,
                DecrementBy = item.DecrementBy,
                MilestonesJson = milestonesJson,
                CreatedAt = item.CreatedAt,
                LastUpdated = item.LastUpdated
            };
        }
    }
}
