using System;

namespace OmniForge.Core.Entities
{
    public class CounterLibraryItem
    {
        public string CounterId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;

        // Optional chat commands to use for this counter.
        // If unset, defaults to "!{CounterId}" and no alias.
        public string? LongCommand { get; set; }
        public string? AliasCommand { get; set; }

        public int IncrementBy { get; set; } = 1;
        public int DecrementBy { get; set; } = 1;

        public int[] Milestones { get; set; } = Array.Empty<int>();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }
}
