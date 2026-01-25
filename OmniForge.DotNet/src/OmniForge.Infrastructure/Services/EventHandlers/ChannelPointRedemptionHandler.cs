using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    public class ChannelPointRedemptionHandler : BaseEventSubHandler
    {
        public override string SubscriptionType => "channel.channel_points_custom_reward_redemption.add";

        public ChannelPointRedemptionHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<ChannelPointRedemptionHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override async Task HandleAsync(JsonElement eventData)
        {
            eventData = UnwrapEvent(eventData);

            if (!TryGetBroadcasterId(eventData, out var broadcasterId) || string.IsNullOrWhiteSpace(broadcasterId))
            {
                return;
            }

            var rewardId = TryGetRewardId(eventData);
            if (string.IsNullOrWhiteSpace(rewardId))
            {
                Logger.LogDebug("Channel point redemption missing reward id. broadcaster_user_id={BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                return;
            }

            using var scope = ScopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetService<IUserRepository>();
            var channelPointRepository = scope.ServiceProvider.GetService<IChannelPointRepository>();
            var counterRepository = scope.ServiceProvider.GetService<ICounterRepository>();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
            var discordService = scope.ServiceProvider.GetService<IDiscordService>();

            if (channelPointRepository == null)
            {
                Logger.LogWarning("⚠️ ChannelPointRepository missing; cannot process redemption. broadcaster_user_id={BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                return;
            }

            // Only act on rewards that are managed/configured in OmniForge.
            var reward = await channelPointRepository.GetRewardAsync(broadcasterId, rewardId).ConfigureAwait(false);
            if (reward == null)
            {
                Logger.LogDebug("⏭️ Ignoring channel point redemption for unmanaged reward. broadcaster_user_id={BroadcasterId}, reward_id={RewardId}",
                    LogSanitizer.Sanitize(broadcasterId),
                    LogSanitizer.Sanitize(rewardId));
                return;
            }

            var redeemerName = GetStringProperty(eventData, "user_name");
            var redeemerLogin = GetStringProperty(eventData, "user_login");
            var userInput = GetStringProperty(eventData, "user_input");
            var redemptionId = GetStringProperty(eventData, "id");

            var rewardTitleFromEvent = TryGetRewardTitle(eventData);
            var rewardTitle = !string.IsNullOrWhiteSpace(rewardTitleFromEvent) ? rewardTitleFromEvent : reward.RewardTitle;

            Logger.LogInformation(
                "🎟️ Channel point redemption: broadcaster_user_id={BroadcasterId}, reward_id={RewardId}, action={Action}, user={User}",
                LogSanitizer.Sanitize(broadcasterId),
                LogSanitizer.Sanitize(rewardId),
                LogSanitizer.Sanitize(reward.Action),
                LogSanitizer.Sanitize(!string.IsNullOrWhiteSpace(redeemerLogin) ? redeemerLogin : redeemerName));

            // Optional Discord notification
            try
            {
                if (userRepository != null && discordService != null)
                {
                    var user = await userRepository.GetUserAsync(broadcasterId).ConfigureAwait(false);
                    if (user?.DiscordSettings?.EnabledNotifications?.ChannelPointRedemption == true)
                    {
                        await discordService.SendNotificationAsync(user, "channel_point_redemption", new
                        {
                            rewardTitle,
                            rewardId,
                            action = reward.Action,
                            redeemerName,
                            redeemerLogin,
                            userInput,
                            redemptionId,
                            textPrompt = string.IsNullOrWhiteSpace(redeemerName)
                                ? $"Channel points redeemed: {rewardTitle}"
                                : $"{redeemerName} redeemed {rewardTitle}",
                            fields = new object[]
                            {
                                new { name = "🎟️ Reward", value = rewardTitle, inline = false },
                                new { name = "👤 User", value = string.IsNullOrWhiteSpace(redeemerName) ? redeemerLogin : redeemerName, inline = true },
                                new { name = "⚙️ Action", value = reward.Action, inline = true },
                                new { name = "💬 Input", value = string.IsNullOrWhiteSpace(userInput) ? "(none)" : userInput, inline = false }
                            }
                        }).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "⚠️ Failed sending Discord channel point redemption notification. broadcaster_user_id={BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
            }

            // Execute action
            try
            {
                if (string.Equals(reward.Action, "jump_scare", StringComparison.OrdinalIgnoreCase))
                {
                    if (overlayNotifier != null)
                    {
                        await overlayNotifier.NotifyCustomAlertAsync(broadcasterId, "jump_scare", new
                        {
                            rewardTitle,
                            rewardId,
                            redeemerName,
                            redeemerLogin,
                            userInput,
                            redemptionId
                        }).ConfigureAwait(false);
                    }
                    return;
                }

                if (counterRepository == null || overlayNotifier == null)
                {
                    return;
                }

                // Counter actions are intentionally silent (no alert), but we update the counters + overlay.
                if (string.Equals(reward.Action, "increment_deaths", StringComparison.OrdinalIgnoreCase))
                {
                    var counters = await counterRepository.IncrementCounterAsync(broadcasterId, "deaths", 1).ConfigureAwait(false);
                    await overlayNotifier.NotifyCounterUpdateAsync(broadcasterId, counters).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(reward.Action, "increment_swears", StringComparison.OrdinalIgnoreCase))
                {
                    var counters = await counterRepository.IncrementCounterAsync(broadcasterId, "swears", 1).ConfigureAwait(false);
                    await overlayNotifier.NotifyCounterUpdateAsync(broadcasterId, counters).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(reward.Action, "decrement_deaths", StringComparison.OrdinalIgnoreCase))
                {
                    var counters = await counterRepository.DecrementCounterAsync(broadcasterId, "deaths", 1).ConfigureAwait(false);
                    await overlayNotifier.NotifyCounterUpdateAsync(broadcasterId, counters).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(reward.Action, "decrement_swears", StringComparison.OrdinalIgnoreCase))
                {
                    var counters = await counterRepository.DecrementCounterAsync(broadcasterId, "swears", 1).ConfigureAwait(false);
                    await overlayNotifier.NotifyCounterUpdateAsync(broadcasterId, counters).ConfigureAwait(false);
                    return;
                }

                Logger.LogInformation(
                    "⏭️ Channel point action not implemented. broadcaster_user_id={BroadcasterId}, action={Action}",
                    LogSanitizer.Sanitize(broadcasterId),
                    LogSanitizer.Sanitize(reward.Action));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "❌ Failed processing channel point redemption. broadcaster_user_id={BroadcasterId}, reward_id={RewardId}",
                    LogSanitizer.Sanitize(broadcasterId),
                    LogSanitizer.Sanitize(rewardId));
            }
        }

        private static string? TryGetRewardId(JsonElement eventData)
        {
            if (eventData.TryGetProperty("reward", out var rewardObj) && rewardObj.ValueKind == JsonValueKind.Object)
            {
                if (rewardObj.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    return idProp.GetString();
                }
            }

            return null;
        }

        private static string? TryGetRewardTitle(JsonElement eventData)
        {
            if (eventData.TryGetProperty("reward", out var rewardObj) && rewardObj.ValueKind == JsonValueKind.Object)
            {
                if (rewardObj.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    return titleProp.GetString();
                }
            }

            return null;
        }
    }
}
