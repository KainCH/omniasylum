using System;
using Azure;
using Azure.Data.Tables;

namespace OmniForge.Infrastructure.Entities
{
    public class BotCredentialsTableEntity : ITableEntity
    {
        public const string Partition = "system";
        public const string Row = "forgeBot";

        public string PartitionKey { get; set; } = Partition;
        public string RowKey { get; set; } = Row;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Username { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset TokenExpiry { get; set; } = DateTimeOffset.MinValue;
    }
}
