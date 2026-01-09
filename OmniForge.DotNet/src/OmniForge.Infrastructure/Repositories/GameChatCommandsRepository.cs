using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class GameChatCommandsRepository : IGameChatCommandsRepository
    {
        private readonly TableClient _tableClient;

        public GameChatCommandsRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.GameChatCommandsTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<ChatCommandConfiguration?> GetAsync(string userId, string gameId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<GameChatCommandsTableEntity>(userId, gameId);
                return response.Value.ToConfiguration();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveAsync(string userId, string gameId, ChatCommandConfiguration config)
        {
            var entity = GameChatCommandsTableEntity.FromConfiguration(userId, gameId, config);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
    }
}
