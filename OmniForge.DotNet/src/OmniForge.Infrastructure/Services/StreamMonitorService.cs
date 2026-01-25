using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Models.EventSub;
using OmniForge.Infrastructure.Services.EventHandlers;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;

namespace OmniForge.Infrastructure.Services
{
    public class StreamMonitorService : IHostedService, IStreamMonitorService
    {
        private readonly INativeEventSubService _eventSubService;
        private readonly TwitchAPI _twitchApi;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StreamMonitorService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TwitchSettings _twitchSettings;
        private readonly IDiscordNotificationTracker _discordTracker;
        private Timer? _connectionWatchdog;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _subscribedUsers = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _usersWantingMonitoring = new(); // Track users who want monitoring (survives reconnects)
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _monitoringCancellation = new(); // Cancel in-flight subscription/reconnect work per user
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MonitorDiagnostics> _diagnostics = new();

        // Track which broadcasters are currently considered live based on EventSub online/offline.
        // Used to emit periodic live heartbeats so overlay.html doesn't auto-hide.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _liveBroadcasters = new();

        // Prevent overlapping watchdog ticks (Timer can re-enter if work exceeds the period).
        private int _watchdogTickRunning = 0;

        private static readonly TimeSpan DiscordPresenceTimeout = TimeSpan.FromSeconds(2);

        public StreamMonitorService(
            INativeEventSubService eventSubService,
            TwitchAPI twitchApi,
            IHttpClientFactory httpClientFactory,
            ILogger<StreamMonitorService> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<TwitchSettings> twitchSettings,
            IDiscordNotificationTracker discordTracker)
        {
            _eventSubService = eventSubService;
            _twitchApi = twitchApi;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _twitchSettings = twitchSettings.Value;
            _discordTracker = discordTracker;

            _eventSubService.OnSessionWelcome += OnSessionWelcome;
            _eventSubService.OnNotification += OnNotification;
            _eventSubService.OnDisconnected += OnDisconnected;
        }

        public Task<SubscriptionResult> SubscribeToUserAsync(string userId)
            => SubscribeToUserInternalAsync(userId, null);

        public Task<SubscriptionResult> SubscribeToUserAsAsync(string userId, string actingUserId)
            => SubscribeToUserInternalAsync(userId, actingUserId);

        protected sealed record TokenValidation(string UserId, string Login, string ClientId, List<string>? Scopes);

        protected virtual async Task<TokenValidation?> ValidateAccessTokenAsync(string accessToken)
        {
            _twitchApi.Settings.ClientId = _twitchSettings.ClientId;
            _twitchApi.Settings.AccessToken = accessToken;

            var validation = await _twitchApi.Auth.ValidateAccessTokenAsync().ConfigureAwait(false);
            if (validation == null)
            {
                return null;
            }

            return new TokenValidation(
                validation.UserId,
                validation.Login,
                validation.ClientId,
                validation.Scopes);
        }

