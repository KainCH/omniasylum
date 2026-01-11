using System;
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
    public class GameCoreCountersConfigRepository : IGameCoreCountersConfigRepository
    {
        private readonly TableClient _tableClient;

        public GameCoreCountersConfigRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.GameCoreCountersConfigTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<GameCoreCountersConfig?> GetAsync(string userId, string gameId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<GameCoreCountersConfigTableEntity>(userId, gameId);
                var entity = response.Value;
                return new GameCoreCountersConfig(
                    UserId: entity.PartitionKey,
                    GameId: entity.RowKey,
                    DeathsEnabled: entity.DeathsEnabled,
                    SwearsEnabled: entity.SwearsEnabled,
                    ScreamsEnabled: entity.ScreamsEnabled,
                    BitsEnabled: entity.BitsEnabled,
                    UpdatedAt: entity.UpdatedAt
                );
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveAsync(string userId, string gameId, GameCoreCountersConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var entity = new GameCoreCountersConfigTableEntity
            {
                PartitionKey = userId,
                RowKey = gameId,
                DeathsEnabled = config.DeathsEnabled,
                SwearsEnabled = config.SwearsEnabled,
                ScreamsEnabled = config.ScreamsEnabled,
                BitsEnabled = config.BitsEnabled,
                UpdatedAt = config.UpdatedAt == default ? DateTimeOffset.UtcNow : config.UpdatedAt
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
    }
}
