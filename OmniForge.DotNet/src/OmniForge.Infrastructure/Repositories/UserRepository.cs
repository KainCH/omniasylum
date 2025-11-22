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
            _tableClient.CreateIfNotExists();
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
    }
}
