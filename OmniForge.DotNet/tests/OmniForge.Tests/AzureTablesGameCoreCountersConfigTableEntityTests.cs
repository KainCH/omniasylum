using System;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.AzureTables.Entities;
using Xunit;

namespace OmniForge.Tests
{
    public class AzureTablesGameCoreCountersConfigTableEntityTests
    {
        [Fact]
        public void FromModel_ThenToModel_ShouldRoundTripAndUseStableKeys()
        {
            var model = new GameCoreCountersConfig(
                UserId: "u1",
                GameId: "g1",
                DeathsEnabled: true,
                SwearsEnabled: false,
                ScreamsEnabled: true,
                BitsEnabled: false,
                UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var entity = GameCoreCountersConfigTableEntity.FromModel(model);

            Assert.Equal("u1", entity.PartitionKey);
            Assert.Equal("g1", entity.RowKey);
            Assert.Equal("u1", GameCoreCountersConfigTableEntity.GetPartitionKey("u1"));
            Assert.Equal("g1", GameCoreCountersConfigTableEntity.GetRowKey("g1"));

            var roundTrip = entity.ToModel();
            Assert.Equal(model.UserId, roundTrip.UserId);
            Assert.Equal(model.GameId, roundTrip.GameId);
            Assert.Equal(model.DeathsEnabled, roundTrip.DeathsEnabled);
            Assert.Equal(model.SwearsEnabled, roundTrip.SwearsEnabled);
            Assert.Equal(model.ScreamsEnabled, roundTrip.ScreamsEnabled);
            Assert.Equal(model.BitsEnabled, roundTrip.BitsEnabled);
            Assert.Equal(model.UpdatedAt, roundTrip.UpdatedAt);
        }
    }
}
