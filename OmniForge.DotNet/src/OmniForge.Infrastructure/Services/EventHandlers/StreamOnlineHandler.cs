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
        private readonly ILogValueSanitizer _logValueSanitizer;
        private readonly IBotCredentialRepository _botCredentialRepository;

        public StreamOnlineHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<StreamOnlineHandler> logger,
            IOptions<TwitchSettings> twitchSettings,
            IDiscordNotificationTracker discordTracker,
            ITwitchAuthService twitchAuthService,
            IDiscordInviteBroadcastScheduler discordInviteBroadcastScheduler,
            ILogValueSanitizer logValueSanitizer,
            IBotCredentialRepository botCredentialRepository)
            : base(scopeFactory, logger)
        {
            _twitchSettings = twitchSettings.Value;
            _discordTracker = discordTracker;
            _twitchAuthService = twitchAuthService;
            _discordInviteBroadcastScheduler = discordInviteBroadcastScheduler;
            _logValueSanitizer = logValueSanitizer;
            _botCredentialRepository = botCredentialRepository;
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

            string safeBroadcasterId = broadcasterId;

            Logger.LogInformation("[EventSub] stream.online received: {BroadcasterName} ({BroadcasterId})", broadcasterName, broadcasterId);

            // Start periodic Discord invite broadcasting (immediate + random 15-30 min interval).
            await _discordInviteBroadcastScheduler.StartAsync(safeBroadcasterId);

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

            var user = await userRepository.GetUserAsync(safeBroadcasterId);
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
            var counters = await counterRepository.GetCountersAsync(safeBroadcasterId) ?? new Counter { TwitchUserId = safeBroadcasterId };

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
                    // If started_at is before stored (migration/heartbeat overwrite or delayed/re-delivered notifications
                    // with an older started_at), this will evaluate to false.
                    isNewStream = startedAt.Value > stored.AddMinutes(1);
                }
            }

            if (isNewStream)
            {
                Logger.LogInformation("[EventSub] 🟢 New stream session for {BroadcasterName} ({BroadcasterId}), started_at={StartedAt}",
                    broadcasterName, safeBroadcasterId, startedAt);
            }
            else
            {
                Logger.LogInformation("[EventSub] 💓 stream.online heartbeat for {BroadcasterName} ({BroadcasterId})",
                    broadcasterName, safeBroadcasterId);
            }

            TwitchChannelCategoryDto? category = null;
            if (twitchApiService != null)
            {
                try
                {
                    category = await twitchApiService.GetChannelCategoryAsync(safeBroadcasterId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "⚠️ Failed to fetch channel category on stream online for user {UserId}", _logValueSanitizer.Safe(safeBroadcasterId));
                }
            }

            if (isNewStream && category != null && !string.IsNullOrWhiteSpace(category.GameId) && gameCountersRepository != null)
            {
                string safeGameId = category.GameId!;
                string safeGameName = category.GameName ?? string.Empty;

                try
                {
                    var savedForGame = await gameCountersRepository.GetAsync(safeBroadcasterId!, safeGameId!);
                    if (savedForGame != null)
                    {
                        counters = savedForGame;
                        counters.TwitchUserId = safeBroadcasterId;
                        counters.LastCategoryName = safeGameName;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "⚠️ Failed loading saved per-game counters on stream online for user {UserId} game {GameId}", _logValueSanitizer.Safe(safeBroadcasterId), _logValueSanitizer.Safe(safeGameId));
                }

                // Best-effort: persist game context for other subsystems.
                if (gameContextRepository != null)
                {
                    try
                    {
                        await gameContextRepository.SaveAsync(new GameContext
                        {
                            UserId = safeBroadcasterId,
                            ActiveGameId = safeGameId,
                            ActiveGameName = safeGameName,
                            UpdatedAt = now
                        });
                    }
                    catch (Exception ex)
                    {
                        // Best-effort only: log and continue without failing the stream online flow.
                        Logger.LogWarning(
                            ex,
                            "⚠️ Failed to persist game context on stream online for user {UserId} game {GameId}",
                            _logValueSanitizer.Safe(safeBroadcasterId),
                            _logValueSanitizer.Safe(safeGameId));
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
                await overlayNotifier.NotifyStreamStartedAsync(safeBroadcasterId!, counters);

                // Keep the overlay visible: streamStarted is not treated as a heartbeat by overlay.html.
                // Emit an explicit status update so clients can mark the stream as live immediately.
                await overlayNotifier.NotifyStreamStatusUpdateAsync(safeBroadcasterId!, "live");
            }

            // Dashboard feed + bot services on stream.online
            scope.ServiceProvider.GetService<IDashboardFeedService>()?.SetLiveStatus(safeBroadcasterId, true);
            var botReactionOnline = scope.ServiceProvider.GetService<IBotReactionService>();
            scope.ServiceProvider.GetService<IScheduledMessageService>()?.StartForUser(safeBroadcasterId);
            if (botReactionOnline != null) await botReactionOnline.HandleStreamStartAsync(safeBroadcasterId);

            // When the stream goes online, apply per-game CCLs from the library using the fetched category.
            if (isNewStream && twitchApiService != null && gameLibraryRepository != null)
            {
                try
                {
                    if (category != null && !string.IsNullOrWhiteSpace(category.GameId))
                    {
                        string safeGameId = category.GameId!;
                        string safeGameName = category.GameName ?? string.Empty;
                        Logger.LogInformation(
                            "🎮 Stream online category for user {UserId}: {GameName} ({GameId})",
                            _logValueSanitizer.Safe(safeBroadcasterId),
                            _logValueSanitizer.Safe(safeGameName),
                            _logValueSanitizer.Safe(safeGameId));

                        var libraryItem = await gameLibraryRepository.GetAsync(safeBroadcasterId!, safeGameId!);
                        if (libraryItem == null)
                        {
                            Logger.LogInformation(
                                "➕ Auto-adding missing game to global library on stream online. user_id={UserId} game_id={GameId} game_name={GameName}",
                                _logValueSanitizer.Safe(safeBroadcasterId),
                                _logValueSanitizer.Safe(category.GameId),
                                _logValueSanitizer.Safe(category.GameName));

                            await gameLibraryRepository.UpsertAsync(new Core.Entities.GameLibraryItem
                            {
                                // Repository is global; UserId is ignored for partitioning.
                                UserId = "global",
                                GameId = safeGameId,
                                GameName = safeGameName,
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
                                        _logValueSanitizer.Safe(safeBroadcasterId),
                                        _logValueSanitizer.Safe(category.GameId),
                                        _logValueSanitizer.JoinSafe(fallback));

                                await twitchApiService.UpdateChannelInformationAsync(
                                    safeBroadcasterId!,
                                    safeGameId!,
                                    fallback);
                            }
                            else
                            {
                                Logger.LogInformation(
                                    "ℹ️ Game auto-added but has no admin CCL config and no user default CCL fallback; skipping CCL apply. game_id={GameId}",
                                        _logValueSanitizer.Safe(category.GameId));
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
                                            _logValueSanitizer.Safe(safeBroadcasterId),
                                            _logValueSanitizer.Safe(category.GameId),
                                            _logValueSanitizer.JoinSafe(fallback));

                                    await twitchApiService.UpdateChannelInformationAsync(
                                        safeBroadcasterId!,
                                        safeGameId!,
                                        fallback);
                                }
                                else
                                {
                                    Logger.LogInformation(
                                        "ℹ️ Admin CCL config not set for this game and no user default CCL fallback; skipping CCL apply. user_id={UserId} game_id={GameId}",
                                            _logValueSanitizer.Safe(safeBroadcasterId),
                                            _logValueSanitizer.Safe(category.GameId));
                                }
                            }
                            else
                            {
                                var enabledCcls = libraryItem.EnabledContentClassificationLabels;
                                Logger.LogInformation(
                                    "🏷️ Applying admin-configured CCLs on stream online. user_id={UserId} game_id={GameId} enabled_ccls={Ccls}",
                                        _logValueSanitizer.Safe(safeBroadcasterId),
                                        _logValueSanitizer.Safe(category.GameId),
                                        _logValueSanitizer.JoinSafe(enabledCcls));

                                await twitchApiService.UpdateChannelInformationAsync(
                                    safeBroadcasterId!,
                                    safeGameId!,
                                    enabledCcls);
                            }
                        }
                    }
                    else
                    {
                        Logger.LogInformation(
                            "🎮 Stream online: no channel category returned; skipping CCL apply. user_id={UserId}",
                            _logValueSanitizer.Safe(safeBroadcasterId));
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "⚠️ Failed applying CCLs on stream online for user {UserId}", _logValueSanitizer.Safe(safeBroadcasterId));
                }
            }

            // Fetch Stream Info
            object notificationData = await GetStreamNotificationDataAsync(user, helixWrapper, safeBroadcasterId!);

            // Send Discord Notification (deduped per stream instance)
            try
            {
                var streamInstanceId = (startedAt ?? counters.StreamStarted ?? now).ToUniversalTime().UtcDateTime.ToString("O");

                var claimed = false;
                try
                {
                    claimed = await counterRepository.TryClaimStreamStartDiscordNotificationAsync(safeBroadcasterId!, streamInstanceId);
                }
                catch (Exception ex)
                {
                    // If we can't safely dedupe, prefer suppressing to avoid spam.
                    Logger.LogWarning(ex, "⚠️ Failed to claim stream_start Discord notification; suppressing send. user_id={UserId}", _logValueSanitizer.Safe(safeBroadcasterId));
                    claimed = false;
                }

                if (!claimed)
                {
                    Logger.LogInformation(
                        "🔁 Suppressed duplicate stream_start Discord announcement. user_id={UserId} stream_instance_id={StreamInstanceId}",
                        _logValueSanitizer.Safe(safeBroadcasterId),
                        _logValueSanitizer.Safe(streamInstanceId));
                }
                else
                {
                    await discordService.SendNotificationAsync(user, "stream_start", notificationData);
                    _discordTracker.RecordNotification(safeBroadcasterId!, true);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to send Discord notification for user {UserId}", broadcasterId);
                _discordTracker.RecordNotification(safeBroadcasterId!, false);
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
                    // Prioritize bot credentials (has all permissions as the monitor system),
                    // then fall back to app token, and finally user token.
                    string? accessToken = null;
                    string tokenSource = "none";

                    // 1. Try bot credentials first (best option - has full permissions)
                    try
                    {
                        var botCredentials = await _botCredentialRepository.GetAsync();
                        if (botCredentials != null && !string.IsNullOrEmpty(botCredentials.AccessToken))
                        {
                            // Check if bot token is expired and needs refresh (5-minute buffer)
                            if (botCredentials.TokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
                            {
                                accessToken = botCredentials.AccessToken;
                                tokenSource = "bot";
                            }
                            else if (!string.IsNullOrEmpty(botCredentials.RefreshToken))
                            {
                                // Attempt to refresh the bot token
                                Logger.LogInformation("Bot token expired for Discord notification fetch; attempting refresh");
                                try
                                {
                                    var refreshedToken = await _twitchAuthService.RefreshTokenAsync(botCredentials.RefreshToken);
                                    if (refreshedToken != null && !string.IsNullOrEmpty(refreshedToken.AccessToken))
                                    {
                                        // Update and persist the refreshed credentials
                                        botCredentials.AccessToken = refreshedToken.AccessToken;
                                        botCredentials.RefreshToken = refreshedToken.RefreshToken;
                                        botCredentials.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn);
                                        await _botCredentialRepository.SaveAsync(botCredentials);

                                        accessToken = botCredentials.AccessToken;
                                        tokenSource = "bot (refreshed)";
                                        Logger.LogInformation("✅ Bot token refreshed successfully for Discord notification fetch");
                                    }
                                    else
                                    {
                                        Logger.LogWarning("Bot token refresh returned null; falling back to alternatives");
                                    }
                                }
                                catch (Exception refreshEx)
                                {
                                    Logger.LogWarning(refreshEx, "Failed to refresh bot token for Discord notification fetch; falling back to alternatives");
                                }
                            }
                            else
                            {
                                Logger.LogInformation("Bot token expired and no refresh token available; falling back to alternatives");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to get bot credentials for Discord notification fetch");
                    }

                    // 2. Fall back to app token (limited but works for public stream data)
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        accessToken = await _twitchAuthService.GetAppAccessTokenAsync();
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            tokenSource = "app";
                        }
                    }

                    // 3. Last resort: user's own token
                    if (string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(user.AccessToken))
                    {
                        accessToken = user.AccessToken;
                        tokenSource = "user";
                    }

                    if (string.IsNullOrEmpty(accessToken))
                    {
                        Logger.LogWarning("No valid access token available for Discord notification fetch for user {UserId}", _logValueSanitizer.Safe(userId));
                        return new { };
                    }

                    Logger.LogInformation("Fetching stream info for Discord notification using {TokenSource} token for user {UserId}", tokenSource, _logValueSanitizer.Safe(userId));

                    var streams = await helixWrapper.GetStreamsAsync(
                        _twitchSettings.ClientId,
                        accessToken,
                        new List<string> { userId });

                    if (streams.Streams != null && streams.Streams.Length > 0)
                    {
                        var stream = streams.Streams[0];
                        Logger.LogInformation(
                            "✅ Retrieved stream data for Discord notification: Title='{Title}', Game='{Game}', Viewers={Viewers}",
                            _logValueSanitizer.Safe(stream.Title ?? ""),
                            _logValueSanitizer.Safe(stream.GameName ?? ""),
                            stream.ViewerCount);

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
                    Logger.LogInformation("Stream not yet available via GetStreams; falling back to channel info for user {UserId}", _logValueSanitizer.Safe(userId));

                    var channelInfo = await helixWrapper.GetChannelInformationAsync(
                        _twitchSettings.ClientId,
                        accessToken,
                        userId);

                    if (channelInfo.Data != null && channelInfo.Data.Length > 0)
                    {
                        var info = channelInfo.Data[0];
                        Logger.LogInformation(
                            "✅ Retrieved channel data for Discord notification: Title='{Title}', Game='{Game}'",
                            _logValueSanitizer.Safe(info.Title ?? ""),
                            _logValueSanitizer.Safe(info.GameName ?? ""));

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

                    Logger.LogWarning("No stream or channel data returned for user {UserId}", _logValueSanitizer.Safe(userId));
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
