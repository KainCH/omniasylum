using System;
using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class ScheduledMessageEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Message { get; set; } = string.Empty;
        public int IntervalMinutes { get; set; } = 30;
        public bool Enabled { get; set; } = true;
    }

    public class BotSettings
    {
        // Event-triggered single-message templates (null/empty = disabled)
        public string? StreamStartMessage { get; set; }
        public string? RaidReceivedMessage { get; set; }
        public string? NewSubMessage { get; set; }
        public string? GiftSubMessage { get; set; }
        public string? ResubMessage { get; set; }
        public string? FirstTimeChatMessage { get; set; }
        public string? BrbMessage { get; set; }
        public string? BackMessage { get; set; }
        public string? ClipAnnouncementMessage { get; set; }

        // Scheduled / recurring posts
        public List<ScheduledMessageEntry> ScheduledMessages { get; set; } = new();

        // Configurable link / social commands (key = "discord" without leading '!', value = response string)
        public Dictionary<string, string> LinkCommands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
