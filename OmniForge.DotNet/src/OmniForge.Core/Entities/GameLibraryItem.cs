using System;
using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class GameLibraryItem
    {
        public string UserId { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty; // Twitch category/game id
        public string GameName { get; set; } = string.Empty;
        public string BoxArtUrl { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

        // Twitch Content Classification Labels (CCLs) enabled for this game.
        // Null means "not configured" (do not modify Twitch channel CCLs).
        // An empty list means "configured to clear all labels".
        public List<string>? EnabledContentClassificationLabels { get; set; }
    }
}
