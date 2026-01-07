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

        public string username { get; set; } = string.Empty;
        public string accessToken { get; set; } = string.Empty;
        public string refreshToken { get; set; } = string.Empty;
        public DateTimeOffset tokenExpiry { get; set; } = DateTimeOffset.MinValue;
    }
}