        private async Task<SubscriptionResult> SubscribeToUserInternalAsync(string userId, string? actingUserId)
        {
            var isAdminActing = !string.IsNullOrEmpty(actingUserId) && actingUserId != userId;
            _logger.LogInformation("📡 Subscribe request: targetUser={TargetUserId}, actingAdmin={ActingAdmin}, isAdminActing={IsAdmin}",
                LogSanitizer.Sanitize(userId), string.IsNullOrEmpty(actingUserId) ? "self" : LogSanitizer.Sanitize(actingUserId!), isAdminActing);

            // Mark intent up-front so Stop Monitoring can take effect immediately even if we are mid-connect/re-subscribe.
            _usersWantingMonitoring.TryAdd(userId, true);
            var token = GetOrCreateMonitoringCancellationToken(userId);
            if (token.IsCancellationRequested)
            {
                return SubscriptionResult.Failed;
            }

            // Ensure connected and we have a valid session ID
            if (!_eventSubService.IsConnected || string.IsNullOrEmpty(_eventSubService.SessionId))
            {
                try
                {
                    var tcs = new TaskCompletionSource<string>();

                    // Local handler to capture the session ID
                    Func<string, Task>? welcomeHandler = null;
                    welcomeHandler = (sessionId) =>
                    {
                        tcs.TrySetResult(sessionId);
                        return Task.CompletedTask;
                    };

                    _eventSubService.OnSessionWelcome += welcomeHandler;

                    // Connect if not already connected
                    await _eventSubService.ConnectAsync().ConfigureAwait(false);

                    if (token.IsCancellationRequested)
                    {
                        _logger.LogInformation("🛑 Subscription cancelled after connect: user {UserId}", LogSanitizer.Sanitize(userId));
                        _eventSubService.OnSessionWelcome -= welcomeHandler;
                        return SubscriptionResult.Failed;
                    }

                    if (!UserStillWantsMonitoring(userId))
                    {
                        _logger.LogInformation("🛑 Subscription aborted after connect: user {UserId} no longer wants monitoring", LogSanitizer.Sanitize(userId));
                        _eventSubService.OnSessionWelcome -= welcomeHandler;
                        return SubscriptionResult.Failed;
                    }

                    // Wait for the welcome message with a timeout
                    Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), token);
                    Task completedTask;
                    try
                    {
                        completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("🛑 Subscription cancelled while waiting for EventSub welcome: user {UserId}", LogSanitizer.Sanitize(userId));
                        _eventSubService.OnSessionWelcome -= welcomeHandler;
                        return SubscriptionResult.Failed;
                    }

                    _eventSubService.OnSessionWelcome -= welcomeHandler;

                    if (completedTask == timeoutTask)
                    {
                        if (token.IsCancellationRequested)
                        {
                            _logger.LogInformation("🛑 Subscription cancelled (welcome wait): user {UserId}", LogSanitizer.Sanitize(userId));
                            return SubscriptionResult.Failed;
                        }
                        _logger.LogError("Timed out waiting for EventSub Session Welcome message.");
                        return SubscriptionResult.Failed;
                    }

                    StartWatchdog();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect to EventSub service.");
                    return SubscriptionResult.Failed;
                }
            }
            else
            {
                StartWatchdog();
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
                var authService = scope.ServiceProvider.GetRequiredService<ITwitchAuthService>();
                var botCredentialRepository = scope.ServiceProvider.GetService<IBotCredentialRepository>();
                var botEligibilityService = scope.ServiceProvider.GetService<ITwitchBotEligibilityService>();
                var monitoringRegistry = scope.ServiceProvider.GetService<IMonitoringRegistry>();
                var twitchApiService = scope.ServiceProvider.GetService<ITwitchApiService>();
                var gameSwitchService = scope.ServiceProvider.GetService<IGameSwitchService>();
                var discordBotClient = scope.ServiceProvider.GetService<IDiscordBotClient>();
                var discordBotSettings = scope.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<OmniForge.Infrastructure.Configuration.DiscordBotSettings>>()?.Value;
                var user = await userRepository.GetUserAsync(userId).ConfigureAwait(false);
                User? actingUser = null;
                // isAdminActing computed above
                if (isAdminActing)
                {
                    actingUser = await userRepository.GetUserAsync(actingUserId!).ConfigureAwait(false);
                    if (actingUser == null || actingUser.Role != "admin")
                    {
                        _logger.LogWarning("Admin monitoring request denied. Acting user {ActingUserId} is not admin or not found.", LogSanitizer.Sanitize(actingUserId!));
                        return SubscriptionResult.Unauthorized;
                    }
                }

                if (user == null)
                {
                    _logger.LogWarning("Cannot subscribe user {UserId}: User not found in database.", LogSanitizer.Sanitize(userId));
                    return SubscriptionResult.Failed;
                }

                User tokenOwner = isAdminActing ? actingUser! : user;

                // Check for token expiry and refresh if needed
                if (tokenOwner.TokenExpiry.AddMinutes(-5) < DateTimeOffset.UtcNow)
                {
                    _logger.LogInformation("Access token for user {UserId} is expired or expiring soon. Refreshing...", LogSanitizer.Sanitize(tokenOwner.TwitchUserId));

                    if (!string.IsNullOrEmpty(tokenOwner.RefreshToken))
                    {
                        var newToken = await authService.RefreshTokenAsync(tokenOwner.RefreshToken).ConfigureAwait(false);
                        if (newToken != null)
                        {
                            tokenOwner.AccessToken = newToken.AccessToken;
                            tokenOwner.RefreshToken = newToken.RefreshToken; // Refresh token might rotate
                            tokenOwner.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);

                            await userRepository.SaveUserAsync(tokenOwner).ConfigureAwait(false);
                            _logger.LogInformation("Successfully refreshed access token for user {UserId}.", LogSanitizer.Sanitize(tokenOwner.TwitchUserId));
                        }
                        else
                        {
                            _logger.LogWarning("Failed to refresh access token for user {UserId}.", LogSanitizer.Sanitize(userId));
                            return SubscriptionResult.Unauthorized;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Cannot refresh token for user {UserId}: No refresh token available.", LogSanitizer.Sanitize(userId));
                        return SubscriptionResult.Unauthorized;
                    }
                }

                if (string.IsNullOrEmpty(tokenOwner.AccessToken))
                {
                    _logger.LogWarning("Cannot subscribe user {UserId}: Access token is missing.", LogSanitizer.Sanitize(tokenOwner.TwitchUserId));
                    return SubscriptionResult.Unauthorized;
                }

                try
                {
                    if (!UserStillWantsMonitoring(userId))
                    {
                        _logger.LogInformation("🛑 Subscription aborted: user {UserId} no longer wants monitoring", LogSanitizer.Sanitize(userId));
                        return SubscriptionResult.Failed;
                    }

                    // Validate the token and get the authoritative User ID
                    string tokenUserId;
                    List<string>? tokenScopes = null;
                    try
                    {
                        var validation = await ValidateAccessTokenAsync(tokenOwner.AccessToken).ConfigureAwait(false);
                        if (validation == null || string.IsNullOrEmpty(validation.UserId))
                        {
                            _logger.LogError("Token validation returned null or empty User ID for user {UserId}", LogSanitizer.Sanitize(userId));
                            return SubscriptionResult.Unauthorized;
                        }
                        tokenUserId = validation.UserId;
                        tokenScopes = validation.Scopes;
                        _logger.LogInformation("Token validated. User ID: {TokenUserId}, Login: {Login}, Client ID: {ClientId}, Scopes: {Scopes}",
                            LogSanitizer.Sanitize(validation.UserId), LogSanitizer.Sanitize(validation.Login), LogSanitizer.Sanitize(validation.ClientId), LogSanitizer.Sanitize(string.Join(", ", tokenScopes ?? new List<string>())));

                        if (!string.IsNullOrWhiteSpace(validation.ClientId)
                            && !string.IsNullOrWhiteSpace(_twitchSettings.ClientId)
                            && !string.Equals(validation.ClientId, _twitchSettings.ClientId, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning(
                                "🔒 Token client_id mismatch for user {UserId}. token_client_id={TokenClientId}, configured_client_id={ConfiguredClientId}. User must re-login using the current Twitch app.",
                                LogSanitizer.Sanitize(userId),
                                LogSanitizer.Sanitize(validation.ClientId),
                                LogSanitizer.Sanitize(_twitchSettings.ClientId));
                            return SubscriptionResult.RequiresReauth;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to validate access token for user {UserId}", LogSanitizer.Sanitize(userId));
                        return SubscriptionResult.Unauthorized;
                    }

                    // Use the token's User ID as the broadcaster ID (they are the same for self-monitoring)
                    var broadcasterId = userId; // target streamer
                    _logger.LogInformation("Using broadcaster_user_id={BroadcasterId}, token_user_id={UserId} for subscriptions (actingAdmin={Acting})", LogSanitizer.Sanitize(broadcasterId), LogSanitizer.Sanitize(tokenUserId), isAdminActing);

                    // We must be able to read the broadcaster's moderators list to decide if Forge bot is eligible.
                    // Twitch enforces this as 'moderation:read'.
                    if (!isAdminActing && (tokenScopes?.Contains("moderation:read") != true))
                    {
                        _logger.LogWarning(
                            "🔒 User {UserId} token is missing required scope for moderators lookup: moderation:read. User must re-login to enable Forge bot moderator eligibility checks.",
                            LogSanitizer.Sanitize(userId));
                        return SubscriptionResult.RequiresReauth;
                    }

                    // Decide whether to use Forge bot for channel-level events (follow/chat) based on moderator eligibility.
                    var useBotForChannelEvents = false;
                    string? botUserId = null;
                    BotCredentials? botCredentials = null;

                    if (!isAdminActing && botEligibilityService != null && botCredentialRepository != null)
                    {
                        var eligibility = await botEligibilityService.GetEligibilityAsync(broadcasterId, tokenOwner.AccessToken, CancellationToken.None).ConfigureAwait(false);
                        if (eligibility.UseBot && !string.IsNullOrEmpty(eligibility.BotUserId))
                        {
                            botCredentials = await EnsureBotTokenValidAsync(botCredentialRepository, authService).ConfigureAwait(false);
                            if (botCredentials != null)
                            {
                                useBotForChannelEvents = true;
                                botUserId = eligibility.BotUserId;
                                _logger.LogInformation("✅ Forge bot eligible. Monitoring will use Forge bot for subscriptions. broadcaster_user_id={BroadcasterId}, bot_user_id={BotUserId}",
                                    LogSanitizer.Sanitize(broadcasterId),
                                    LogSanitizer.Sanitize(botUserId));
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Forge bot eligible but bot credentials are missing/invalid. Falling back to broadcaster token. broadcaster_user_id={BroadcasterId}",
                                    LogSanitizer.Sanitize(broadcasterId));
                            }
                        }
                        else
                        {
                            _logger.LogInformation("ℹ️ Bot not eligible for channel events: {Reason}", LogSanitizer.Sanitize(eligibility.Reason ?? "unknown"));
                        }
                    }

                    if (isAdminActing)
                    {
                        _logger.LogInformation("🧑‍💼 Monitoring start is admin-initiated; Forge bot eligibility is not evaluated. broadcaster_user_id={BroadcasterId}",
                            LogSanitizer.Sanitize(broadcasterId));
                    }
                    else if (!useBotForChannelEvents)
                    {
                        _logger.LogInformation("👤 Monitoring will use broadcaster token (Forge bot not active). broadcaster_user_id={BroadcasterId}, token_user_id={TokenUserId}",
                            LogSanitizer.Sanitize(broadcasterId),
                            LogSanitizer.Sanitize(tokenUserId));
                    }

                    // Check if user has required scopes when we must fall back to streamer token.
                    // If we can use the Forge bot for channel events, the streamer token is only used for stream.online/offline.
                    var requiredScopes = useBotForChannelEvents
                        ? Array.Empty<string>()
                        : new[] { "user:read:chat", "moderator:read:followers" };

                    var missingScopes = requiredScopes.Where(s => tokenScopes?.Contains(s) != true).ToList();
                    if (missingScopes.Count > 0)
                    {
                        _logger.LogWarning("🔒 User {UserId} token is missing required scopes: [{MissingScopes}]. User must re-login to get updated scopes.",
                            LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(string.Join(", ", missingScopes)));
                        return SubscriptionResult.RequiresReauth;
                    }

                    var hasChatScope = useBotForChannelEvents || (tokenScopes?.Contains("user:read:chat") == true);
                    var channelEventsUserId = useBotForChannelEvents ? botUserId! : tokenUserId;

                    var condition = new Dictionary<string, string> { { "broadcaster_user_id", broadcasterId } };
                    var sessionId = _eventSubService.SessionId;

                    var subscribedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (string.IsNullOrEmpty(sessionId))
                    {
                        _logger.LogWarning("Cannot subscribe user {UserId}: Session ID is missing.", LogSanitizer.Sanitize(userId));
                        return SubscriptionResult.Failed;
                    }

                    // Create subscription context to pass state through helper calls
                    var context = new SubscriptionContext
                    {
                        HelixWrapper = helixWrapper,
                        AuthService = authService,
                        UserRepository = userRepository,
                        User = tokenOwner,
                        SessionId = sessionId,
                        CurrentAccessToken = tokenOwner.AccessToken
                    };

                    if (!isAdminActing)
                    {
                        // Subscribe to stream events
                        if (useBotForChannelEvents)
                        {
                            if (await SubscribeWithBotRetryAsync(helixWrapper, authService, botCredentialRepository!, botCredentials!, sessionId, "stream.online", "1", condition, token).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("stream.online");
                            }

                            if (await SubscribeWithBotRetryAsync(helixWrapper, authService, botCredentialRepository!, botCredentials!, sessionId, "stream.offline", "1", condition, token).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("stream.offline");
                            }
                        }
                        else
                        {
                            if (await SubscribeWithRetryAsync(context, "stream.online", "1", condition, token).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("stream.online");
                            }

                            if (await SubscribeWithRetryAsync(context, "stream.offline", "1", condition, token).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("stream.offline");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Admin-initiated monitoring: skipping stream.online/offline subscriptions (requires broadcaster auth)");
                    }

                    // channel.follow v2 requires moderator_user_id as well
                    var followCondition = new Dictionary<string, string>
                    {
                        { "broadcaster_user_id", broadcasterId },
                        { "moderator_user_id", channelEventsUserId }
                    };
                    if (useBotForChannelEvents)
                    {
                        if (await SubscribeWithBotRetryAsync(helixWrapper, authService, botCredentialRepository!, botCredentials!, sessionId, "channel.follow", "2", followCondition, token).ConfigureAwait(false))
                        {
                            subscribedTypes.Add("channel.follow");
                        }
                    }
                    else
                    {
                        if (await SubscribeWithRetryAsync(context, "channel.follow", "2", followCondition, token).ConfigureAwait(false))
                        {
                            subscribedTypes.Add("channel.follow");
                        }
                    }

                    // Chat subscriptions require 'user:read:chat' scope
                    if (hasChatScope)
                    {
                        var chatCondition = new Dictionary<string, string>
                        {
                            { "broadcaster_user_id", broadcasterId },
                            { "user_id", channelEventsUserId }
                        };

                        _logger.LogInformation("Subscribing to chat events with broadcaster_user_id={BroadcasterId}, user_id={UserId} (adminMode={AdminMode})",
                            LogSanitizer.Sanitize(broadcasterId), LogSanitizer.Sanitize(tokenUserId), isAdminActing);

                        // Chat subscriptions don't retry on BadTokenException - user needs to re-login
                        if (useBotForChannelEvents)
                        {
                            if (await SubscribeWithBotRetryAsync(helixWrapper, authService, botCredentialRepository!, botCredentials!, sessionId, "channel.chat.message", "1", chatCondition, token, retryOnBadToken: false).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("channel.chat.message");
                            }

                            if (await SubscribeWithBotRetryAsync(helixWrapper, authService, botCredentialRepository!, botCredentials!, sessionId, "channel.chat.notification", "1", chatCondition, token, retryOnBadToken: false).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("channel.chat.notification");
                            }
                        }
                        else
                        {
                            if (await SubscribeWithRetryAsync(context, "channel.chat.message", "1", chatCondition, token, retryOnBadToken: false).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("channel.chat.message");
                            }

                            if (await SubscribeWithRetryAsync(context, "channel.chat.notification", "1", chatCondition, token, retryOnBadToken: false).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("channel.chat.notification");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("⏭️ Skipping chat EventSub subscriptions (channel.chat.message, channel.chat.notification) - missing 'user:read:chat' scope. Basic stream monitoring will still work.");
                    }

                    // Best-effort: subscribe to channel.update so we can detect game/category changes.
                    // Do not fail monitoring if Twitch rejects this subscription due to missing scopes.
                    try
                    {
                        var channelUpdateCondition = new Dictionary<string, string>
                        {
                            { "broadcaster_user_id", broadcasterId }
                        };

                        if (useBotForChannelEvents)
                        {
                            if (await SubscribeWithBotRetryAsync(helixWrapper, authService, botCredentialRepository!, botCredentials!, sessionId, "channel.update", "2", channelUpdateCondition, token, retryOnBadToken: true).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("channel.update");
                            }
                        }
                        else
                        {
                            if (await SubscribeWithRetryAsync(context, "channel.update", "2", channelUpdateCondition, token, retryOnBadToken: true).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("channel.update");
                            }
                        }

                        _logger.LogInformation("✅ Subscribed to channel.update for broadcaster_user_id={BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to subscribe to channel.update for user {UserId}. Game auto-switch will be disabled.", LogSanitizer.Sanitize(userId));
                    }

                    // Channel Points redemptions (used for jump scares + other reward actions).
                    // Subscribe only when feature is enabled and the broadcaster token has the required scope.
                    if (!isAdminActing && user.Features.ChannelPoints)
                    {
                        if (tokenScopes?.Contains("channel:read:redemptions") == true)
                        {
                            var redemptionCondition = new Dictionary<string, string>
                            {
                                { "broadcaster_user_id", broadcasterId }
                            };

                            if (await SubscribeWithRetryAsync(context, "channel.channel_points_custom_reward_redemption.add", "1", redemptionCondition, token).ConfigureAwait(false))
                            {
                                subscribedTypes.Add("channel.channel_points_custom_reward_redemption.add");
                            }
                        }
                        else
                        {
                            _logger.LogWarning(
                                "🔒 Skipping channel points redemption EventSub subscription (missing scope: channel:read:redemptions). user {UserId} must re-login to enable channel point consumption.",
                                LogSanitizer.Sanitize(userId));
                        }
                    }

                    // User may have pressed Stop during reconnect/resubscribe. Don't re-add them.
                    if (!UserStillWantsMonitoring(userId))
                    {
                        _logger.LogInformation("🛑 Subscription finished but user {UserId} no longer wants monitoring. Skipping activation.", LogSanitizer.Sanitize(userId));
                        var diagCancelled = _diagnostics.GetOrAdd(userId, _ => new MonitorDiagnostics());
                        diagCancelled.IsSubscribed = false;
                        diagCancelled.LastSubscribeAt = DateTimeOffset.UtcNow;
                        diagCancelled.LastSubscribeResult = SubscriptionResult.Failed;
                        diagCancelled.SubscribedTypes = Array.Empty<string>();
                        diagCancelled.LastError = "Cancelled by user";
                        return SubscriptionResult.Failed;
                    }

                    _subscribedUsers.TryAdd(userId, true);
                    _usersWantingMonitoring.TryAdd(userId, true);
                    var diag = _diagnostics.GetOrAdd(userId, _ => new MonitorDiagnostics());
                    diag.IsSubscribed = true;
                    diag.AdminInitiated = isAdminActing;
                    diag.LastSubscribeAt = DateTimeOffset.UtcNow;
                    diag.LastSubscribeResult = SubscriptionResult.Success;
                    diag.SubscribedTypes = subscribedTypes
                        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    diag.LastError = null;

                    monitoringRegistry?.SetState(userId, new MonitoringState(
                        UseBot: useBotForChannelEvents,
                        BotUserId: useBotForChannelEvents ? botUserId : null,
                        UpdatedAtUtc: DateTimeOffset.UtcNow));

                    try
                    {
                        if (discordBotClient != null && discordBotSettings != null && !string.IsNullOrWhiteSpace(discordBotSettings.BotToken))
                        {
                            var onlineTask = discordBotClient.EnsureOnlineAsync(discordBotSettings.BotToken, "shaping commands in the forge");
                            var completed = await Task.WhenAny(onlineTask, Task.Delay(DiscordPresenceTimeout)).ConfigureAwait(false);
                            if (completed != onlineTask)
                            {
                                _logger.LogWarning("⏱️ Discord bot presence update timed out (online) for user {UserId}", LogSanitizer.Sanitize(userId));

                                // Ensure any exception from the still-running task is observed and logged.
                                _ = onlineTask.ContinueWith(
                                    t =>
                                    {
                                        if (t.Exception != null)
                                        {
                                            _logger.LogWarning(t.Exception, "Discord presence update failed after timeout for user {UserId}", LogSanitizer.Sanitize(userId));
                                        }
                                    },
                                    TaskContinuationOptions.OnlyOnFaulted);
                            }
                            else
                            {
                                // Task completed within the timeout; observe any exceptions here.
                                await onlineTask.ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to bring Discord bot online for monitoring start (user {UserId})", LogSanitizer.Sanitize(userId));
                    }

                    // Best-effort: seed the current channel game/category immediately on monitor start.
                    // This allows the overlay/counters to switch before the next channel.update event arrives.
                    if (!isAdminActing && twitchApiService != null && gameSwitchService != null)
                    {
                        try
                        {
                            var category = await twitchApiService.GetChannelCategoryAsync(userId).ConfigureAwait(false);
                            if (category != null && !string.IsNullOrWhiteSpace(category.GameId))
                            {
                                _logger.LogInformation(
                                    "🎮 Seeding active game on monitor start for user {UserId}: {GameName} ({GameId})",
                                    LogSanitizer.Sanitize(userId),
                                    LogSanitizer.Sanitize(category.GameName),
                                    LogSanitizer.Sanitize(category.GameId));

                                await gameSwitchService
                                    .HandleGameDetectedAsync(userId, category.GameId, category.GameName)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                _logger.LogInformation(
                                    "🎮 No active game/category returned for seed on monitor start (user {UserId})",
                                    LogSanitizer.Sanitize(userId));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Failed to seed active game on monitor start for user {UserId}", LogSanitizer.Sanitize(userId));
                        }
                    }

                    // Best-effort: seed live status immediately on monitor start.
                    // Important: Twitch does NOT replay stream.online if the stream was already live before we subscribed.
                    // Without this, _liveBroadcasters may remain empty and the overlay heartbeat loop won't run, causing mid-stream hides.
                    try
                    {
                        if (!isAdminActing && twitchApiService != null)
                        {
                            var streamInfo = await twitchApiService.GetStreamInfoAsync(userId).ConfigureAwait(false);
                            if (streamInfo?.IsLive == true)
                            {
                                _liveBroadcasters[userId] = true;
                                if (scope.ServiceProvider.GetService<IOverlayNotifier>() is IOverlayNotifier overlayNotifier)
                                {
                                    await overlayNotifier.NotifyStreamStatusUpdateAsync(userId, "live").ConfigureAwait(false);
                                }

                                // Restart Discord invite broadcasts when we attach mid-stream after a restart.
                                // This normally happens in StreamOnlineHandler, but Twitch won't replay stream.online.
                                if (scope.ServiceProvider.GetService<OmniForge.Infrastructure.Services.EventHandlers.IDiscordInviteBroadcastScheduler>() is { } inviteScheduler)
                                {
                                    await inviteScheduler.StartAsync(userId).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Best-effort: failed to seed live status on monitor start for user {UserId}", LogSanitizer.Sanitize(userId));
                    }

                    _logger.LogInformation("✅ User {UserId} fully subscribed to all events", LogSanitizer.Sanitize(userId));
                    return SubscriptionResult.Success;
                }
                catch (TwitchLib.Api.Core.Exceptions.BadScopeException)
                {
                    _logger.LogWarning("Failed to subscribe user {UserId}: Bad Scope / Unauthorized.", LogSanitizer.Sanitize(userId));
                    var diag = _diagnostics.GetOrAdd(userId, _ => new MonitorDiagnostics());
                    diag.LastSubscribeAt = DateTimeOffset.UtcNow;
                    diag.LastSubscribeResult = SubscriptionResult.Unauthorized;
                    diag.LastError = "Bad scope / unauthorized";
                    return SubscriptionResult.Unauthorized;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to subscribe user {UserId}", LogSanitizer.Sanitize(userId));
                    var diag = _diagnostics.GetOrAdd(userId, _ => new MonitorDiagnostics());
                    diag.LastSubscribeAt = DateTimeOffset.UtcNow;
                    diag.LastSubscribeResult = SubscriptionResult.Failed;
                    diag.LastError = ex.Message;
                    return SubscriptionResult.Failed;
                }
            }
        }

        public async Task UnsubscribeFromUserAsync(string userId)
        {
            _logger.LogInformation("🛑 Stop Monitoring requested for user {UserId}", LogSanitizer.Sanitize(userId));

            // Cancel any in-flight subscribe/reconnect work immediately.
            // IMPORTANT: do not dispose CTS here (can race with in-flight code registering callbacks on the token).
            if (_monitoringCancellation.TryRemove(userId, out var cts))
            {
                try { cts.Cancel(); } catch { /* ignore */ }
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var monitoringRegistry = scope.ServiceProvider.GetService<IMonitoringRegistry>();
                var inviteScheduler = scope.ServiceProvider.GetService<OmniForge.Infrastructure.Services.EventHandlers.IDiscordInviteBroadcastScheduler>();
                var discordBotClient = scope.ServiceProvider.GetService<IDiscordBotClient>();
                var discordBotSettings = scope.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<OmniForge.Infrastructure.Configuration.DiscordBotSettings>>()?.Value;
                monitoringRegistry?.Remove(userId);

                // If monitoring is stopped, stop periodic invite broadcasts to avoid posting while "offline" in Forge.
                if (inviteScheduler != null)
                {
                    await inviteScheduler.StopAsync(userId).ConfigureAwait(false);
                }

                if (monitoringRegistry != null
                    && discordBotClient != null
                    && discordBotSettings != null
                    && !string.IsNullOrWhiteSpace(discordBotSettings.BotToken)
                    && monitoringRegistry.GetAllStates().Count == 0)
                {
                    // Don't block Stop Monitoring on Discord API latency.
                    var idleTask = discordBotClient.SetIdleAsync(discordBotSettings.BotToken, "shaping commands in the forge");
                    var completed = await Task.WhenAny(idleTask, Task.Delay(DiscordPresenceTimeout)).ConfigureAwait(false);
                    if (completed != idleTask)
                    {
                        _logger.LogWarning("⏱️ Discord bot presence update timed out (idle) for user {UserId}", LogSanitizer.Sanitize(userId));

                        // Ensure any exceptions from the still-running idleTask are observed.
                        _ = idleTask.ContinueWith(
                            t =>
                            {
                                if (t.Exception != null)
                                {
                                    _logger.LogWarning(
                                        t.Exception,
                                        "Discord idle update failed for user {UserId}",
                                        LogSanitizer.Sanitize(userId));
                                }
                            },
                            TaskContinuationOptions.OnlyOnFaulted);
                    }
                    else
                    {
                        // Task completed within the timeout; observe any exceptions here.
                        await idleTask.ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update monitoring registry for user {UserId}", LogSanitizer.Sanitize(userId));
            }

            // Note: Helix doesn't easily support "unsubscribe by user" without tracking subscription IDs.
            // For now, we'll just mark as unsubscribed in our local tracking.
            // In a real implementation, we should store subscription IDs returned by CreateEventSubSubscriptionAsync.
            var wasSubscribed = _subscribedUsers.TryRemove(userId, out _);
            var wasWanting = _usersWantingMonitoring.TryRemove(userId, out _); // User explicitly stopped monitoring
            var diag = _diagnostics.GetOrAdd(userId, _ => new MonitorDiagnostics());
            diag.IsSubscribed = false;
            diag.LastSubscribeResult = null;
            diag.SubscribedTypes = Array.Empty<string>();
            diag.LastError = null;

            _logger.LogInformation("🛑 User {UserId} removed. WasSubscribed: {WasSubscribed}, WasWanting: {WasWanting}. Remaining active: {Count}, wanting: {WantingCount}",
                LogSanitizer.Sanitize(userId), wasSubscribed, wasWanting, _subscribedUsers.Count, _usersWantingMonitoring.Count);

            if (_usersWantingMonitoring.IsEmpty)
            {
                _logger.LogInformation("🔌 No users wanting monitoring. Disconnecting EventSub WebSocket and stopping watchdog.");
                _connectionWatchdog?.Dispose();
                _connectionWatchdog = null;
                // Disconnect if no users are left to save resources and ensure clean state for next connection
                await _eventSubService.DisconnectAsync().ConfigureAwait(false);
                _logger.LogInformation("✅ EventSub disconnected successfully");
            }
        }

        /// <summary>
        /// Forces a token refresh and updates the user in the database.
        /// Returns the updated user, or null if refresh failed.
        /// </summary>
        private async Task<OmniForge.Core.Entities.User?> ForceRefreshTokenAsync(
            OmniForge.Core.Entities.User user,
            ITwitchAuthService authService,
            IUserRepository userRepository)
        {
            if (string.IsNullOrEmpty(user.RefreshToken))
            {
                _logger.LogError("Cannot refresh token for user {UserId}: No refresh token available", LogSanitizer.Sanitize(user.TwitchUserId));
                return null;
            }

            try
            {
                var refreshedToken = await authService.RefreshTokenAsync(user.RefreshToken).ConfigureAwait(false);
                if (refreshedToken != null)
                {
                    user.AccessToken = refreshedToken.AccessToken;
                    user.RefreshToken = refreshedToken.RefreshToken;
                    user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn);
                    await userRepository.SaveUserAsync(user).ConfigureAwait(false);
                    _logger.LogInformation("✅ Token refreshed successfully for user {UserId}", LogSanitizer.Sanitize(user.TwitchUserId));
                    return user;
                }
                else
                {
                    _logger.LogError("❌ Token refresh returned null for user {UserId}", LogSanitizer.Sanitize(user.TwitchUserId));
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during token refresh for user {UserId}", LogSanitizer.Sanitize(user.TwitchUserId));
                return null;
            }
        }

        private async Task<BotCredentials?> EnsureBotTokenValidAsync(
            IBotCredentialRepository botCredentialRepository,
            ITwitchAuthService authService)
        {
            var creds = await botCredentialRepository.GetAsync().ConfigureAwait(false);
            if (creds == null)
            {
                _logger.LogWarning("⚠️ Forge bot credentials not found; falling back to streamer token");
                return null;
            }

            if (string.IsNullOrEmpty(creds.RefreshToken))
            {
                _logger.LogWarning("⚠️ Forge bot refresh token missing; falling back to streamer token");
                return null;
            }

            // Refresh bot token if needed (buffer of 5 minutes)
            if (creds.TokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("🔄 Refreshing Forge bot token for {Username}", LogSanitizer.Sanitize(creds.Username));
                var refreshed = await authService.RefreshTokenAsync(creds.RefreshToken).ConfigureAwait(false);
                if (refreshed == null)
                {
                    _logger.LogError("❌ Failed to refresh Forge bot token for {Username}", LogSanitizer.Sanitize(creds.Username));
                    return null;
                }

                creds.AccessToken = refreshed.AccessToken;
                creds.RefreshToken = refreshed.RefreshToken;
                creds.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
                await botCredentialRepository.SaveAsync(creds).ConfigureAwait(false);
                _logger.LogInformation("✅ Forge bot token refreshed; expires at {Expiry}", creds.TokenExpiry);
            }

            if (string.IsNullOrEmpty(creds.AccessToken))
            {
                _logger.LogWarning("⚠️ Forge bot access token missing; falling back to streamer token");
                return null;
            }

            return creds;
        }

        private async Task<bool> SubscribeWithBotRetryAsync(
            ITwitchHelixWrapper helixWrapper,
            ITwitchAuthService authService,
            IBotCredentialRepository botCredentialRepository,
            BotCredentials botCredentials,
            string sessionId,
            string subscriptionType,
            string version,
            Dictionary<string, string> condition,
            CancellationToken cancellationToken,
            bool retryOnBadToken = true)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                // Pre-validate the bot token so we can detect clientId mismatches and expired/invalid tokens
                // with clear diagnostics (TwitchLib BadRequestException messages can be generic).
                try
                {
                    var validation = await authService.ValidateTokenAsync(botCredentials.AccessToken).ConfigureAwait(false);
                    if (validation == null)
                    {
                        _logger.LogWarning(
                            "⚠️ Bot access token validation failed before subscribing to {SubscriptionType}.",
                            LogSanitizer.Sanitize(subscriptionType));

                        if (retryOnBadToken)
                        {
                            _logger.LogInformation("🔄 Attempting bot token refresh after failed validation...");
                            var refreshed = await authService.RefreshTokenAsync(botCredentials.RefreshToken).ConfigureAwait(false);
                            if (refreshed != null)
                            {
                                botCredentials.AccessToken = refreshed.AccessToken;
                                botCredentials.RefreshToken = refreshed.RefreshToken;
                                botCredentials.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
                                await botCredentialRepository.SaveAsync(botCredentials).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "🔎 Bot token validated. TokenClientId={TokenClientId} TokenUserId={TokenUserId} ScopesCount={ScopesCount} ExpiresIn={ExpiresIn}",
                            LogSanitizer.Sanitize(validation.ClientId),
                            LogSanitizer.Sanitize(validation.UserId),
                            validation.Scopes?.Count ?? 0,
                            validation.ExpiresIn);

                        if (!string.IsNullOrWhiteSpace(_twitchSettings.ClientId)
                            && !string.Equals(validation.ClientId, _twitchSettings.ClientId, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogError(
                                "❌ Bot token clientId mismatch; cannot create EventSub subscriptions with Forge bot. ExpectedClientId={ExpectedClientId} TokenClientId={TokenClientId}. Re-auth the bot with the current Twitch app.",
                                LogSanitizer.Sanitize(_twitchSettings.ClientId),
                                LogSanitizer.Sanitize(validation.ClientId));
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Bot token validation threw before subscribing to {SubscriptionType}", LogSanitizer.Sanitize(subscriptionType));
                }

                await helixWrapper.CreateEventSubSubscriptionAsync(
                    _twitchSettings.ClientId,
                    botCredentials.AccessToken,
                    subscriptionType,
                    version,
                    condition,
                    EventSubTransportMethod.Websocket,
                    sessionId).ConfigureAwait(false);
                _logger.LogInformation("✅ Successfully subscribed to {SubscriptionType} (Forge bot)", LogSanitizer.Sanitize(subscriptionType));
                return true;
            }
            catch (TwitchLib.Api.Core.Exceptions.BadRequestException brEx)
            {
                _logger.LogError(brEx, "❌ BadRequest subscribing to {SubscriptionType} (Forge bot). This commonly indicates ClientId/token mismatch or invalid refresh token.", LogSanitizer.Sanitize(subscriptionType));
                return false;
            }
            catch (TwitchLib.Api.Core.Exceptions.BadTokenException btEx)
            {
                if (!retryOnBadToken)
                {
                    _logger.LogWarning(btEx, "⚠️ BadTokenException for {SubscriptionType} (Forge bot) - bot may need re-auth", LogSanitizer.Sanitize(subscriptionType));
                    return false;
                }

                _logger.LogWarning(btEx, "⚠️ BadTokenException for {SubscriptionType} (Forge bot) - forcing bot token refresh...", LogSanitizer.Sanitize(subscriptionType));
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                var refreshed = await authService.RefreshTokenAsync(botCredentials.RefreshToken).ConfigureAwait(false);
                if (refreshed == null)
                {
                    _logger.LogError("❌ Failed to refresh Forge bot token during subscription retry");
                    return false;
                }

                botCredentials.AccessToken = refreshed.AccessToken;
                botCredentials.RefreshToken = refreshed.RefreshToken;
                botCredentials.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
                await botCredentialRepository.SaveAsync(botCredentials).ConfigureAwait(false);

                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId,
                        botCredentials.AccessToken,
                        subscriptionType,
                        version,
                        condition,
                        EventSubTransportMethod.Websocket,
                        sessionId).ConfigureAwait(false);
                    _logger.LogInformation("✅ Successfully subscribed to {SubscriptionType} after bot token refresh", LogSanitizer.Sanitize(subscriptionType));
                    return true;
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "❌ Failed to subscribe to {SubscriptionType} even after bot token refresh", LogSanitizer.Sanitize(subscriptionType));
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to {SubscriptionType} (Forge bot)", LogSanitizer.Sanitize(subscriptionType));
                return false;
            }
        }

        /// <summary>
        /// Context for EventSub subscription operations, carrying all needed dependencies and state.
        /// </summary>
        private class SubscriptionContext
        {
            public required ITwitchHelixWrapper HelixWrapper { get; init; }
            public required ITwitchAuthService AuthService { get; init; }
            public required IUserRepository UserRepository { get; init; }
            public required OmniForge.Core.Entities.User User { get; set; }
            public required string SessionId { get; init; }
            public string CurrentAccessToken { get; set; } = string.Empty;
        }

        /// <summary>
        /// Subscribes to an EventSub subscription type with automatic token refresh retry on BadTokenException.
        /// </summary>
        /// <param name="context">The subscription context containing dependencies and current state.</param>
        /// <param name="subscriptionType">The EventSub subscription type (e.g., "stream.online").</param>
        /// <param name="version">The subscription version (e.g., "1" or "2").</param>
        /// <param name="condition">The condition dictionary for the subscription.</param>
        /// <param name="retryOnBadToken">Whether to retry with token refresh on BadTokenException.</param>
        /// <returns>True if subscription succeeded, false otherwise.</returns>
        private async Task<bool> SubscribeWithRetryAsync(
            SubscriptionContext context,
            string subscriptionType,
            string version,
            Dictionary<string, string> condition,
            CancellationToken cancellationToken,
            bool retryOnBadToken = true)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                await context.HelixWrapper.CreateEventSubSubscriptionAsync(
                    _twitchSettings.ClientId,
                    context.CurrentAccessToken,
                    subscriptionType,
                    version,
                    condition,
                    EventSubTransportMethod.Websocket,
                    context.SessionId).ConfigureAwait(false);
                _logger.LogInformation("✅ Successfully subscribed to {SubscriptionType}", LogSanitizer.Sanitize(subscriptionType));
                return true;
            }
            catch (TwitchLib.Api.Core.Exceptions.BadTokenException btEx)
            {
                if (!retryOnBadToken)
                {
                    _logger.LogWarning(btEx, "⚠️ BadTokenException for {SubscriptionType} - user may need to re-login", LogSanitizer.Sanitize(subscriptionType));
                    return false;
                }

                _logger.LogWarning(btEx, "⚠️ BadTokenException for {SubscriptionType} - forcing token refresh...", LogSanitizer.Sanitize(subscriptionType));
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                var refreshedUser = await ForceRefreshTokenAsync(context.User, context.AuthService, context.UserRepository).ConfigureAwait(false);
                if (refreshedUser != null)
                {
                    context.User = refreshedUser;
                    context.CurrentAccessToken = refreshedUser.AccessToken;
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return false;
                        }
                        await context.HelixWrapper.CreateEventSubSubscriptionAsync(
                            _twitchSettings.ClientId,
                            context.CurrentAccessToken,
                            subscriptionType,
                            version,
                            condition,
                            EventSubTransportMethod.Websocket,
                            context.SessionId).ConfigureAwait(false);
                        _logger.LogInformation("✅ Successfully subscribed to {SubscriptionType} after token refresh", LogSanitizer.Sanitize(subscriptionType));
                        return true;
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "❌ Failed to subscribe to {SubscriptionType} even after token refresh", LogSanitizer.Sanitize(subscriptionType));
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to {SubscriptionType}", LogSanitizer.Sanitize(subscriptionType));
                return false;
            }
        }

        private CancellationToken GetOrCreateMonitoringCancellationToken(string userId)
        {
            // Avoid disposing an existing CTS here to prevent races with threads that may still be using it.
            while (true)
            {
                if (_monitoringCancellation.TryGetValue(userId, out var existingCts))
                {
                    // If the existing CTS is still active, just reuse it.
                    if (!existingCts.IsCancellationRequested)
                    {
                        return existingCts.Token;
                    }

                    // Existing CTS is canceled; create a new one and try to replace it atomically.
                    var newCts = new CancellationTokenSource();
                    if (_monitoringCancellation.TryUpdate(userId, newCts, existingCts))
                    {
                        // Do not dispose existingCts here to avoid race conditions with concurrent readers.
                        return newCts.Token;
                    }

                    // Another thread updated the entry concurrently; retry.
                    continue;
                }

                // No CTS exists for this user; try to add a new one.
                var addedCts = new CancellationTokenSource();
                if (_monitoringCancellation.TryAdd(userId, addedCts))
                {
                    return addedCts.Token;
                }

                // Another thread added one concurrently; retry to fetch it.
            }
        }

        private bool UserStillWantsMonitoring(string userId)
        {
            if (!_usersWantingMonitoring.ContainsKey(userId))
            {
                return false;
            }

            if (_monitoringCancellation.TryGetValue(userId, out var cts) && cts.IsCancellationRequested)
            {
                return false;
            }

            return true;
        }

        public async Task<SubscriptionResult> ForceReconnectUserAsync(string userId)
        {
            _logger.LogInformation("🔄 Force reconnect requested for user {UserId}", LogSanitizer.Sanitize(userId));
            if (_eventSubService.IsConnected)
            {
                try { await _eventSubService.DisconnectAsync().ConfigureAwait(false); } catch (Exception ex) { _logger.LogWarning(ex, "Force reconnect: disconnect failed but continuing"); }
            }
            try { await _eventSubService.ConnectAsync().ConfigureAwait(false); } catch (Exception ex) { _logger.LogError(ex, "Force reconnect: connect failed"); }
            var result = await SubscribeToUserAsync(userId).ConfigureAwait(false);
            _logger.LogInformation("🔄 Force reconnect result for {UserId}: {Result}", LogSanitizer.Sanitize(userId), result);
            return result;
        }

        public StreamMonitorStatus GetUserConnectionStatus(string userId)
        {
            var discordStatus = _discordTracker.GetLastNotification(userId);
            _diagnostics.TryGetValue(userId, out var diag);
            double? keepAliveAge = null;
            try
            {
                if (_eventSubService.LastKeepaliveTime != default)
                {
                    keepAliveAge = (DateTime.UtcNow - _eventSubService.LastKeepaliveTime).TotalSeconds;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to compute keepalive age");
            }

            return new StreamMonitorStatus
            {
                Connected = _eventSubService.IsConnected,
                Subscriptions = diag?.SubscribedTypes ?? (_subscribedUsers.ContainsKey(userId) ? new[] { "stream.online", "stream.offline" } : Array.Empty<string>()),
                LastConnected = _eventSubService.IsConnected ? DateTimeOffset.UtcNow : null, // Approximate
                LastDiscordNotification = discordStatus?.Time,
                LastDiscordNotificationSuccess = discordStatus?.Success ?? false,
                IsSubscribed = diag?.IsSubscribed ?? _subscribedUsers.ContainsKey(userId),
                EventSubSessionId = _eventSubService.SessionId,
                EventSubKeepaliveTimeoutSeconds = _eventSubService.KeepaliveTimeoutSeconds,
                LastEventType = diag?.LastEventType,
                LastEventAt = diag?.LastEventAt,
                LastEventSummary = diag?.LastEventSummary,
                LastError = diag?.LastError,
                KeepaliveAgeSeconds = keepAliveAge,
                AdminInitiated = diag?.AdminInitiated ?? false
            };
        }

        public bool IsUserSubscribed(string userId)
        {
            return _subscribedUsers.ContainsKey(userId);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting StreamMonitorService...");
            // Do NOT auto-connect. Wait for user action.
            await Task.CompletedTask;
        }

        private void StartWatchdog()
        {
            if (_connectionWatchdog == null)
            {
                _connectionWatchdog = new Timer(CheckConnection, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                _logger.LogInformation("🕒 Watchdog timer started (interval=1m)");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping StreamMonitorService...");
            _connectionWatchdog?.Dispose();
            try
            {
                await _eventSubService.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping StreamMonitorService.");
            }
        }

        private async Task OnSessionWelcome(string sessionId)
        {
            _logger.LogInformation($"EventSub Session Welcome. ID: {LogSanitizer.Sanitize(sessionId)}");

            // Auto-subscription is disabled. Users must manually start monitoring.
            // This prevents unauthorized or unwanted subscriptions on startup.
            await Task.CompletedTask;
        }

        private async Task OnDisconnected()
        {
            _logger.LogWarning("🔴 EventSub Disconnected! Active subscriptions: {Count}, Users wanting monitoring: {WantingCount}",
                _subscribedUsers.Count, _usersWantingMonitoring.Count);

            // Clear active subscriptions (they're invalidated on disconnect)
            // BUT keep _usersWantingMonitoring so we can re-subscribe on reconnect
            _subscribedUsers.Clear();

            await Task.CompletedTask;
        }

        private async void CheckConnection(object? state)
        {
            if (Interlocked.Exchange(ref _watchdogTickRunning, 1) == 1)
            {
                _logger.LogDebug("Watchdog: previous tick still running, skipping");
                return;
            }

            try
            {
            // Only attempt to maintain connection if we have users wanting monitoring
            if (_usersWantingMonitoring.IsEmpty)
            {
                _logger.LogDebug("Watchdog: No users wanting monitoring, skipping check");
                return;
            }

            if (!_eventSubService.IsConnected)
            {
                _logger.LogWarning("🔄 Watchdog detected disconnected state. Users wanting monitoring: {Count}. Attempting to reconnect...",
                    _usersWantingMonitoring.Count);
                try
                {
                    await _eventSubService.ConnectAsync().ConfigureAwait(false);

                    // Wait for session welcome before re-subscribing
                    await Task.Delay(2000).ConfigureAwait(false); // Give time for welcome message

                    if (_eventSubService.IsConnected && !string.IsNullOrEmpty(_eventSubService.SessionId))
                    {
                        _logger.LogInformation("🔄 Reconnected! Re-subscribing {Count} users...", _usersWantingMonitoring.Count);
                        foreach (var userId in _usersWantingMonitoring.Keys)
                        {
                            if (!UserStillWantsMonitoring(userId))
                            {
                                continue;
                            }
                            _logger.LogInformation("🔄 Re-subscribing user {UserId}...", LogSanitizer.Sanitize(userId));
                            var result = await SubscribeToUserAsync(userId).ConfigureAwait(false);
                            _logger.LogInformation("🔄 Re-subscription result for user {UserId}: {Result}", LogSanitizer.Sanitize(userId), result);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("🔄 Connected but no session ID yet. Will retry on next watchdog tick.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🔴 Watchdog reconnection failed.");
                }
            }
            else
            {
                // Check keepalive
                var timeSinceLastKeepalive = DateTime.UtcNow - _eventSubService.LastKeepaliveTime;

                // Twitch tells us the negotiated keepalive timeout in session_welcome.keepalive_timeout_seconds.
                // Use a forgiving threshold (3x) so we don't flap on transient latency.
                var negotiated = _eventSubService.KeepaliveTimeoutSeconds;
                var thresholdSeconds = negotiated.HasValue
                    ? Math.Max(30, negotiated.Value * 3)
                    : 30;

                if (timeSinceLastKeepalive.TotalSeconds > thresholdSeconds)
                {
                    _logger.LogWarning(
                        "⏱️ No keepalive received for {Seconds:F1}s (threshold={Threshold}s, negotiated={Negotiated}s). Triggering reconnect...",
                        timeSinceLastKeepalive.TotalSeconds,
                        thresholdSeconds,
                        negotiated.HasValue ? negotiated.Value : -1);
                    try
                    {
                        await _eventSubService.DisconnectAsync().ConfigureAwait(false);
                        await _eventSubService.ConnectAsync().ConfigureAwait(false);

                        // Wait for session welcome before re-subscribing
                        await Task.Delay(2000).ConfigureAwait(false);

                        if (_eventSubService.IsConnected && !string.IsNullOrEmpty(_eventSubService.SessionId))
                        {
                            _logger.LogInformation("🔄 Reconnected after keepalive timeout! Re-subscribing {Count} users...", _usersWantingMonitoring.Count);
                            foreach (var userId in _usersWantingMonitoring.Keys)
                            {
                                if (!UserStillWantsMonitoring(userId))
                                {
                                    continue;
                                }
                                var result = await SubscribeToUserAsync(userId).ConfigureAwait(false);
                                _logger.LogInformation("🔄 Re-subscription result for user {UserId}: {Result}", LogSanitizer.Sanitize(userId), result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "🔴 Keepalive reconnection failed.");
                    }
                }
            }

            // Overlay live heartbeat: if a broadcaster is live, emit a periodic 'live' status update.
            // overlay.html treats streamStatusUpdate("live") as an online heartbeat and auto-hides after 2 minutes without it.
            if (!_liveBroadcasters.IsEmpty)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                    if (overlayNotifier != null)
                    {
                        foreach (var broadcasterId in _liveBroadcasters.Keys)
                        {
                            // If monitoring was stopped for this user, don't keep sending heartbeats.
                            if (!UserStillWantsMonitoring(broadcasterId))
                            {
                                _liveBroadcasters.TryRemove(broadcasterId, out _);
                                continue;
                            }

                            await overlayNotifier.NotifyStreamStatusUpdateAsync(broadcasterId, "live").ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Watchdog failed to emit overlay live heartbeats");
                }
            }

            }
            finally
            {
                Interlocked.Exchange(ref _watchdogTickRunning, 0);
            }
        }

        private async Task OnNotification(EventSubMessage message)
        {
            try
            {
                var subscriptionType = message.Payload.Subscription?.Type;
                var eventData = message.Payload.Event;

                if (string.IsNullOrEmpty(subscriptionType))
                {
                    _logger.LogWarning("Received notification with no subscription type");
                    return;
                }

                var isChatEvent = subscriptionType.StartsWith("channel.chat", StringComparison.OrdinalIgnoreCase);
                if (isChatEvent)
                {
                    _logger.LogDebug("💬 EventSub notification received: {SubscriptionType}", LogSanitizer.Sanitize(subscriptionType));
                }
                else
                {
                    _logger.LogInformation("📨 EventSub notification received: {SubscriptionType}", LogSanitizer.Sanitize(subscriptionType));
                }

                using var scope = _scopeFactory.CreateScope();
                var handlerRegistry = scope.ServiceProvider.GetRequiredService<IEventSubHandlerRegistry>();
                var handler = handlerRegistry.GetHandler(subscriptionType);
                if (handler != null)
                {
                    if (TryGetBroadcasterId(eventData, out var broadcasterId) && !string.IsNullOrEmpty(broadcasterId))
                    {
                        // Maintain a simple live/offline view for overlay heartbeat support.
                        if (string.Equals(subscriptionType, "stream.online", StringComparison.OrdinalIgnoreCase))
                        {
                            _liveBroadcasters[broadcasterId!] = true;
                        }
                        else if (string.Equals(subscriptionType, "stream.offline", StringComparison.OrdinalIgnoreCase))
                        {
                            _liveBroadcasters.TryRemove(broadcasterId!, out _);
                        }

                        var diag = _diagnostics.GetOrAdd(broadcasterId!, _ => new MonitorDiagnostics());
                        diag.LastEventType = subscriptionType;
                        diag.LastEventAt = DateTimeOffset.UtcNow;
                        diag.LastEventSummary = TrySummarizeEvent(eventData);
                    }
                    await handler.HandleAsync(eventData).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("No handler registered for subscription type: {SubscriptionType}", LogSanitizer.Sanitize(subscriptionType));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification.");
            }
        }

        private static string? TrySummarizeEvent(System.Text.Json.JsonElement eventData)
        {
            try
            {
                // Provide a concise summary (id + optional user/login if present)
                if (eventData.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString();
                    if (eventData.TryGetProperty("user_login", out var loginProp))
                    {
                        return $"id={id}, user={loginProp.GetString()}";
                    }
                    if (eventData.TryGetProperty("broadcaster_user_name", out var bnameProp))
                    {
                        return $"id={id}, broadcaster={bnameProp.GetString()}";
                    }
                    return $"id={id}";
                }
                // fallback: truncate raw JSON
                var raw = eventData.GetRawText();
                return raw.Length > 200 ? raw.Substring(0, 200) + "…" : raw;
            }
            catch
            {
                return null;
            }
        }

        private class MonitorDiagnostics
        {
            public bool IsSubscribed { get; set; }
            public bool AdminInitiated { get; set; }
            public DateTimeOffset? LastSubscribeAt { get; set; }
            public SubscriptionResult? LastSubscribeResult { get; set; }
            public string[] SubscribedTypes { get; set; } = Array.Empty<string>();
            public string? LastEventType { get; set; }
            public DateTimeOffset? LastEventAt { get; set; }
            public string? LastEventSummary { get; set; }
            public string? LastError { get; set; }
        }

        // NOTE: Legacy event handlers (HandleStreamOnline, HandleStreamOffline, HandleFollow, HandleSubscribe,
        // HandleSubscriptionGift, HandleSubscriptionMessage, HandleCheer, HandleRaid, HandleChatMessage,
        // HandleChatNotification, SendDiscordInvite) have been removed and replaced with the Strategy Pattern.
        // Event handling is now delegated to IEventSubHandler implementations in EventHandlers/ folder.
        // See: StreamOnlineHandler, StreamOfflineHandler, FollowHandler, etc.

        private bool TryGetBroadcasterId(System.Text.Json.JsonElement eventData, out string? broadcasterId)
        {
            broadcasterId = null;
            if (eventData.TryGetProperty("broadcaster_user_id", out var idProp))
                broadcasterId = idProp.GetString();
            return broadcasterId != null;
        }
    }
}
