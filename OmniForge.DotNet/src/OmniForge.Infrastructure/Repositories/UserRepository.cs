using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig, ILogger<UserRepository> logger)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.UsersTable);
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<User?> GetUserAsync(string twitchUserId)
        {
            try
            {
                _logger.LogDebug("📥 Getting user {UserId} from Azure Table Storage", twitchUserId);
                var response = await _tableClient.GetEntityAsync<UserTableEntity>("user", twitchUserId);
                var user = response.Value.ToDomain();
                _logger.LogDebug("✅ Retrieved user {UserId}: {DisplayName}", twitchUserId, user.DisplayName);
                return user;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("⚠️ User {UserId} not found in Azure Table Storage", twitchUserId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting user {UserId} from Azure Table Storage", twitchUserId);
                throw;
            }
        }

        public async Task SaveUserAsync(User user)
        {
            try
            {
                _logger.LogInformation("💾 Saving user {UserId} ({DisplayName}) to Azure Table Storage", user.TwitchUserId, user.DisplayName);
                _logger.LogDebug("📋 OverlaySettings: Position={Position}, Scale={Scale}, Enabled={Enabled}",
                    user.OverlaySettings?.Position, user.OverlaySettings?.Scale, user.OverlaySettings?.Enabled);
                _logger.LogInformation("🔗 DiscordWebhookUrl: {WebhookUrl}",
                    string.IsNullOrEmpty(user.DiscordWebhookUrl) ? "EMPTY" : $"{user.DiscordWebhookUrl.Substring(0, Math.Min(50, user.DiscordWebhookUrl.Length))}...");

                var entity = UserTableEntity.FromDomain(user);

                _logger.LogDebug("📦 Serialized overlaySettings: {OverlaySettings}", entity.overlaySettings);
                _logger.LogDebug("📦 Entity discordWebhookUrl: {WebhookUrl}",
                    string.IsNullOrEmpty(entity.discordWebhookUrl) ? "EMPTY" : $"{entity.discordWebhookUrl.Substring(0, Math.Min(50, entity.discordWebhookUrl.Length))}...");

                await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
                _logger.LogInformation("✅ Successfully saved user {UserId} to Azure Table Storage", user.TwitchUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving user {UserId} to Azure Table Storage", user.TwitchUserId);
                throw;
            }
        }

        public async Task DeleteUserAsync(string twitchUserId)
        {
            try
            {
                _logger.LogInformation("🗑️ Deleting user {UserId} from Azure Table Storage", twitchUserId);
                await _tableClient.DeleteEntityAsync("user", twitchUserId);
                _logger.LogInformation("✅ Successfully deleted user {UserId}", twitchUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting user {UserId}", twitchUserId);
                throw;
            }
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            var users = new List<User>();
            var query = _tableClient.QueryAsync<UserTableEntity>(filter: $"PartitionKey eq 'user'");

            await foreach (var entity in query)
            {
                users.Add(entity.ToDomain());
            }

            return users;
        }

        public async Task<ChatCommandConfiguration> GetChatCommandsConfigAsync(string userId)
        {
            try
            {
                // Chat commands are stored in the 'users' table but with a different PartitionKey/RowKey strategy?
                // The legacy code uses `database.getUserChatCommands` which likely queries the `users` table or a separate one.
                // Let's assume we store it in the `users` table with PK={UserId} and RK="chatCommands" to keep it close to user data but separate row.
                // Wait, UserTableEntity uses PK="user" and RK={UserId}.
                // Let's use PK={UserId} and RK="chatCommands" for this config.

                var response = await _tableClient.GetEntityAsync<ChatCommandConfigTableEntity>(userId, "chatCommands");
                return response.Value.ToConfiguration();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return new ChatCommandConfiguration();
            }
        }

        public async Task SaveChatCommandsConfigAsync(string userId, ChatCommandConfiguration config)
        {
            var entity = ChatCommandConfigTableEntity.FromConfiguration(userId, config);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
    }
}
