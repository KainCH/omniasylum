using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using Microsoft.Extensions.Options;
using OmniForge.Core.Entities;
using OmniForge.Core.Utilities;

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
        private readonly IDiscordInviteBroadcastScheduler _discordInviteBroadcastScheduler;

        public StreamOnlineHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<StreamOnlineHandler> logger,
            IOptions<TwitchSettings> twitchSettings,
            IDiscordNotificationTracker discordTracker,
            ITwitchAuthService twitchAuthService,
            IDiscordInviteBroadcastScheduler discordInviteBroadcastScheduler)
            : base(scopeFactory, logger)
        {
            _twitchSettings = twitchSettings.Value;
            _discordTracker = discordTracker;
            _twitchAuthService = twitchAuthService;
            _discordInviteBroadcastScheduler = discordInviteBroadcastScheduler;
        }

        public override string SubscriptionType => "stream.online";

        public override async Task HandleAsync(JsonElement eventData)
        {
            eventData = UnwrapEvent(eventData);

            string? broadcasterId = GetStringProperty(eventData, "broadcaster_user_id");
            string broadcasterName = GetStringProperty(eventData, "broadcaster_user_name", "Unknown");

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return;
            }

            Logger.LogInformation("Stream Online: {BroadcasterName} ({BroadcasterId})", broadcasterName, broadcasterId);

            // Start periodic Discord invite broadcasting (immediate + random 15-30 min interval).
            await _discordInviteBroadcastScheduler.StartAsync(broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
            var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();
            var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
            var twitchApiService = scope.ServiceProvider.GetService<ITwitchApiService>();
            var gameLibraryRepository = scope.ServiceProvider.GetService<IGameLibraryRepository>();
            var gameCountersRepository = scope.ServiceProvider.GetService<IGameCountersRepository>();
            var gameContextRepository = scope.ServiceProvider.GetService<IGameContextRepository>();

            var user = await userRepository.GetUserAsync(broadcasterId);
            if (user == null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;

            DateTimeOffset? startedAt = null;
            var startedAtRaw = GetStringProperty(eventData, "started_at", string.Empty);
            if (!string.IsNullOrWhiteSpace(startedAtRaw) && DateTimeOffset.TryParse(startedAtRaw, out var parsedStartedAt))
            {
                startedAt = parsedStartedAt.ToUniversalTime();
            }

            // Load current counters, then (if this is a new stream) load the saved per-game snapshot for the detected category.
            var counters = await counterRepository.GetCountersAsync(broadcasterId) ?? new Counter { TwitchUserId = broadcasterId };

            // Determine whether this is a genuine new stream start vs. a repeated "online" heartbeat.
            // Notes:
            // - In normal Twitch behavior, stream.online is emitted once per stream.
            // - In our app, we may treat repeated online signals as a heartbeat.
            // - Older builds may have written StreamStarted = now on each heartbeat; treat that as the same stream and correct.
            var isNewStream = counters.StreamStarted == null;
            if (startedAt != null)
            {
                if (counters.StreamStarted == null)
                {
                    isNewStream = true;
                }
                else
                {
                    var stored = counters.StreamStarted.Value.ToUniversalTime();
                    // Treat this as a new stream only if started_at is clearly after what we have stored.
                    // If started_at is before stored, treat as the same stream (migration/heartbeat overwrite
                    // or delayed/re-delivered notifications with an older started_at).
                    if (startedAt.Value <= stored)
                    {
                        isNewStream = false;
                    }
                    else
                    {
                        isNewStream = startedAt.Value > stored.AddMinutes(1);
                    }
                }
            }

            TwitchChannelCategoryDto? category = null;
            if (twitchApiService != null)
            {
                try
                {
                    category = await twitchApiService.GetChannelCategoryAsync(broadcasterId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "⚠️ Failed to fetch channel category on stream online for user {UserId}", LogSanitizer.Sanitize(broadcasterId));
                }
            }

            if (isNewStream && category != null && !string.IsNullOrWhiteSpace(category.GameId) && gameCountersRepository != null)
            {
                try
                {
                    var savedForGame = await gameCountersRepository.GetAsync(broadcasterId, category.GameId);
                    if (savedForGame != null)
                    {
                        counters = savedForGame;
                        counters.TwitchUserId = broadcasterId;
                        counters.LastCategoryName = category.GameName;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "⚠️ Failed loading saved per-game counters on stream online for user {UserId} game {GameId}", LogSanitizer.Sanitize(broadcasterId), LogSanitizer.Sanitize(category.GameId));
                }

                // Best-effort: persist game context for other subsystems.
                if (gameContextRepository != null)
                {
                    try
                    {
                        await gameContextRepository.SaveAsync(new GameContext
                        {
                            UserId = broadcasterId,
                            ActiveGameId = category.GameId,
                            ActiveGameName = category.GameName,
                            UpdatedAt = now
                        });
                    }
                    catch (Exception ex)
                    {
                        // Best-effort only: log and continue without failing the stream online flow.
                        Logger.LogWarning(
                            ex,
                            "⚠️ Failed to persist game context on stream online for user {UserId} game {GameId}",
                            LogSanitizer.Sanitize(broadcasterId),
                            LogSanitizer.Sanitize(category.GameId));
                    }
                }
            }

            // Update Counter
            if (startedAt != null)
            {
                // Keep StreamStarted stable for this stream instance.
                counters.StreamStarted = startedAt;
            }
            else if (counters.StreamStarted == null)
            {
                counters.StreamStarted = now;
            }
            counters.LastUpdated = now;

            // Only do "stream start" resets on genuine new stream starts.
            if (isNewStream)
            {
                counters.Bits = 0;
            }
            await counterRepository.SaveCountersAsync(counters);

            // Notify Overlay
            if (overlayNotifier != null)
            {
                await overlayNotifier.NotifyStreamStartedAsync(broadcasterId, counters);
            }

            // When the stream goes online, apply per-game CCLs from the library using the fetched category.
            if (isNewStream && twitchApiService != null && gameLibraryRepository != null)
            {
                try
                {
                    if (category != null && !string.IsNullOrWhiteSpace(category.GameId))
                    {
                        Logger.LogInformation(
                            "🎮 Stream online category for user {UserId}: {GameName} ({GameId})",
                            LogSanitizer.Sanitize(broadcasterId),
                            LogSanitizer.Sanitize(category.GameName),
                            LogSanitizer.Sanitize(category.GameId));

                        var libraryItem = await gameLibraryRepository.GetAsync(broadcasterId, category.GameId);
                        if (libraryItem == null)
                        {
                            Logger.LogInformation(
                                "➕ Auto-adding missing game to global library on stream online. user_id={UserId} game_id={GameId} game_name={GameName}",
                                LogSanitizer.Sanitize(broadcasterId),
                                LogSanitizer.Sanitize(category.GameId),
                                LogSanitizer.Sanitize(category.GameName));

                            await gameLibraryRepository.UpsertAsync(new Core.Entities.GameLibraryItem
                            {
                                // Repository is global; UserId is ignored for partitioning.
                                UserId = "global",
                                GameId = category.GameId,
                                GameName = category.GameName,
                                CreatedAt = now,
                                LastSeenAt = now,
                                BoxArtUrl = string.Empty,
                                EnabledContentClassificationLabels = null
                            });

                            // New games are unconfigured; use per-user defaults if configured, otherwise skip.
                            var fallback = user.Features?.StreamSettings?.DefaultContentClassificationLabels;
                            if (fallback != null)
                            {
                                Logger.LogInformation(
                                    "🏷️ Applying user default CCL fallback on stream online (game auto-added). user_id={UserId} game_id={GameId} enabled_ccls={Ccls}",
                                    LogSanitizer.Sanitize(broadcasterId),
                                    LogSanitizer.Sanitize(category.GameId),
                                    string.Join(", ", fallback.Select(LogSanitizer.Sanitize)));

                                await twitchApiService.UpdateChannelInformationAsync(
                                    broadcasterId,
                                    category.GameId,
                                    fallback);
                            }
                            else
                            {
                                Logger.LogInformation(
                                    "ℹ️ Game auto-added but has no admin CCL config and no user default CCL fallback; skipping CCL apply. game_id={GameId}",
                                    LogSanitizer.Sanitize(category.GameId));
                            }
                        }
                        else
                        {
                            if (libraryItem.EnabledContentClassificationLabels == null)
                            {
                                var fallback = user.Features?.StreamSettings?.DefaultContentClassificationLabels;
                                if (fallback != null)
                                {
                                    Logger.LogInformation(
                                        "🏷️ Applying user default CCL fallback on stream online (game unconfigured). user_id={UserId} game_id={GameId} enabled_ccls={Ccls}",
                                        LogSanitizer.Sanitize(broadcasterId),
                                        LogSanitizer.Sanitize(category.GameId),
                                        string.Join(", ", fallback.Select(LogSanitizer.Sanitize)));

                                    await twitchApiService.UpdateChannelInformationAsync(
                                        broadcasterId,
                                        category.GameId,
                                        fallback);
                                }
                                else
                                {
                                    Logger.LogInformation(
                                        "ℹ️ Admin CCL config not set for this game and no user default CCL fallback; skipping CCL apply. user_id={UserId} game_id={GameId}",
                                        LogSanitizer.Sanitize(broadcasterId),
                                        LogSanitizer.Sanitize(category.GameId));
                                }
                            }
                            else
                            {
                                var enabledCcls = libraryItem.EnabledContentClassificationLabels;
                                Logger.LogInformation(
                                    "🏷️ Applying admin-configured CCLs on stream online. user_id={UserId} game_id={GameId} enabled_ccls={Ccls}",
                                    LogSanitizer.Sanitize(broadcasterId),
                                    LogSanitizer.Sanitize(category.GameId),
                                    string.Join(", ", enabledCcls.Select(LogSanitizer.Sanitize)));

                                await twitchApiService.UpdateChannelInformationAsync(
                                    broadcasterId,
                                    category.GameId,
                                    enabledCcls);
                            }
                        }
                    }
                    else
                    {
                        Logger.LogInformation(
                            "🎮 Stream online: no channel category returned; skipping CCL apply. user_id={UserId}",
                            LogSanitizer.Sanitize(broadcasterId));
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "⚠️ Failed applying CCLs on stream online for user {UserId}", LogSanitizer.Sanitize(broadcasterId));
                }
            }

            // Fetch Stream Info
            object notificationData = await GetStreamNotificationDataAsync(user, helixWrapper, broadcasterId);

            // Send Discord Notification (deduped per stream instance)
            try
            {
                var streamInstanceId = (startedAt ?? counters.StreamStarted ?? now).ToUniversalTime().UtcDateTime.ToString("O");

                var claimed = false;
                try
                {
                    claimed = await counterRepository.TryClaimStreamStartDiscordNotificationAsync(broadcasterId, streamInstanceId);
                }
                catch (Exception ex)
                {
                    // If we can't safely dedupe, prefer suppressing to avoid spam.
                    Logger.LogWarning(ex, "⚠️ Failed to claim stream_start Discord notification; suppressing send. user_id={UserId}", LogSanitizer.Sanitize(broadcasterId));
                    claimed = false;
                }

                if (!claimed)
                {
                    Logger.LogInformation(
                        "🔁 Suppressed duplicate stream_start Discord announcement. user_id={UserId} stream_instance_id={StreamInstanceId}",
                        LogSanitizer.Sanitize(broadcasterId),
                        LogSanitizer.Sanitize(streamInstanceId));
                }
                else
                {
                    await discordService.SendNotificationAsync(user, "stream_start", notificationData);
                    _discordTracker.RecordNotification(broadcasterId, true);
                }
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
