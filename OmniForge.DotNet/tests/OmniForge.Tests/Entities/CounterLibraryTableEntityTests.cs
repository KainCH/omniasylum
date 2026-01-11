using System;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests.Entities
{
    public class CounterLibraryTableEntityTests
    {
        [Fact]
        public void FromItem_ThenToItem_ShouldRoundTripCoreFields()
        {
            var item = new CounterLibraryItem
            {
                CounterId = "counter-1",
                Name = "Test Counter",
                Icon = "bi-star",
                LongCommand = "!testcounter",
                AliasCommand = "!tc",
                IncrementBy = 5,
                DecrementBy = 2,
                Milestones = new[] { 10, 50 },
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                LastUpdated = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
            };

            var entity = CounterLibraryTableEntity.FromItem(item, "[10,50]");

            Assert.Equal("counter", entity.PartitionKey);
            Assert.Equal("counter-1", entity.RowKey);
            Assert.Equal("Test Counter", entity.Name);
            Assert.Equal("bi-star", entity.Icon);
            Assert.Equal("!testcounter", entity.LongCommand);
            Assert.Equal("!tc", entity.AliasCommand);
            Assert.Equal(5, entity.IncrementBy);
            Assert.Equal(2, entity.DecrementBy);
            Assert.Equal("[10,50]", entity.MilestonesJson);
            Assert.Equal(item.CreatedAt, entity.CreatedAt);
            Assert.Equal(item.LastUpdated, entity.LastUpdated);

            var roundTrip = entity.ToItem(new[] { 10, 50 });
            Assert.Equal("counter-1", roundTrip.CounterId);
            Assert.Equal("Test Counter", roundTrip.Name);
            Assert.Equal("bi-star", roundTrip.Icon);
            Assert.Equal("!testcounter", roundTrip.LongCommand);
            Assert.Equal("!tc", roundTrip.AliasCommand);
            Assert.Equal(5, roundTrip.IncrementBy);
            Assert.Equal(2, roundTrip.DecrementBy);
            Assert.Equal(new[] { 10, 50 }, roundTrip.Milestones);
            Assert.Equal(item.CreatedAt, roundTrip.CreatedAt);
            Assert.Equal(item.LastUpdated, roundTrip.LastUpdated);
        }
    }
}
