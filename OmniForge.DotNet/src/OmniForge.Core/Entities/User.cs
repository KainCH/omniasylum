using System;
using System.Collections.Generic;

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
        public DiscordSettings DiscordSettings { get; set; } = new DiscordSettings();
        public bool IsActive { get; set; } = true;
        public string StreamStatus { get; set; } = "offline";
        public List<string> ManagedStreamers { get; set; } = new List<string>();
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
        public StreamSettings StreamSettings { get; set; } = new StreamSettings();
    }

    public class StreamSettings
    {
        public BitThresholds BitThresholds { get; set; } = new BitThresholds();
        public bool AutoStartStream { get; set; } = false;
        public bool ResetOnStreamStart { get; set; } = true;
        public bool AutoIncrementCounters { get; set; } = false;
    }

    public class BitThresholds
    {
        public int Death { get; set; } = 100;
        public int Swear { get; set; } = 50;
        public int Celebration { get; set; } = 10;
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

    public class DiscordSettings
    {
        public string TemplateStyle { get; set; } = "asylum_themed";
        public DiscordEnabledNotifications EnabledNotifications { get; set; } = new DiscordEnabledNotifications();
        public DiscordMilestoneThresholds MilestoneThresholds { get; set; } = new DiscordMilestoneThresholds();

        // Legacy/Flat properties for backward compatibility if needed,
        // but we should prefer the structured ones.
        public bool EnableChannelNotifications { get; set; } = false;
    }

    public class DiscordEnabledNotifications
    {
        public bool DeathMilestone { get; set; } = true;
        public bool SwearMilestone { get; set; } = true;
        public bool ScreamMilestone { get; set; } = true;
        public bool StreamStart { get; set; } = true;
        public bool StreamEnd { get; set; } = false;
        public bool FollowerGoal { get; set; } = false;
        public bool SubscriberMilestone { get; set; } = false;
        public bool ChannelPointRedemption { get; set; } = false;
    }

    public class DiscordMilestoneThresholds
    {
        public List<int> Deaths { get; set; } = new List<int> { 10, 25, 50, 100, 250, 500 };
        public List<int> Swears { get; set; } = new List<int> { 25, 50, 100, 200, 500 };
        public List<int> Screams { get; set; } = new List<int> { 10, 25, 50, 100 };
    }
}
