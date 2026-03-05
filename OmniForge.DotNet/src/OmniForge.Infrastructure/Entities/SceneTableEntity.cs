using System;
using Azure;
using Azure.Data.Tables;

namespace OmniForge.Infrastructure.Entities
{
    public class SceneTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // Escaped scene name
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string SceneName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    }
}
