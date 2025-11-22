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

        public int deaths { get; set; }
        public int swears { get; set; }
        public int screams { get; set; }
        public int bits { get; set; }
        public DateTimeOffset lastUpdated { get; set; }
        public DateTimeOffset? streamStarted { get; set; }
        public string? lastNotifiedStreamId { get; set; }

        public Counter ToDomain()
        {
            return new Counter
            {
                TwitchUserId = PartitionKey,
                Deaths = deaths,
                Swears = swears,
                Screams = screams,
                Bits = bits,
                LastUpdated = lastUpdated,
                StreamStarted = streamStarted,
                LastNotifiedStreamId = lastNotifiedStreamId
            };
        }

        public static CounterTableEntity FromDomain(Counter counter)
        {
            return new CounterTableEntity
            {
                PartitionKey = counter.TwitchUserId,
                RowKey = "counters",
                deaths = counter.Deaths,
                swears = counter.Swears,
                screams = counter.Screams,
                bits = counter.Bits,
                lastUpdated = counter.LastUpdated,
                streamStarted = counter.StreamStarted,
                lastNotifiedStreamId = counter.LastNotifiedStreamId
            };
        }
    }
}
