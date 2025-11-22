using System;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class CounterTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = "counters";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public int Deaths { get; set; }
        public int Swears { get; set; }
        public int Screams { get; set; }
        public int Bits { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
        public DateTimeOffset? StreamStarted { get; set; }
        public string? LastNotifiedStreamId { get; set; }

        public Counter ToDomain()
        {
            return new Counter
            {
                TwitchUserId = PartitionKey,
                Deaths = Deaths,
                Swears = Swears,
                Screams = Screams,
                Bits = Bits,
                LastUpdated = LastUpdated,
                StreamStarted = StreamStarted,
                LastNotifiedStreamId = LastNotifiedStreamId
            };
        }

        public static CounterTableEntity FromDomain(Counter counter)
        {
            return new CounterTableEntity
            {
                PartitionKey = counter.TwitchUserId,
                RowKey = "counters",
                Deaths = counter.Deaths,
                Swears = counter.Swears,
                Screams = counter.Screams,
                Bits = counter.Bits,
                LastUpdated = counter.LastUpdated,
                StreamStarted = counter.StreamStarted,
                LastNotifiedStreamId = counter.LastNotifiedStreamId
            };
        }
    }
}
