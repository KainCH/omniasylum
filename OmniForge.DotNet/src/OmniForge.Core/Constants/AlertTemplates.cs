using System.Collections.Generic;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Constants
{
    public static class AlertTemplates
    {
        public static List<Alert> GetDefaultTemplates()
        {
            return new List<Alert>
            {
                new Alert
                {
                    Id = "follow",
                    Type = "follow",
                    Name = "New Follower",
                    VisualCue = "A door creaks open slowly",
                    Sound = "door-creak",
                    SoundDescription = "Heavy door creaking open",
                    TextPrompt = "🚪 A new patient has arrived… [User]",
                    Duration = 5000,
                    BackgroundColor = "#1a0d0d",
                    TextColor = "#ff6b6b",
                    BorderColor = "#8b1538",
                    IsDefault = true,
                    Effects = "{\"animation\":\"doorCreak\",\"svgMask\":\"fog\",\"particle\":\"dust\",\"screenShake\":true,\"soundTrigger\":\"doorCreak.wav\"}"
                },
                new Alert
                {
                    Id = "subscription",
                    Type = "subscription",
                    Name = "New Subscriber",
                    VisualCue = "Restraints snap shut",
                    Sound = "electroshock-buzz",
                    SoundDescription = "Electroshock buzz or echoing scream",
                    TextPrompt = "⚡ They've committed for the long stay. [User] - Tier [Tier]",
                    Duration = 6000,
                    BackgroundColor = "#1a0d1a",
                    TextColor = "#9147ff",
                    BorderColor = "#6441a5",
                    IsDefault = true,
                    Effects = "{\"animation\":\"electricPulse\",\"svgMask\":\"glassDistortion\",\"particle\":\"sparks\",\"screenFlicker\":true,\"glowIntensity\":\"high\",\"soundTrigger\":\"electroshock.wav\"}"
                },
                new Alert
                {
                    Id = "resub",
                    Type = "resub",
                    Name = "Resubscription",
                    VisualCue = "A file slams shut on a desk",
                    Sound = "typewriter-ding",
                    SoundDescription = "Pen scribble + typewriter ding",
                    TextPrompt = "📋 Case file reopened: [User] returns. [Months] months confined.",
                    Duration = 5500,
                    BackgroundColor = "#0d1a0d",
                    TextColor = "#00ff88",
                    BorderColor = "#1db954",
                    IsDefault = true,
                    Effects = "{\"animation\":\"typewriter\",\"svgMask\":\"paperTexture\",\"particle\":\"ink\",\"textScramble\":true,\"soundTrigger\":\"typewriter.wav\"}"
                },
                new Alert
                {
                    Id = "bits",
                    Type = "bits",
                    Name = "Bits Donation",
                    VisualCue = "Coins dropping into a metal tray",
                    Sound = "coin-drop",
                    SoundDescription = "Metallic clinking",
                    TextPrompt = "💰 [User] bribed the guards with [Amount] bits.",
                    Duration = 4000,
                    BackgroundColor = "#1a1a0d",
                    TextColor = "#ffd700",
                    BorderColor = "#b8860b",
                    IsDefault = true,
                    Effects = "{\"animation\":\"coinRain\",\"svgMask\":\"none\",\"particle\":\"coins\",\"soundTrigger\":\"coins.wav\"}"
                },
                new Alert
                {
                    Id = "raid",
                    Type = "raid",
                    Name = "Raid",
                    VisualCue = "Siren wailing, red lights flashing",
                    Sound = "siren-wail",
                    SoundDescription = "Emergency siren",
                    TextPrompt = "🚨 SECURITY BREACH! [User] is leading a riot of [Viewers] inmates!",
                    Duration = 8000,
                    BackgroundColor = "#330000",
                    TextColor = "#ff0000",
                    BorderColor = "#ff0000",
                    IsDefault = true,
                    Effects = "{\"animation\":\"sirenFlash\",\"svgMask\":\"scanlines\",\"particle\":\"debris\",\"screenShake\":true,\"soundTrigger\":\"siren.wav\"}"
                },
                new Alert
                {
                    Id = "giftsub",
                    Type = "giftsub",
                    Name = "Gift Subscription",
                    VisualCue = "A package slides under the door",
                    Sound = "paper-rustle",
                    SoundDescription = "Paper rustling",
                    TextPrompt = "🎁 [User] smuggled a key to [Recipient]!",
                    Duration = 5000,
                    BackgroundColor = "#0d1a1a",
                    TextColor = "#00ffff",
                    BorderColor = "#008b8b",
                    IsDefault = true,
                    Effects = "{\"animation\":\"slideIn\",\"svgMask\":\"none\",\"particle\":\"confetti\",\"soundTrigger\":\"rustle.wav\"}"
                },
                new Alert
                {
                    Id = "hypetrain",
                    Type = "hypetrain",
                    Name = "Hype Train",
                    VisualCue = "Steam engine whistle",
                    Sound = "train-whistle",
                    SoundDescription = "Steam whistle",
                    TextPrompt = "🚂 The transport train is leaving! Level [Level] - [Percent]%",
                    Duration = 10000,
                    BackgroundColor = "#1a0d1a",
                    TextColor = "#ff00ff",
                    BorderColor = "#8b008b",
                    IsDefault = true,
                    Effects = "{\"animation\":\"trainMove\",\"svgMask\":\"steam\",\"particle\":\"smoke\",\"soundTrigger\":\"train.wav\"}"
                },
                new Alert
                {
                    Id = "paypal_donation",
                    Type = "paypal_donation",
                    Name = "PayPal Donation",
                    VisualCue = "Cash register ringing",
                    Sound = "cash-register",
                    SoundDescription = "Ka-ching cash register sound",
                    TextPrompt = "💸 [User] donated $[Amount]! [Message]",
                    Duration = 6000,
                    BackgroundColor = "#0d1a0d",
                    TextColor = "#00ff00",
                    BorderColor = "#228b22",
                    IsDefault = true,
                    Effects = "{\"animation\":\"moneyRain\",\"svgMask\":\"none\",\"particle\":\"dollars\",\"screenShake\":true,\"glowIntensity\":\"medium\",\"soundTrigger\":\"cashregister.wav\"}"
                }
            };
        }

        public static Dictionary<string, string> GetDefaultEventMappings()
        {
            return new Dictionary<string, string>
            {
                { "channel.follow", "follow" },
                { "channel.bits.use", "bits" },
                { "chat_notification_subscribe", "subscription" },
                { "chat_notification_resub", "resub" },
                { "chat_notification_gift_sub", "giftsub" },
                { "chat_notification_community_gift", "giftsub" },
                { "chat_notification_raid", "raid" },
                { "chat_notification_bits_badge", "bits" },
                { "chat_notification_announcement", "announcement" },
                { "paypal_donation", "paypal_donation" }
            };
        }

        public static List<string> GetAllAvailableEvents()
        {
            return new List<string>
            {
                "channel.follow",
                "channel.bits.use",
                "chat_notification_subscribe",
                "chat_notification_resub",
                "chat_notification_gift_sub",
                "chat_notification_community_gift",
                "chat_notification_gift_upgrade",
                "chat_notification_prime_upgrade",
                "chat_notification_raid",
                "chat_notification_announcement",
                "chat_notification_bits_badge",
                "chat_notification_charity_donation",
                "paypal_donation"
            };
        }
    }
}
