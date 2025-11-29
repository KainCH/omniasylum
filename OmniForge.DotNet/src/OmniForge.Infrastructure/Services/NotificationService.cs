using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IDiscordService _discordService;
        private readonly ITwitchClientManager _twitchClientManager;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            IDiscordService discordService,
            ITwitchClientManager twitchClientManager,
            IOverlayNotifier overlayNotifier,
            ILogger<NotificationService> logger)
        {
            _discordService = discordService;
            _twitchClientManager = twitchClientManager;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task CheckAndSendMilestoneNotificationsAsync(User user, string counterType, int previousValue, int newValue)
        {
            try
            {
                // 1. Get Notification Settings
                var settings = user.DiscordSettings;
                if (settings == null) return;

                // 2. Determine Event Type and Thresholds
                string eventType;
                List<int> thresholds;
                bool discordEnabledForType;
                bool channelEnabledForType; // Legacy support or future feature

                switch (counterType.ToLower())
                {
                    case "deaths":
                        eventType = "death_milestone";
                        thresholds = settings.MilestoneThresholds.Deaths;
                        discordEnabledForType = settings.EnabledNotifications.DeathMilestone;
                        break;
                    case "swears":
                        eventType = "swear_milestone";
                        thresholds = settings.MilestoneThresholds.Swears;
                        discordEnabledForType = settings.EnabledNotifications.SwearMilestone;
                        break;
                    case "screams":
                        eventType = "scream_milestone";
                        thresholds = settings.MilestoneThresholds.Screams;
                        discordEnabledForType = settings.EnabledNotifications.ScreamMilestone;
                        break;
                    default:
                        return;
                }

                // Legacy check for channel notifications (if not in structured settings, default to false or check legacy prop)
                channelEnabledForType = settings.EnableChannelNotifications;

                if (!discordEnabledForType && !channelEnabledForType)
                {
                    return;
                }

                if (thresholds == null || !thresholds.Any()) return;

                // 3. Check for crossed milestones
                var crossedMilestones = thresholds.Where(t => previousValue < t && newValue >= t).ToList();

                foreach (var milestone in crossedMilestones)
                {
                    // Find previous milestone for progress display
                    var previousMilestone = thresholds
                        .Where(t => t < milestone)
                        .OrderByDescending(t => t)
                        .FirstOrDefault();

                    _logger.LogInformation("Milestone reached: {CounterType} {Milestone} for user {Username}", LogSanitizer.Sanitize(counterType), milestone, LogSanitizer.Sanitize(user.Username));

                    // 4. Send Discord Notification
                    if (discordEnabledForType && !string.IsNullOrEmpty(user.DiscordWebhookUrl))
                    {
                        await _discordService.SendNotificationAsync(user, eventType, new
                        {
                            count = milestone,
                            actualCount = newValue,
                            previousMilestone = previousMilestone,
                            fields = new[]
                            {
                                new { name = "🎯 Milestone", value = $"{milestone}", inline = true },
                                new { name = "📊 Current Count", value = $"{newValue}", inline = true },
                                new { name = "📈 Progress", value = $"{previousMilestone} → {milestone}", inline = true }
                            }
                        });
                    }

                    // 5. Send Twitch Chat Notification
                    if (channelEnabledForType)
                    {
                        string emoji = counterType.ToLower() switch
                        {
                            "deaths" => "💀",
                            "swears" => "🤬",
                            "screams" => "😱",
                            _ => "🎉"
                        };

                        string message = $"{emoji} MILESTONE REACHED! {milestone} {counterType.ToUpper()}! Current count: {newValue} {emoji}";
                        await _twitchClientManager.SendMessageAsync(user.TwitchUserId, message);
                    }

                    // 6. Emit Overlay Event
                    await _overlayNotifier.NotifyMilestoneReachedAsync(user.TwitchUserId, counterType, milestone, newValue, previousMilestone);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking milestones for user {Username}", LogSanitizer.Sanitize(user.Username));
            }
        }
    }
}
