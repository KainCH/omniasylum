using System;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests
{
    public class CounterRequestTableEntityTests
    {
        [Fact]
        public void FromRequest_ThenToRequest_ShouldRoundTripFields()
        {
            var request = new CounterRequest
            {
                RequestId = "r1",
                RequestedByUserId = "u1",
                Name = "Counter",
                Icon = "bi-star",
                Description = "desc",
                Status = "approved",
                AdminNotes = "note",
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
            };

            var entity = CounterRequestTableEntity.FromRequest(request);

            Assert.Equal("approved", entity.PartitionKey);
            Assert.Equal("r1", entity.RowKey);
            Assert.Equal("u1", entity.RequestedByUserId);

            var roundTrip = entity.ToRequest();
            Assert.Equal("r1", roundTrip.RequestId);
            Assert.Equal("u1", roundTrip.RequestedByUserId);
            Assert.Equal("Counter", roundTrip.Name);
            Assert.Equal("bi-star", roundTrip.Icon);
            Assert.Equal("desc", roundTrip.Description);
            Assert.Equal("approved", roundTrip.Status);
            Assert.Equal("note", roundTrip.AdminNotes);
            Assert.Equal(request.CreatedAt, roundTrip.CreatedAt);
            Assert.Equal(request.UpdatedAt, roundTrip.UpdatedAt);
        }
    }
}
