using System;
using Azure;
using Azure.Data.Tables;

namespace OmniForge.Infrastructure.Entities
{
    public class SceneActionTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // Escaped scene name
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        /// <summary>
        /// JSON-serialized SceneAction data (counter visibility, timer, overtime config).
        /// </summary>
        public string ActionJson { get; set; } = "{}";

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
