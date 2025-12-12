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
        private readonly IEventSubHandlerRegistry _handlerRegistry;
        private readonly IDiscordNotificationTracker _discordTracker;
        private Timer? _connectionWatchdog;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _subscribedUsers = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _usersWantingMonitoring = new(); // Track users who want monitoring (survives reconnects)

        public StreamMonitorService(
            INativeEventSubService eventSubService,
            TwitchAPI twitchApi,
            IHttpClientFactory httpClientFactory,
            ILogger<StreamMonitorService> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<TwitchSettings> twitchSettings,
            IEventSubHandlerRegistry handlerRegistry,
            IDiscordNotificationTracker discordTracker)
        {
            _eventSubService = eventSubService;
            _twitchApi = twitchApi;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _twitchSettings = twitchSettings.Value;
            _handlerRegistry = handlerRegistry;
            _discordTracker = discordTracker;

            _eventSubService.OnSessionWelcome += OnSessionWelcome;
            _eventSubService.OnNotification += OnNotification;
            _eventSubService.OnDisconnected += OnDisconnected;
        }

        public Task<SubscriptionResult> SubscribeToUserAsync(string userId)
            => SubscribeToUserInternalAsync(userId, null);

        public Task<SubscriptionResult> SubscribeToUserAsAsync(string userId, string actingUserId)
            => SubscribeToUserInternalAsync(userId, actingUserId);

        private async Task<SubscriptionResult> SubscribeToUserInternalAsync(string userId, string? actingUserId)
        {
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
                    await _eventSubService.ConnectAsync();

                    // Wait for the welcome message with a timeout
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                    _eventSubService.OnSessionWelcome -= welcomeHandler;

                    if (completedTask == timeoutTask)
                    {
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
                var user = await userRepository.GetUserAsync(userId);
                User? actingUser = null;
                var isAdminActing = !string.IsNullOrEmpty(actingUserId) && actingUserId != userId;
                if (isAdminActing)
                {
                    actingUser = await userRepository.GetUserAsync(actingUserId!);
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
                        var newToken = await authService.RefreshTokenAsync(tokenOwner.RefreshToken);
                        if (newToken != null)
                        {
                            tokenOwner.AccessToken = newToken.AccessToken;
                            tokenOwner.RefreshToken = newToken.RefreshToken; // Refresh token might rotate
                            tokenOwner.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);

                            await userRepository.SaveUserAsync(tokenOwner);
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
                    // Validate the token and get the authoritative User ID
                    string tokenUserId;
                    List<string>? tokenScopes = null;
                    try
                    {
                        _twitchApi.Settings.ClientId = _twitchSettings.ClientId;
                        _twitchApi.Settings.AccessToken = tokenOwner.AccessToken;
                        var validation = await _twitchApi.Auth.ValidateAccessTokenAsync();
                        if (validation == null || string.IsNullOrEmpty(validation.UserId))
                        {
                            _logger.LogError("Token validation returned null or empty User ID for user {UserId}", LogSanitizer.Sanitize(userId));
                            return SubscriptionResult.Unauthorized;
                        }
                        tokenUserId = validation.UserId;
                        tokenScopes = validation.Scopes;
                        _logger.LogInformation("Token validated. User ID: {TokenUserId}, Login: {Login}, Client ID: {ClientId}, Scopes: {Scopes}",
                            LogSanitizer.Sanitize(validation.UserId), LogSanitizer.Sanitize(validation.Login), LogSanitizer.Sanitize(validation.ClientId), LogSanitizer.Sanitize(string.Join(", ", tokenScopes ?? new List<string>())));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to validate access token for user {UserId}", LogSanitizer.Sanitize(userId));
                        return SubscriptionResult.Unauthorized;
                    }

                    // Check if user has required scopes for full EventSub functionality
                    // Required scopes for core functionality:
                    // - user:read:chat - EventSub chat messages
                    // - user:write:chat - Send chat messages via API
                    // - user:bot - Required for EventSub chat subscriptions
                    // - moderator:read:followers - channel.follow events
                    // - channel:read:subscriptions - subscription events
                    // - bits:read - cheer events
                    // - channel:read:redemptions - channel point events
                    var requiredScopes = new[] { "user:read:chat", "moderator:read:followers" };
                    var missingScopes = requiredScopes.Where(s => tokenScopes?.Contains(s) != true).ToList();

                    if (missingScopes.Count > 0)
                    {
                        _logger.LogWarning("🔒 User {UserId} token is missing required scopes: [{MissingScopes}]. User must re-login to get updated scopes.",
                            LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(string.Join(", ", missingScopes)));
                        return SubscriptionResult.RequiresReauth;
                    }

                    var hasChatScope = tokenScopes?.Contains("user:read:chat") == true;

                    // Use the token's User ID as the broadcaster ID (they are the same for self-monitoring)
                    var broadcasterId = userId; // target streamer
                    _logger.LogInformation("Using broadcaster_user_id={BroadcasterId}, token_user_id={UserId} for subscriptions (actingAdmin={Acting})", LogSanitizer.Sanitize(broadcasterId), LogSanitizer.Sanitize(tokenUserId), isAdminActing);

                    var condition = new Dictionary<string, string> { { "broadcaster_user_id", broadcasterId } };
                    var sessionId = _eventSubService.SessionId;

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
                        // Subscribe to stream events (requires broadcaster auth)
                        await SubscribeWithRetryAsync(context, "stream.online", "1", condition);
                        await SubscribeWithRetryAsync(context, "stream.offline", "1", condition);
                    }
                    else
                    {
                        _logger.LogInformation("Admin-initiated monitoring: skipping stream.online/offline subscriptions (requires broadcaster auth)");
                    }

                    // channel.follow v2 requires moderator_user_id as well
                    var followCondition = new Dictionary<string, string>
                    {
                        { "broadcaster_user_id", broadcasterId },
                        { "moderator_user_id", tokenUserId }
                    };
                    await SubscribeWithRetryAsync(context, "channel.follow", "2", followCondition);

                    // Chat subscriptions require 'user:read:chat' scope
                    if (hasChatScope)
                    {
                        var chatCondition = new Dictionary<string, string>
                        {
                            { "broadcaster_user_id", broadcasterId },
                            { "user_id", tokenUserId }
                        };

                        _logger.LogInformation("Subscribing to chat events with broadcaster_user_id={BroadcasterId}, user_id={UserId} (adminMode={AdminMode})",
                            LogSanitizer.Sanitize(broadcasterId), LogSanitizer.Sanitize(tokenUserId), isAdminActing);

                        // Chat subscriptions don't retry on BadTokenException - user needs to re-login
                        await SubscribeWithRetryAsync(context, "channel.chat.message", "1", chatCondition, retryOnBadToken: false);
                        await SubscribeWithRetryAsync(context, "channel.chat.notification", "1", chatCondition, retryOnBadToken: false);
                    }
                    else
                    {
                        _logger.LogInformation("⏭️ Skipping chat EventSub subscriptions (channel.chat.message, channel.chat.notification) - missing 'user:read:chat' scope. Basic stream monitoring will still work.");
                    }

                    _subscribedUsers.TryAdd(userId, true);
                    _usersWantingMonitoring.TryAdd(userId, true);
                    _logger.LogInformation("✅ User {UserId} fully subscribed to all events", LogSanitizer.Sanitize(userId));
                    return SubscriptionResult.Success;
                }
                catch (TwitchLib.Api.Core.Exceptions.BadScopeException)
                {
                    _logger.LogWarning("Failed to subscribe user {UserId}: Bad Scope / Unauthorized.", LogSanitizer.Sanitize(userId));
                    return SubscriptionResult.Unauthorized;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to subscribe user {UserId}", LogSanitizer.Sanitize(userId));
                    return SubscriptionResult.Failed;
                }
            }
        }

        public async Task UnsubscribeFromUserAsync(string userId)
        {
            _logger.LogInformation("🛑 Stop Monitoring requested for user {UserId}", LogSanitizer.Sanitize(userId));

            // Note: Helix doesn't easily support "unsubscribe by user" without tracking subscription IDs.
            // For now, we'll just mark as unsubscribed in our local tracking.
            // In a real implementation, we should store subscription IDs returned by CreateEventSubSubscriptionAsync.
            var wasSubscribed = _subscribedUsers.TryRemove(userId, out _);
            var wasWanting = _usersWantingMonitoring.TryRemove(userId, out _); // User explicitly stopped monitoring

            _logger.LogInformation("🛑 User {UserId} removed. WasSubscribed: {WasSubscribed}, WasWanting: {WasWanting}. Remaining active: {Count}, wanting: {WantingCount}",
                LogSanitizer.Sanitize(userId), wasSubscribed, wasWanting, _subscribedUsers.Count, _usersWantingMonitoring.Count);

            if (_usersWantingMonitoring.IsEmpty)
            {
                _logger.LogInformation("🔌 No users wanting monitoring. Disconnecting EventSub WebSocket and stopping watchdog.");
                _connectionWatchdog?.Dispose();
                _connectionWatchdog = null;
                // Disconnect if no users are left to save resources and ensure clean state for next connection
                await _eventSubService.DisconnectAsync();
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
                var refreshedToken = await authService.RefreshTokenAsync(user.RefreshToken);
                if (refreshedToken != null)
                {
                    user.AccessToken = refreshedToken.AccessToken;
                    user.RefreshToken = refreshedToken.RefreshToken;
                    user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn);
                    await userRepository.SaveUserAsync(user);
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
            bool retryOnBadToken = true)
        {
            try
            {
                await context.HelixWrapper.CreateEventSubSubscriptionAsync(
                    _twitchSettings.ClientId,
                    context.CurrentAccessToken,
                    subscriptionType,
                    version,
                    condition,
                    EventSubTransportMethod.Websocket,
                    context.SessionId);
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
                var refreshedUser = await ForceRefreshTokenAsync(context.User, context.AuthService, context.UserRepository);
                if (refreshedUser != null)
                {
                    context.User = refreshedUser;
                    context.CurrentAccessToken = refreshedUser.AccessToken;
                    try
                    {
                        await context.HelixWrapper.CreateEventSubSubscriptionAsync(
                            _twitchSettings.ClientId,
                            context.CurrentAccessToken,
                            subscriptionType,
                            version,
                            condition,
                            EventSubTransportMethod.Websocket,
                            context.SessionId);
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

        public async Task<SubscriptionResult> ForceReconnectUserAsync(string userId)
        {
            // Re-subscribe
            return await SubscribeToUserAsync(userId);
        }

        public StreamMonitorStatus GetUserConnectionStatus(string userId)
        {
            var discordStatus = _discordTracker.GetLastNotification(userId);

            return new StreamMonitorStatus
            {
                Connected = _eventSubService.IsConnected,
                Subscriptions = _subscribedUsers.ContainsKey(userId) ? new[] { "stream.online", "stream.offline" } : Array.Empty<string>(),
                LastConnected = _eventSubService.IsConnected ? DateTimeOffset.UtcNow : null, // Approximate
                LastDiscordNotification = discordStatus?.Time,
                LastDiscordNotificationSuccess = discordStatus?.Success ?? false
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
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping StreamMonitorService...");
            _connectionWatchdog?.Dispose();
            try
            {
                await _eventSubService.DisconnectAsync();
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
                    await _eventSubService.ConnectAsync();

                    // Wait for session welcome before re-subscribing
                    await Task.Delay(2000); // Give time for welcome message

                    if (_eventSubService.IsConnected && !string.IsNullOrEmpty(_eventSubService.SessionId))
                    {
                        _logger.LogInformation("🔄 Reconnected! Re-subscribing {Count} users...", _usersWantingMonitoring.Count);
                        foreach (var userId in _usersWantingMonitoring.Keys)
                        {
                            _logger.LogInformation("🔄 Re-subscribing user {UserId}...", LogSanitizer.Sanitize(userId));
                            var result = await SubscribeToUserAsync(userId);
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
                if (timeSinceLastKeepalive.TotalSeconds > 30) // Assuming default keepalive is ~10s
                {
                    _logger.LogWarning("⏱️ No keepalive received for {Seconds:F1}s. Triggering reconnect...", timeSinceLastKeepalive.TotalSeconds);
                    try
                    {
                        await _eventSubService.DisconnectAsync();
                        await _eventSubService.ConnectAsync();

                        // Wait for session welcome before re-subscribing
                        await Task.Delay(2000);

                        if (_eventSubService.IsConnected && !string.IsNullOrEmpty(_eventSubService.SessionId))
                        {
                            _logger.LogInformation("🔄 Reconnected after keepalive timeout! Re-subscribing {Count} users...", _usersWantingMonitoring.Count);
                            foreach (var userId in _usersWantingMonitoring.Keys)
                            {
                                var result = await SubscribeToUserAsync(userId);
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

                var handler = _handlerRegistry.GetHandler(subscriptionType);
                if (handler != null)
                {
                    await handler.HandleAsync(eventData);
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
