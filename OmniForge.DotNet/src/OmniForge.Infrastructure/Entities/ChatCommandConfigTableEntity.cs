using System;
using Azure;
using Azure.Data.Tables;
using System.Text.Json;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class ChatCommandConfigTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = "chatCommands";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string CommandsConfig { get; set; } = "{}";
        public DateTimeOffset LastUpdated { get; set; }

        public ChatCommandConfiguration ToConfiguration()
        {
            if (string.IsNullOrEmpty(CommandsConfig))
            {
                return new ChatCommandConfiguration();
            }

            try
            {
                return JsonSerializer.Deserialize<ChatCommandConfiguration>(CommandsConfig) ?? new ChatCommandConfiguration();
            }
            catch
            {
                return new ChatCommandConfiguration();
            }
        }

        public static ChatCommandConfigTableEntity FromConfiguration(string userId, ChatCommandConfiguration config)
        {
            return new ChatCommandConfigTableEntity
            {
                PartitionKey = userId,
                CommandsConfig = JsonSerializer.Serialize(config),
                LastUpdated = DateTimeOffset.UtcNow
            };
        }
    }
}
