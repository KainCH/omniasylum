using System;

namespace OmniForge.Core.Entities
{
    public class GameContext
    {
        public string UserId { get; set; } = string.Empty;
        public string? ActiveGameId { get; set; }
        public string? ActiveGameName { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
