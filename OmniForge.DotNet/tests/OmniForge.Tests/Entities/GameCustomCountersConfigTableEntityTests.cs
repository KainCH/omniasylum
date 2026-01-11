using System;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests.Entities
{
    public class GameCustomCountersConfigTableEntityTests
    {
        [Fact]
        public void ToConfiguration_WhenEmptyJson_ShouldReturnDefault()
        {
            var entity = new GameCustomCountersConfigTableEntity
            {
                countersConfig = ""
            };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.Empty(config.Counters);
        }

        [Fact]
        public void ToConfiguration_WhenInvalidJson_ShouldReturnDefault()
        {
            var entity = new GameCustomCountersConfigTableEntity
            {
                countersConfig = "{not valid json]"
            };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.Empty(config.Counters);
        }

        [Fact]
        public void FromConfiguration_ThenToConfiguration_ShouldRoundTrip()
        {
            var config = new CustomCounterConfiguration();
            config.Counters["deaths"] = new CustomCounterDefinition
            {
                Name = "Deaths",
                Icon = "bi-skull",
                IncrementBy = 2,
                DecrementBy = 1,
                Milestones = { 5, 10 }
            };

            var entity = GameCustomCountersConfigTableEntity.FromConfiguration("u1", "g1", config);

            Assert.Equal("u1", entity.PartitionKey);
            Assert.Equal("g1", entity.RowKey);
            Assert.False(string.IsNullOrWhiteSpace(entity.countersConfig));
            Assert.NotEqual(default, entity.lastUpdated);

            var roundTrip = entity.ToConfiguration();
            Assert.True(roundTrip.Counters.ContainsKey("deaths"));
            Assert.Equal("Deaths", roundTrip.Counters["deaths"].Name);
            Assert.Equal(2, roundTrip.Counters["deaths"].IncrementBy);
            Assert.Equal(2, roundTrip.Counters["deaths"].Milestones.Count);
        }
    }
}
