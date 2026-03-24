using System;
using System.Collections.Generic;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class LicenseService : ILicenseService
    {
        private static readonly Dictionary<LicenseTier, HashSet<string>> TierFeatures = new()
        {
            [LicenseTier.Free] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StreamOverlay", "ChatCommands", "DiscordNotifications", "StreamAlerts"
            },
            [LicenseTier.Pro] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StreamOverlay", "ChatCommands", "DiscordNotifications", "StreamAlerts",
                "OverlayV2", "ChannelPoints", "AutoClip", "BitsIntegration"
            },
            [LicenseTier.Premium] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StreamOverlay", "ChatCommands", "DiscordNotifications", "StreamAlerts",
                "OverlayV2", "ChannelPoints", "AutoClip", "BitsIntegration",
                "SceneSync", "Analytics", "AlertAnimations", "CustomCommands"
            }
        };

        public LicenseTier GetEffectiveTier(User user)
        {
            if (user == null) return LicenseTier.Free;
            if (!IsLicenseActive(user)) return LicenseTier.Free;
            return user.LicenseTier;
        }

        public bool HasFeatureAccess(User user, string featureName)
        {
            var tier = GetEffectiveTier(user);
            return TierFeatures.TryGetValue(tier, out var features) && features.Contains(featureName);
        }

        public bool IsLicenseActive(User user)
        {
            if (user == null) return false;
            if (user.LicenseExpiresAt == null) return true;
            return user.LicenseExpiresAt.Value > DateTimeOffset.UtcNow;
        }
    }
}
