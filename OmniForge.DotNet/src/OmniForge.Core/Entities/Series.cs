using System;

namespace OmniForge.Core.Entities
{
    public class Series
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Counter Snapshot { get; set; } = new Counter();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
        public bool IsActive { get; set; } = false;
    }
}
