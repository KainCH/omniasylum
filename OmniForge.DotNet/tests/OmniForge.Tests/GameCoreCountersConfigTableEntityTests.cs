using System;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests
{
    public class GameCoreCountersConfigTableEntityTests
    {
        [Fact]
        public void Defaults_ShouldBeReasonable()
        {
            var before = DateTimeOffset.UtcNow.AddMinutes(-1);
            var entity = new GameCoreCountersConfigTableEntity();
            var after = DateTimeOffset.UtcNow.AddMinutes(1);

            Assert.Equal(string.Empty, entity.PartitionKey);
            Assert.Equal(string.Empty, entity.RowKey);
            Assert.True(entity.DeathsEnabled);
            Assert.True(entity.SwearsEnabled);
            Assert.True(entity.ScreamsEnabled);
            Assert.False(entity.BitsEnabled);
            Assert.InRange(entity.UpdatedAt, before, after);
        }

        [Fact]
        public void CanSetProperties_ShouldRoundTripValues()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var entity = new GameCoreCountersConfigTableEntity
            {
                PartitionKey = "u1",
                RowKey = "g1",
                DeathsEnabled = false,
                SwearsEnabled = true,
                ScreamsEnabled = false,
                BitsEnabled = true,
                UpdatedAt = now
            };

            Assert.Equal("u1", entity.PartitionKey);
            Assert.Equal("g1", entity.RowKey);
            Assert.False(entity.DeathsEnabled);
            Assert.True(entity.SwearsEnabled);
            Assert.False(entity.ScreamsEnabled);
            Assert.True(entity.BitsEnabled);
            Assert.Equal(now, entity.UpdatedAt);
        }
    }
}
