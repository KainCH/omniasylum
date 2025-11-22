using System;

namespace OmniForge.Core.Entities
{
    public class Alert
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string VisualCue { get; set; } = string.Empty;
        public string Sound { get; set; } = string.Empty;
        public string SoundDescription { get; set; } = string.Empty;
        public string TextPrompt { get; set; } = string.Empty;
        public int Duration { get; set; } = 4000;
        public string BackgroundColor { get; set; } = "#1a0d0d";
        public string TextColor { get; set; } = "#ffffff";
        public string BorderColor { get; set; } = "#666666";
        public string Effects { get; set; } = "{}"; // JSON string for effects configuration
        public bool IsEnabled { get; set; } = true;
        public bool IsDefault { get; set; } = false;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
