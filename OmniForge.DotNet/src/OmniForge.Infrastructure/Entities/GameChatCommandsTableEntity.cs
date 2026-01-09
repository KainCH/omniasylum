using System;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class GameChatCommandsTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // GameId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string commandsConfig { get; set; } = "{}";
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

        public ChatCommandConfiguration ToConfiguration()
        {
            if (string.IsNullOrEmpty(commandsConfig))
            {
                return new ChatCommandConfiguration();
            }

            try
            {
                return JsonSerializer.Deserialize<ChatCommandConfiguration>(commandsConfig) ?? new ChatCommandConfiguration();
            }
            catch
            {
                return new ChatCommandConfiguration();
            }
        }

        public static GameChatCommandsTableEntity FromConfiguration(string userId, string gameId, ChatCommandConfiguration config)
        {
            return new GameChatCommandsTableEntity
            {
                PartitionKey = userId,
                RowKey = gameId,
                commandsConfig = JsonSerializer.Serialize(config),
                LastUpdated = DateTimeOffset.UtcNow
            };
        }
    }
}
