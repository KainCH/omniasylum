using System;
using Azure;
using Azure.Data.Tables;

namespace OmniForge.Infrastructure.Entities
{
    public class BroadcastProfileTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // ProfileId (Guid)
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        /// <summary>
        /// JSON-serialized BroadcastProfile data (name, scene actions, checklist items).
        /// </summary>
        public string ProfileJson { get; set; } = "{}";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
