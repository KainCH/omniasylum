using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.AzureTables.Entities;

public sealed class GameCoreCountersConfigTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public bool DeathsEnabled { get; set; }

    public bool SwearsEnabled { get; set; }

    public bool ScreamsEnabled { get; set; }

    public bool BitsEnabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static string GetPartitionKey(string userId) => userId;

    public static string GetRowKey(string gameId) => gameId;

    public static GameCoreCountersConfigTableEntity FromModel(GameCoreCountersConfig model)
    {
        return new GameCoreCountersConfigTableEntity
        {
            PartitionKey = GetPartitionKey(model.UserId),
            RowKey = GetRowKey(model.GameId),
            DeathsEnabled = model.DeathsEnabled,
            SwearsEnabled = model.SwearsEnabled,
            ScreamsEnabled = model.ScreamsEnabled,
            BitsEnabled = model.BitsEnabled,
            UpdatedAt = model.UpdatedAt
        };
    }

    public GameCoreCountersConfig ToModel()
    {
        return new GameCoreCountersConfig(
            UserId: PartitionKey,
            GameId: RowKey,
            DeathsEnabled: DeathsEnabled,
            SwearsEnabled: SwearsEnabled,
            ScreamsEnabled: ScreamsEnabled,
            BitsEnabled: BitsEnabled,
            UpdatedAt: UpdatedAt
        );
    }
}
