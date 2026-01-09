using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using Microsoft.Extensions.Options;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles stream.online EventSub notifications.
    /// </summary>
    public class StreamOnlineHandler : BaseEventSubHandler
    {
        private readonly TwitchSettings _twitchSettings;
        private readonly IDiscordNotificationTracker _discordTracker;
        private readonly ITwitchAuthService _twitchAuthService;

        public StreamOnlineHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<StreamOnlineHandler> logger,
            IOptions<TwitchSettings> twitchSettings,
            IDiscordNotificationTracker discordTracker,
            ITwitchAuthService twitchAuthService)
            : base(scopeFactory, logger)
        {
            _twitchSettings = twitchSettings.Value;
            _discordTracker = discordTracker;
            _twitchAuthService = twitchAuthService;
        }

        public override string SubscriptionType => "stream.online";

        public override async Task HandleAsync(JsonElement eventData)
        {
            string? broadcasterId = GetStringProperty(eventData, "broadcaster_user_id");
            string broadcasterName = GetStringProperty(eventData, "broadcaster_user_name", "Unknown");

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return;
            }

            Logger.LogInformation("Stream Online: {BroadcasterName} ({BroadcasterId})", broadcasterName, broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
            var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();
            var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            var user = await userRepository.GetUserAsync(broadcasterId);
            if (user == null)
            {
                return;
            }

            // Update Counter
            var counters = await counterRepository.GetCountersAsync(broadcasterId);
            if (counters != null)
            {
                counters.StreamStarted = DateTimeOffset.UtcNow;
                await counterRepository.SaveCountersAsync(counters);
            }

            // Notify Overlay
            if (overlayNotifier != null && counters != null)
            {
                await overlayNotifier.NotifyStreamStartedAsync(broadcasterId, counters);
            }

            // Fetch Stream Info
            object notificationData = await GetStreamNotificationDataAsync(user, helixWrapper, broadcasterId);

            // Send Discord Notification
            try
            {
                await discordService.SendNotificationAsync(user, "stream_start", notificationData);
                _discordTracker.RecordNotification(broadcasterId, true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to send Discord notification for user {UserId}", broadcasterId);
                _discordTracker.RecordNotification(broadcasterId, false);
            }
        }

        private async Task<object> GetStreamNotificationDataAsync(
            Core.Entities.User user,
            ITwitchHelixWrapper helixWrapper,
            string userId)
        {
            try
            {
                if (!string.IsNullOrEmpty(_twitchSettings.ClientId))
                {
                    var accessToken = !string.IsNullOrEmpty(user.AccessToken)
                        ? user.AccessToken
                        : await _twitchAuthService.GetAppAccessTokenAsync();

                    if (string.IsNullOrEmpty(accessToken))
                    {
                        return new { };
                    }

                    var streams = await helixWrapper.GetStreamsAsync(
                        _twitchSettings.ClientId,
                        accessToken,
                        new List<string> { userId });

                    if (streams.Streams != null && streams.Streams.Length > 0)
                    {
                        var stream = streams.Streams[0];
                        return new
                        {
                            title = stream.Title,
                            game = stream.GameName,
                            thumbnailUrl = stream.ThumbnailUrl.Replace("{width}", "640").Replace("{height}", "360") +
                                           $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                            viewerCount = stream.ViewerCount,
                            startedAt = stream.StartedAt,
                            broadcasterName = stream.UserName
                        };
                    }

                    // Fallback to channel info if stream not available yet
                    var channelInfo = await helixWrapper.GetChannelInformationAsync(
                        _twitchSettings.ClientId,
                        accessToken,
                        userId);

                    if (channelInfo.Data != null && channelInfo.Data.Length > 0)
                    {
                        var info = channelInfo.Data[0];
                        return new
                        {
                            title = info.Title,
                            game = info.GameName,
                            thumbnailUrl = user.ProfileImageUrl,
                            viewerCount = 0,
                            startedAt = DateTime.UtcNow,
                            broadcasterName = info.BroadcasterName
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch stream info for {DisplayName}", user.DisplayName);
            }

            return new { };
        }
    }
}
