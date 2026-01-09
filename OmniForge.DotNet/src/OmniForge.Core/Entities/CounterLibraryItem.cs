using System;

namespace OmniForge.Core.Entities
{
    public class CounterLibraryItem
    {
        public string CounterId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;

        public int IncrementBy { get; set; } = 1;
        public int DecrementBy { get; set; } = 1;

        public int[] Milestones { get; set; } = Array.Empty<int>();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }
}
