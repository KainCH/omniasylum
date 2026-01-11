using System;
using Azure;
using Azure.Data.Tables;

namespace OmniForge.Infrastructure.Entities
{
    public class GameCoreCountersConfigTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // GameId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public bool DeathsEnabled { get; set; } = true;
        public bool SwearsEnabled { get; set; } = true;
        public bool ScreamsEnabled { get; set; } = true;
        public bool BitsEnabled { get; set; } = false;

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
