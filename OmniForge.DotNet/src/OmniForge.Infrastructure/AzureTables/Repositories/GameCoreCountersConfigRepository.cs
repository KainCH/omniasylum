using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.AzureTables.Entities;

namespace OmniForge.Infrastructure.AzureTables.Repositories;

public sealed class GameCoreCountersConfigRepository : IGameCoreCountersConfigRepository
{
    private readonly TableClient _table;

    public GameCoreCountersConfigRepository(
        TableServiceClient tableServiceClient,
        IOptions<AzureTableConfiguration> options)
    {
        var config = options.Value;
        _table = tableServiceClient.GetTableClient(config.GameCoreCountersConfigTable);
    }

    public async Task InitializeAsync()
    {
        await _table.CreateIfNotExistsAsync();
    }

    public async Task<GameCoreCountersConfig?> GetAsync(string userId, string gameId)
    {
        try
        {
            var response = await _table.GetEntityAsync<GameCoreCountersConfigTableEntity>(
                partitionKey: GameCoreCountersConfigTableEntity.GetPartitionKey(userId),
                rowKey: GameCoreCountersConfigTableEntity.GetRowKey(gameId));

            return response.Value.ToModel();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task SaveAsync(string userId, string gameId, GameCoreCountersConfig config)
    {
        var entity = GameCoreCountersConfigTableEntity.FromModel(config with
        {
            UserId = userId,
            GameId = gameId,
            UpdatedAt = config.UpdatedAt == default ? DateTimeOffset.UtcNow : config.UpdatedAt
        });

        return _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }
}
