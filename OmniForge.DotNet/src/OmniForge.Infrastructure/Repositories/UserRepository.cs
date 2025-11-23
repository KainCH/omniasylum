using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly TableClient _tableClient;

        public UserRepository(TableServiceClient tableServiceClient)
        {
            _tableClient = tableServiceClient.GetTableClient("users");
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<User?> GetUserAsync(string twitchUserId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<UserTableEntity>("user", twitchUserId);
                return response.Value.ToDomain();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveUserAsync(User user)
        {
            var entity = UserTableEntity.FromDomain(user);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task DeleteUserAsync(string twitchUserId)
        {
            await _tableClient.DeleteEntityAsync("user", twitchUserId);
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
