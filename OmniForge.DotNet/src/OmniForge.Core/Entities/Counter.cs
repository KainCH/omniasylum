using System;
using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class Counter
    {
        public string TwitchUserId { get; set; } = string.Empty;
        public int Deaths { get; set; }
        public int Swears { get; set; }
        public int Screams { get; set; }
        public int Bits { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
        public DateTimeOffset? StreamStarted { get; set; }
        public string? LastNotifiedStreamId { get; set; }
        public string? LastCategoryName { get; set; }
        public Dictionary<string, int> CustomCounters { get; set; } = new Dictionary<string, int>();
    }
}
