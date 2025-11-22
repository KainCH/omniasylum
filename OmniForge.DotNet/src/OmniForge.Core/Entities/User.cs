using System;

namespace OmniForge.Core.Entities
{
    public class User
    {
        public string TwitchUserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset TokenExpiry { get; set; }
        public string Role { get; set; } = "streamer";
        public FeatureFlags Features { get; set; } = new FeatureFlags();
        public OverlaySettings OverlaySettings { get; set; } = new OverlaySettings();
        public string DiscordWebhookUrl { get; set; } = string.Empty;
        public string DiscordInviteLink { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string StreamStatus { get; set; } = "offline";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastLogin { get; set; }
    }

    public class FeatureFlags
    {
        public bool ChatCommands { get; set; } = true;
        public bool ChannelPoints { get; set; } = false;
        public bool AutoClip { get; set; } = false;
        public bool CustomCommands { get; set; } = false;
        public bool Analytics { get; set; } = false;
        public bool Webhooks { get; set; } = false;
        public bool BitsIntegration { get; set; } = false;
        public bool StreamOverlay { get; set; } = false;
        public bool AlertAnimations { get; set; } = false;
        public bool DiscordNotifications { get; set; } = true;
        public bool DiscordWebhook { get; set; } = false;
        public string TemplateStyle { get; set; } = "asylum_themed";
        public bool StreamAlerts { get; set; } = true;
    }

    public class OverlaySettings
    {
        public bool Enabled { get; set; } = false;
        public string Position { get; set; } = "top-right";
        public OverlayCounters Counters { get; set; } = new OverlayCounters();
        public BitsGoal BitsGoal { get; set; } = new BitsGoal();
        public OverlayTheme Theme { get; set; } = new OverlayTheme();
        public OverlayAnimations Animations { get; set; } = new OverlayAnimations();
    }

    public class OverlayCounters
    {
        public bool Deaths { get; set; } = true;
        public bool Swears { get; set; } = true;
        public bool Screams { get; set; } = true;
        public bool Bits { get; set; } = false;
    }

    public class BitsGoal
    {
        public int Target { get; set; } = 1000;
        public int Current { get; set; } = 0;
    }

    public class OverlayTheme
    {
        public string BackgroundColor { get; set; } = "rgba(0, 0, 0, 0.7)";
        public string BorderColor { get; set; } = "#d4af37";
        public string TextColor { get; set; } = "white";
    }

    public class OverlayAnimations
    {
        public bool Enabled { get; set; } = true;
        public bool ShowAlerts { get; set; } = true;
        public bool CelebrationEffects { get; set; } = true;
    }
}
