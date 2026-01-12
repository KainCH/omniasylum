using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Sends Discord invite links in Twitch chat with throttling.
    /// </summary>
    public class DiscordInviteSender : IDiscordInviteSender
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DiscordInviteSender> _logger;
        private readonly IDiscordNotificationTracker _notificationTracker;
        private readonly IMonitoringRegistry _monitoringRegistry;
        private readonly ITwitchBotEligibilityService _botEligibilityService;
        private readonly ITwitchApiService _twitchApiService;
        private readonly TimeSpan _throttleDuration = TimeSpan.FromMinutes(5);

        public DiscordInviteSender(
            IServiceScopeFactory scopeFactory,
            ILogger<DiscordInviteSender> logger,
            IDiscordNotificationTracker notificationTracker,
            IMonitoringRegistry monitoringRegistry,
            ITwitchBotEligibilityService botEligibilityService,
            ITwitchApiService twitchApiService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _notificationTracker = notificationTracker;
            _monitoringRegistry = monitoringRegistry;
            _botEligibilityService = botEligibilityService;
            _twitchApiService = twitchApiService;
        }

        public async Task SendDiscordInviteAsync(string broadcasterId)
        {
            // Check throttle
            var lastNotification = _notificationTracker.GetLastNotification(broadcasterId);
            if (lastNotification.HasValue)
            {
                if (DateTimeOffset.UtcNow - lastNotification.Value.Time < _throttleDuration)
                {
                    return; // Throttled
                }
            }

            using var scope = _scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await userRepository.GetUserAsync(broadcasterId);

            if (user == null)
            {
                return;
            }

            try
            {
                string discordInviteLink = !string.IsNullOrEmpty(user.DiscordInviteLink)
                    ? user.DiscordInviteLink
                    : "https://discord.gg/omniasylum"; // Fallback

                string message = $"Join our Discord community! {discordInviteLink}";

                // Resolve bot user id from monitoring registry, or (if needed) eligibility cache.
                string? botUserId = null;
                if (_monitoringRegistry.TryGetState(broadcasterId, out var state) && state.UseBot && !string.IsNullOrWhiteSpace(state.BotUserId))
                {
                    botUserId = state.BotUserId;
                }
                else if (!string.IsNullOrWhiteSpace(user.AccessToken))
                {
                    var eligibility = await _botEligibilityService.GetEligibilityAsync(broadcasterId, user.AccessToken);
                    _monitoringRegistry.SetState(broadcasterId, new MonitoringState(eligibility.UseBot, eligibility.BotUserId, DateTimeOffset.UtcNow));
                    if (eligibility.UseBot && !string.IsNullOrWhiteSpace(eligibility.BotUserId))
                    {
                        botUserId = eligibility.BotUserId;
                    }
                }

                if (string.IsNullOrWhiteSpace(botUserId))
                {
                    _logger.LogWarning(
                        "⚠️ Skipping Discord invite chat send (must use app/bot token only). broadcaster_id={BroadcasterId}",
                        LogSanitizer.Sanitize(broadcasterId));
                    return;
                }

                await _twitchApiService.SendChatMessageAsBotAsync(broadcasterId, botUserId, message);

                // Update tracker
                _notificationTracker.RecordNotification(broadcasterId, true);
                _logger.LogInformation("Sent Discord invite to channel {BroadcasterId}", broadcasterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Discord invite.");
                _notificationTracker.RecordNotification(broadcasterId, false);
            }
        }
    }
}
