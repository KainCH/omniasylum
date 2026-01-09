using System;

namespace OmniForge.Core.Entities
{
    public class CounterRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string RequestedByUserId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string Status { get; set; } = "pending"; // pending | approved | rejected
        public string AdminNotes { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
