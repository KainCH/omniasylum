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
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Models.EventSub;
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
        private Timer? _connectionWatchdog;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _subscribedUsers = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _usersWantingMonitoring = new(); // Track users who want monitoring (survives reconnects)
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset Time, bool Success)> _lastDiscordNotifications = new();

        public StreamMonitorService(
            INativeEventSubService eventSubService,
            TwitchAPI twitchApi,
            IHttpClientFactory httpClientFactory,
            ILogger<StreamMonitorService> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<TwitchSettings> twitchSettings)
        {
            _eventSubService = eventSubService;
            _twitchApi = twitchApi;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _twitchSettings = twitchSettings.Value;

            _eventSubService.OnSessionWelcome += OnSessionWelcome;
            _eventSubService.OnNotification += OnNotification;
            _eventSubService.OnDisconnected += OnDisconnected;
        }

        public async Task<SubscriptionResult> SubscribeToUserAsync(string userId)
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

                if (user == null)
                {
                    _logger.LogWarning("Cannot subscribe user {UserId}: User not found in database.", userId);
                    return SubscriptionResult.Failed;
                }

                // Check for token expiry and refresh if needed
                if (user.TokenExpiry.AddMinutes(-5) < DateTimeOffset.UtcNow)
                {
                    _logger.LogInformation("Access token for user {UserId} is expired or expiring soon. Refreshing...", userId);

                    if (!string.IsNullOrEmpty(user.RefreshToken))
                    {
                        var newToken = await authService.RefreshTokenAsync(user.RefreshToken);
                        if (newToken != null)
                        {
                            user.AccessToken = newToken.AccessToken;
                            user.RefreshToken = newToken.RefreshToken; // Refresh token might rotate
                            user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);

                            await userRepository.SaveUserAsync(user);
                            _logger.LogInformation("Successfully refreshed access token for user {UserId}.", userId);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to refresh access token for user {UserId}.", userId);
                            return SubscriptionResult.Unauthorized;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Cannot refresh token for user {UserId}: No refresh token available.", userId);
                        return SubscriptionResult.Unauthorized;
                    }
                }

                if (string.IsNullOrEmpty(user.AccessToken))
                {
                    _logger.LogWarning("Cannot subscribe user {UserId}: Access token is missing.", userId);
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
                        _twitchApi.Settings.AccessToken = user.AccessToken;
                        var validation = await _twitchApi.Auth.ValidateAccessTokenAsync();
                        if (validation == null || string.IsNullOrEmpty(validation.UserId))
                        {
                            _logger.LogError("Token validation returned null or empty User ID for user {UserId}", userId);
                            return SubscriptionResult.Unauthorized;
                        }
                        tokenUserId = validation.UserId;
                        tokenScopes = validation.Scopes;
                        _logger.LogInformation("Token validated. User ID: {TokenUserId}, Login: {Login}, Client ID: {ClientId}, Scopes: {Scopes}",
                            validation.UserId, validation.Login, validation.ClientId, string.Join(", ", tokenScopes ?? new List<string>()));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to validate access token for user {UserId}", userId);
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
                            userId, string.Join(", ", missingScopes));
                        return SubscriptionResult.RequiresReauth;
                    }

                    var hasChatScope = tokenScopes?.Contains("user:read:chat") == true;

                    // Use the token's User ID as the broadcaster ID (they are the same for self-monitoring)
                    var broadcasterId = tokenUserId;
                    _logger.LogInformation("Using broadcaster_user_id={BroadcasterId}, user_id={UserId} for subscriptions", broadcasterId, tokenUserId);

                    var condition = new Dictionary<string, string> { { "broadcaster_user_id", broadcasterId } };
                    var sessionId = _eventSubService.SessionId;

                    // Track the current access token - this gets updated if we refresh
                    var currentAccessToken = user.AccessToken;

                    if (string.IsNullOrEmpty(sessionId))
                    {
                        _logger.LogWarning("Cannot subscribe user {UserId}: Session ID is missing.", userId);
                        return SubscriptionResult.Failed;
                    }

                    // Subscribe to stream.online
                    try
                    {
                        await helixWrapper.CreateEventSubSubscriptionAsync(
                            _twitchSettings.ClientId, currentAccessToken, "stream.online", "1", condition, EventSubTransportMethod.Websocket, sessionId);
                        _logger.LogInformation("✅ Successfully subscribed to stream.online");
                    }
                    catch (TwitchLib.Api.Core.Exceptions.BadTokenException btEx)
                    {
                        _logger.LogWarning(btEx, "⚠️ BadTokenException for stream.online - forcing token refresh...");
                        var refreshedUser = await ForceRefreshTokenAsync(user, authService, userRepository);
                        if (refreshedUser != null)
                        {
                            user = refreshedUser;
                            currentAccessToken = user.AccessToken;
                            try
                            {
                                await helixWrapper.CreateEventSubSubscriptionAsync(
                                    _twitchSettings.ClientId, currentAccessToken, "stream.online", "1", condition, EventSubTransportMethod.Websocket, sessionId);
                                _logger.LogInformation("✅ Successfully subscribed to stream.online after token refresh");
                            }
                            catch (Exception retryEx) { _logger.LogError(retryEx, "❌ Failed to subscribe to stream.online even after token refresh"); }
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to subscribe to stream.online"); }

                    // Subscribe to stream.offline
                    try
                    {
                        await helixWrapper.CreateEventSubSubscriptionAsync(
                            _twitchSettings.ClientId, currentAccessToken, "stream.offline", "1", condition, EventSubTransportMethod.Websocket, sessionId);
                        _logger.LogInformation("✅ Successfully subscribed to stream.offline");
                    }
                    catch (TwitchLib.Api.Core.Exceptions.BadTokenException btEx)
                    {
                        _logger.LogWarning(btEx, "⚠️ BadTokenException for stream.offline - forcing token refresh...");
                        var refreshedUser = await ForceRefreshTokenAsync(user, authService, userRepository);
                        if (refreshedUser != null)
                        {
                            user = refreshedUser;
                            currentAccessToken = user.AccessToken;
                            try
                            {
                                await helixWrapper.CreateEventSubSubscriptionAsync(
                                    _twitchSettings.ClientId, currentAccessToken, "stream.offline", "1", condition, EventSubTransportMethod.Websocket, sessionId);
                                _logger.LogInformation("✅ Successfully subscribed to stream.offline after token refresh");
                            }
                            catch (Exception retryEx) { _logger.LogError(retryEx, "❌ Failed to subscribe to stream.offline even after token refresh"); }
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to subscribe to stream.offline"); }

                    // Add Channel Events
                    // channel.follow v2 requires moderator_user_id as well
                    var followCondition = new Dictionary<string, string>
                    {
                        { "broadcaster_user_id", broadcasterId },
                        { "moderator_user_id", tokenUserId } // Must match token owner
                    };

                    try
                    {
                        await helixWrapper.CreateEventSubSubscriptionAsync(
                            _twitchSettings.ClientId, currentAccessToken, "channel.follow", "2", followCondition, EventSubTransportMethod.Websocket, sessionId);
                        _logger.LogInformation("✅ Successfully subscribed to channel.follow");
                    }
                    catch (TwitchLib.Api.Core.Exceptions.BadTokenException btEx)
                    {
                        _logger.LogWarning(btEx, "⚠️ BadTokenException for channel.follow - forcing token refresh...");
                        var refreshedUser = await ForceRefreshTokenAsync(user, authService, userRepository);
                        if (refreshedUser != null)
                        {
                            user = refreshedUser;
                            currentAccessToken = user.AccessToken;
                            try
                            {
                                await helixWrapper.CreateEventSubSubscriptionAsync(
                                    _twitchSettings.ClientId, currentAccessToken, "channel.follow", "2", followCondition, EventSubTransportMethod.Websocket, sessionId);
                                _logger.LogInformation("✅ Successfully subscribed to channel.follow after token refresh");
                            }
                            catch (Exception retryEx) { _logger.LogError(retryEx, "❌ Failed to subscribe to channel.follow even after token refresh"); }
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to subscribe to channel.follow"); }

                    // For chat messages, we need BOTH broadcaster_user_id and user_id.
                    // Since the broadcaster is monitoring their OWN channel, both values are the same:
                    // broadcaster_user_id = the channel to monitor chat in (broadcaster's ID)
                    // user_id = the user reading the chat (also the broadcaster's ID, since it's their token)
                    // NOTE: These require the 'user:read:chat' scope which may not be present on older tokens
                    if (hasChatScope)
                    {
                        var chatCondition = new Dictionary<string, string>
                        {
                            { "broadcaster_user_id", tokenUserId },  // Broadcaster's channel
                            { "user_id", tokenUserId }               // Same ID - broadcaster is reading their own chat
                        };

                        _logger.LogInformation("Subscribing to channel.chat.message with broadcaster_user_id={BroadcasterId}, user_id={UserId} (same value for self-monitoring)",
                            tokenUserId, tokenUserId);

                        try
                        {
                            await helixWrapper.CreateEventSubSubscriptionAsync(
                                _twitchSettings.ClientId, currentAccessToken, "channel.chat.message", "1", chatCondition, EventSubTransportMethod.Websocket, sessionId);
                            _logger.LogInformation("✅ Successfully subscribed to channel.chat.message");
                        }
                        catch (TwitchLib.Api.Core.Exceptions.BadTokenException btEx)
                        {
                            _logger.LogWarning(btEx, "⚠️ BadTokenException for channel.chat.message - user may need to re-login");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Failed to subscribe to channel.chat.message. Condition: broadcaster_user_id={BroadcasterId}, user_id={UserId}",
                                tokenUserId, tokenUserId);
                        }

                        try
                        {
                            await helixWrapper.CreateEventSubSubscriptionAsync(
                                _twitchSettings.ClientId, currentAccessToken, "channel.chat.notification", "1", chatCondition, EventSubTransportMethod.Websocket, sessionId);
                            _logger.LogInformation("✅ Successfully subscribed to channel.chat.notification");
                        }
                        catch (TwitchLib.Api.Core.Exceptions.BadTokenException btEx)
                        {
                            _logger.LogWarning(btEx, "⚠️ BadTokenException for channel.chat.notification - user may need to re-login");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Failed to subscribe to channel.chat.notification. Condition: broadcaster_user_id={BroadcasterId}, user_id={UserId}",
                                tokenUserId, tokenUserId);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("⏭️ Skipping chat EventSub subscriptions (channel.chat.message, channel.chat.notification) - missing 'user:read:chat' scope. Basic stream monitoring will still work.");
                    }

                    _subscribedUsers.TryAdd(userId, true);
                    _usersWantingMonitoring.TryAdd(userId, true); // Track that this user wants monitoring
                    _logger.LogInformation("✅ User {UserId} fully subscribed to all events", userId);
                    return SubscriptionResult.Success;
                }
                catch (TwitchLib.Api.Core.Exceptions.BadScopeException)
                {
                    _logger.LogWarning("Failed to subscribe user {UserId}: Bad Scope / Unauthorized.", userId);
                    return SubscriptionResult.Unauthorized;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to subscribe user {UserId}", userId);
                    return SubscriptionResult.Failed;
                }
            }
        }

        public async Task UnsubscribeFromUserAsync(string userId)
        {
            _logger.LogInformation("🛑 Stop Monitoring requested for user {UserId}", userId);

            // Note: Helix doesn't easily support "unsubscribe by user" without tracking subscription IDs.
            // For now, we'll just mark as unsubscribed in our local tracking.
            // In a real implementation, we should store subscription IDs returned by CreateEventSubSubscriptionAsync.
            var wasSubscribed = _subscribedUsers.TryRemove(userId, out _);
            var wasWanting = _usersWantingMonitoring.TryRemove(userId, out _); // User explicitly stopped monitoring

            _logger.LogInformation("🛑 User {UserId} removed. WasSubscribed: {WasSubscribed}, WasWanting: {WasWanting}. Remaining active: {Count}, wanting: {WantingCount}",
                userId, wasSubscribed, wasWanting, _subscribedUsers.Count, _usersWantingMonitoring.Count);

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
                _logger.LogError("Cannot refresh token for user {UserId}: No refresh token available", user.TwitchUserId);
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
                    _logger.LogInformation("✅ Token refreshed successfully for user {UserId}", user.TwitchUserId);
                    return user;
                }
                else
                {
                    _logger.LogError("❌ Token refresh returned null for user {UserId}", user.TwitchUserId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during token refresh for user {UserId}", user.TwitchUserId);
                return null;
            }
        }

        public async Task<SubscriptionResult> ForceReconnectUserAsync(string userId)
        {
            // Re-subscribe
            return await SubscribeToUserAsync(userId);
        }

        public StreamMonitorStatus GetUserConnectionStatus(string userId)
        {
            var discordStatus = _lastDiscordNotifications.TryGetValue(userId, out var status) ? status : (Time: (DateTimeOffset?)null, Success: false);

            return new StreamMonitorStatus
            {
                Connected = _eventSubService.IsConnected,
                Subscriptions = _subscribedUsers.ContainsKey(userId) ? new[] { "stream.online", "stream.offline" } : Array.Empty<string>(),
                LastConnected = _eventSubService.IsConnected ? DateTimeOffset.UtcNow : null, // Approximate
                LastDiscordNotification = discordStatus.Time,
                LastDiscordNotificationSuccess = discordStatus.Success
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
            _logger.LogInformation($"EventSub Session Welcome. ID: {sessionId}");

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
                            _logger.LogInformation("🔄 Re-subscribing user {UserId}...", userId);
                            var result = await SubscribeToUserAsync(userId);
                            _logger.LogInformation("🔄 Re-subscription result for user {UserId}: {Result}", userId, result);
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
                                _logger.LogInformation("🔄 Re-subscription result for user {UserId}: {Result}", userId, result);
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

                if (subscriptionType == "stream.online")
                {
                    await HandleStreamOnline(eventData);
                }
                else if (subscriptionType == "stream.offline")
                {
                    await HandleStreamOffline(eventData);
                }
                else if (subscriptionType == "channel.follow")
                {
                    await HandleFollow(eventData);
                }
                else if (subscriptionType == "channel.subscribe")
                {
                    await HandleSubscribe(eventData);
                }
                else if (subscriptionType == "channel.subscription.gift")
                {
                    await HandleSubscriptionGift(eventData);
                }
                else if (subscriptionType == "channel.subscription.message")
                {
                    await HandleSubscriptionMessage(eventData);
                }
                else if (subscriptionType == "channel.cheer")
                {
                    await HandleCheer(eventData);
                }
                else if (subscriptionType == "channel.raid")
                {
                    await HandleRaid(eventData);
                }
                else if (subscriptionType == "channel.chat.message")
                {
                    await HandleChatMessage(eventData);
                }
                else if (subscriptionType == "channel.chat.notification")
                {
                    await HandleChatNotification(eventData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification.");
            }
        }

        private async Task HandleStreamOnline(System.Text.Json.JsonElement eventData)
        {
            string? broadcasterId = null;
            string? broadcasterName = null;

            try
            {
                if (eventData.TryGetProperty("broadcaster_user_id", out var idProp))
                    broadcasterId = idProp.GetString();

                if (eventData.TryGetProperty("broadcaster_user_name", out var nameProp))
                    broadcasterName = nameProp.GetString();
            }
            catch {}

            if (broadcasterId == null) return;

            _logger.LogInformation($"Stream Online: {broadcasterName} ({broadcasterId})");

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();
                var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>(); // Optional service

                var userId = broadcasterId;
                var user = await userRepository.GetUserAsync(userId);

                if (user != null)
                {
                    // Update Counter
                    var counters = await counterRepository.GetCountersAsync(userId);
                    if (counters != null)
                    {
                        counters.StreamStarted = DateTimeOffset.UtcNow;
                        await counterRepository.SaveCountersAsync(counters);
                    }

                    // Notify Overlay
                    if (overlayNotifier != null && counters != null)
                    {
                        await overlayNotifier.NotifyStreamStartedAsync(userId, counters);
                    }

                    // Fetch Stream Info
                    object notificationData = new { };
                    try
                    {
                        if (!string.IsNullOrEmpty(user.AccessToken) && !string.IsNullOrEmpty(_twitchSettings.ClientId))
                        {
                            var streams = await helixWrapper.GetStreamsAsync(_twitchSettings.ClientId, user.AccessToken, new List<string> { userId });
                            if (streams.Streams != null && streams.Streams.Length > 0)
                            {
                                var stream = streams.Streams[0];
                                notificationData = new
                                {
                                    title = stream.Title,
                                    game = stream.GameName,
                                    thumbnailUrl = stream.ThumbnailUrl.Replace("{width}", "640").Replace("{height}", "360") + $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                                    viewerCount = stream.ViewerCount,
                                    startedAt = stream.StartedAt,
                                    broadcasterName = stream.UserName
                                };
                                _logger.LogInformation($"Retrieved fresh stream info for {user.DisplayName}: {stream.Title} - {stream.GameName}");
                            }
                            else
                            {
                                // Fallback to Channel Information if stream data is not yet available
                                _logger.LogInformation($"Stream info not available yet for {user.DisplayName}, fetching channel info...");
                                var channelInfo = await helixWrapper.GetChannelInformationAsync(_twitchSettings.ClientId, user.AccessToken, userId);
                                if (channelInfo.Data != null && channelInfo.Data.Length > 0)
                                {
                                    var info = channelInfo.Data[0];
                                    notificationData = new
                                    {
                                        title = info.Title,
                                        game = info.GameName,
                                        thumbnailUrl = user.ProfileImageUrl, // Fallback to profile image since stream thumbnail isn't ready
                                        viewerCount = 0,
                                        startedAt = DateTime.UtcNow,
                                        broadcasterName = info.BroadcasterName
                                    };
                                    _logger.LogInformation($"Retrieved channel info for {user.DisplayName}: {info.Title} - {info.GameName}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to fetch stream info for {user.DisplayName}, using event data fallback.");
                    }

                    // Send Discord Notification
                    if (notificationData != null)
                    {
                        try
                        {
                            await discordService.SendNotificationAsync(user, "stream_start", notificationData);
                            _lastDiscordNotifications[userId] = (DateTimeOffset.UtcNow, true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send Discord notification for user {UserId}", userId);
                            _lastDiscordNotifications[userId] = (DateTimeOffset.UtcNow, false);
                        }
                    }
                }
            }
        }

        private async Task HandleStreamOffline(System.Text.Json.JsonElement eventData)
        {
            string? broadcasterId = null;
            string? broadcasterName = null;

            try
            {
                if (eventData.TryGetProperty("broadcaster_user_id", out var idProp))
                    broadcasterId = idProp.GetString();

                if (eventData.TryGetProperty("broadcaster_user_name", out var nameProp))
                    broadcasterName = nameProp.GetString();
            }
            catch {}

            if (broadcasterId == null) return;

            _logger.LogInformation($"Stream Offline: {broadcasterName} ({broadcasterId})");

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

                var userId = broadcasterId;
                var user = await userRepository.GetUserAsync(userId);

                if (user != null)
                {
                    // Update Counter
                    var counters = await counterRepository.GetCountersAsync(userId);
                    if (counters != null)
                    {
                        counters.StreamStarted = null;
                        await counterRepository.SaveCountersAsync(counters);
                    }

                    // Notify Overlay
                    if (overlayNotifier != null && counters != null)
                    {
                        await overlayNotifier.NotifyStreamEndedAsync(userId, counters);
                    }
                }
            }
        }

        private async Task HandleFollow(System.Text.Json.JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId)) return;

            string displayName = "Someone";
            if (eventData.TryGetProperty("user_name", out var nameProp)) displayName = nameProp.GetString() ?? "Someone";

            using (var scope = _scopeFactory.CreateScope())
            {
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyFollowerAsync(broadcasterId!, displayName);
                }
            }
        }

        private string GetReadableTier(string tier)
        {
            return tier switch
            {
                "1000" => "Tier 1",
                "2000" => "Tier 2",
                "3000" => "Tier 3",
                "Prime" => "Prime",
                _ => tier
            };
        }

        private async Task HandleSubscribe(System.Text.Json.JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId)) return;

            string displayName = "Someone";
            if (eventData.TryGetProperty("user_name", out var nameProp)) displayName = nameProp.GetString() ?? "Someone";

            string tier = "1000";
            if (eventData.TryGetProperty("tier", out var tierProp)) tier = tierProp.GetString() ?? "1000";
            tier = GetReadableTier(tier);

            bool isGift = false;
            if (eventData.TryGetProperty("is_gift", out var giftProp)) isGift = giftProp.GetBoolean();

            using (var scope = _scopeFactory.CreateScope())
            {
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifySubscriberAsync(broadcasterId!, displayName, tier, isGift);
                }
            }
        }

        private async Task HandleSubscriptionGift(System.Text.Json.JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId)) return;

            string gifterName = "Anonymous";
            if (eventData.TryGetProperty("user_name", out var nameProp) && nameProp.ValueKind != System.Text.Json.JsonValueKind.Null)
                gifterName = nameProp.GetString() ?? "Anonymous";
            else if (eventData.TryGetProperty("is_anonymous", out var anonProp) && anonProp.GetBoolean())
                gifterName = "Anonymous";

            int total = 1;
            if (eventData.TryGetProperty("total", out var totalProp)) total = totalProp.GetInt32();

            string tier = "1000";
            if (eventData.TryGetProperty("tier", out var tierProp)) tier = tierProp.GetString() ?? "1000";
            tier = GetReadableTier(tier);

            using (var scope = _scopeFactory.CreateScope())
            {
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                if (overlayNotifier != null)
                {
                    // NotifyGiftSubAsync expects recipientName, but this event is for the BATCH of gifts.
                    // We'll use "Community" or similar as recipient for the batch alert.
                    await overlayNotifier.NotifyGiftSubAsync(broadcasterId!, gifterName, "Community", tier, total);
                }
            }
        }

        private async Task HandleSubscriptionMessage(System.Text.Json.JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId)) return;

            string displayName = "Someone";
            if (eventData.TryGetProperty("user_name", out var nameProp)) displayName = nameProp.GetString() ?? "Someone";

            string message = "";
            if (eventData.TryGetProperty("message", out var msgProp) && msgProp.TryGetProperty("text", out var textProp))
                message = textProp.GetString() ?? "";

            // Check for Discord keywords in resub message
            if (!string.IsNullOrEmpty(message) &&
                (message.Contains("!discord", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("discord link", StringComparison.OrdinalIgnoreCase)))
            {
                await SendDiscordInvite(broadcasterId!);
            }

            int months = 1;
            if (eventData.TryGetProperty("cumulative_months", out var monthsProp)) months = monthsProp.GetInt32();

            string tier = "1000";
            if (eventData.TryGetProperty("tier", out var tierProp)) tier = tierProp.GetString() ?? "1000";
            tier = GetReadableTier(tier);

            using (var scope = _scopeFactory.CreateScope())
            {
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyResubAsync(broadcasterId!, displayName, months, tier, message);
                }
            }
        }

        private async Task HandleCheer(System.Text.Json.JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId)) return;

            string displayName = "Anonymous";
            if (eventData.TryGetProperty("user_name", out var nameProp) && nameProp.ValueKind != System.Text.Json.JsonValueKind.Null)
                displayName = nameProp.GetString() ?? "Anonymous";
            else if (eventData.TryGetProperty("is_anonymous", out var anonProp) && anonProp.GetBoolean())
                displayName = "Anonymous";

            int bits = 0;
            if (eventData.TryGetProperty("bits", out var bitsProp)) bits = bitsProp.GetInt32();

            string message = "";
            if (eventData.TryGetProperty("message", out var msgProp)) message = msgProp.GetString() ?? "";

            using (var scope = _scopeFactory.CreateScope())
            {
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyBitsAsync(broadcasterId!, displayName, bits, message, 0); // totalBits unknown from this event
                }
            }
        }

        private async Task HandleRaid(System.Text.Json.JsonElement eventData)
        {
            // Raid event has "to_broadcaster_user_id" as the target (us)
            string? broadcasterId = null;
            if (eventData.TryGetProperty("to_broadcaster_user_id", out var idProp))
                broadcasterId = idProp.GetString();

            if (broadcasterId == null) return;

            string raiderName = "Someone";
            if (eventData.TryGetProperty("from_broadcaster_user_name", out var nameProp)) raiderName = nameProp.GetString() ?? "Someone";

            int viewers = 0;
            if (eventData.TryGetProperty("viewers", out var viewersProp)) viewers = viewersProp.GetInt32();

            using (var scope = _scopeFactory.CreateScope())
            {
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyRaidAsync(broadcasterId!, raiderName, viewers);
                }
            }
        }

        private async Task HandleChatMessage(System.Text.Json.JsonElement eventData)
        {
            try
            {
                if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null) return;

                if (eventData.TryGetProperty("message", out var messageProp) &&
                    messageProp.TryGetProperty("text", out var textProp))
                {
                    var messageText = textProp.GetString();
                    if (string.IsNullOrEmpty(messageText)) return;

                    // Check for Discord keywords
                    if (messageText.Contains("!discord", StringComparison.OrdinalIgnoreCase) ||
                        messageText.Contains("discord link", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendDiscordInvite(broadcasterId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling chat message.");
            }
        }

        private async Task HandleChatNotification(System.Text.Json.JsonElement eventData)
        {
            try
            {
                if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null) return;

                string noticeType = "";
                if (eventData.TryGetProperty("notice_type", out var noticeProp))
                    noticeType = noticeProp.GetString() ?? "";

                _logger.LogInformation($"Chat Notification: {noticeType} for {broadcasterId}");

                // Check for Discord keywords in the message if present
                if (eventData.TryGetProperty("message", out var messageProp) &&
                    messageProp.TryGetProperty("text", out var textProp))
                {
                    var messageText = textProp.GetString();
                    if (!string.IsNullOrEmpty(messageText) &&
                        (messageText.Contains("!discord", StringComparison.OrdinalIgnoreCase) ||
                         messageText.Contains("discord link", StringComparison.OrdinalIgnoreCase)))
                    {
                        await SendDiscordInvite(broadcasterId);
                    }
                }

                string chatterName = "Someone";
                if (eventData.TryGetProperty("chatter_user_name", out var chatterProp))
                    chatterName = chatterProp.GetString() ?? "Someone";

                using (var scope = _scopeFactory.CreateScope())
                {
                    var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();
                    if (overlayNotifier == null) return;

                    switch (noticeType)
                    {
                        case "sub":
                            if (eventData.TryGetProperty("sub", out var subProp))
                            {
                                string tier = "1000";
                                if (subProp.TryGetProperty("sub_tier", out var tierProp)) tier = tierProp.GetString() ?? "1000";
                                tier = GetReadableTier(tier);
                                bool isPrime = false;
                                if (subProp.TryGetProperty("is_prime", out var primeProp)) isPrime = primeProp.GetBoolean();

                                await overlayNotifier.NotifySubscriberAsync(broadcasterId, chatterName, tier, false);
                            }
                            break;

                        case "resub":
                            if (eventData.TryGetProperty("resub", out var resubProp))
                            {
                                int months = 1;
                                if (resubProp.TryGetProperty("cumulative_months", out var monthsProp)) months = monthsProp.GetInt32();
                                string tier = "1000";
                                if (resubProp.TryGetProperty("sub_tier", out var tierProp)) tier = tierProp.GetString() ?? "1000";
                                tier = GetReadableTier(tier);

                                string message = "";
                                if (eventData.TryGetProperty("message", out var msgProp) && msgProp.TryGetProperty("text", out var txtProp))
                                    message = txtProp.GetString() ?? "";

                                await overlayNotifier.NotifyResubAsync(broadcasterId, chatterName, months, tier, message);
                            }
                            break;

                        case "sub_gift":
                            if (eventData.TryGetProperty("sub_gift", out var giftProp))
                            {
                                int duration = 1;
                                if (giftProp.TryGetProperty("duration_months", out var durProp)) duration = durProp.GetInt32();
                                string tier = "1000";
                                if (giftProp.TryGetProperty("sub_tier", out var tierProp)) tier = tierProp.GetString() ?? "1000";
                                tier = GetReadableTier(tier);
                                string recipientName = "Someone";
                                if (giftProp.TryGetProperty("recipient_user_name", out var recProp)) recipientName = recProp.GetString() ?? "Someone";

                                // NotifyGiftSubAsync(userId, gifterName, recipientName, tier, totalGifts)
                                // This is a single gift, so totalGifts = 1
                                await overlayNotifier.NotifyGiftSubAsync(broadcasterId, chatterName, recipientName, tier, 1);
                            }
                            break;

                        case "community_sub_gift":
                            if (eventData.TryGetProperty("community_sub_gift", out var commGiftProp))
                            {
                                int total = 1;
                                if (commGiftProp.TryGetProperty("total", out var totalProp)) total = totalProp.GetInt32();
                                string tier = "1000";
                                if (commGiftProp.TryGetProperty("sub_tier", out var tierProp)) tier = tierProp.GetString() ?? "1000";
                                tier = GetReadableTier(tier);

                                // For community gifts, recipient is "Community" or similar
                                await overlayNotifier.NotifyGiftSubAsync(broadcasterId, chatterName, "Community", tier, total);
                            }
                            break;

                        case "raid":
                            if (eventData.TryGetProperty("raid", out var raidProp))
                            {
                                int viewers = 0;
                                if (raidProp.TryGetProperty("viewer_count", out var viewProp)) viewers = viewProp.GetInt32();
                                string raiderName = chatterName; // The chatter is the raider

                                await overlayNotifier.NotifyRaidAsync(broadcasterId, raiderName, viewers);
                            }
                            break;

                        case "announcement":
                            // Optional: Handle announcements
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling chat notification.");
            }
        }

        private async Task SendDiscordInvite(string broadcasterId)
        {
            // Check throttle (5 minutes)
            if (_lastDiscordNotifications.TryGetValue(broadcasterId, out var lastStatus))
            {
                if ((DateTimeOffset.UtcNow - lastStatus.Time).TotalMinutes < 5)
                {
                    return; // Throttled
                }
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var user = await userRepository.GetUserAsync(broadcasterId);

                if (user == null || string.IsNullOrEmpty(user.AccessToken)) return;

                try
                {
                    string discordInviteLink = !string.IsNullOrEmpty(user.DiscordInviteLink)
                        ? user.DiscordInviteLink
                        : "https://discord.gg/omniasylum"; // Fallback

                    string message = $"Join our Discord community! {discordInviteLink}";

                    // Send message to chat
                    var client = _httpClientFactory.CreateClient();
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/chat/messages");
                    request.Headers.Add("Client-Id", _twitchSettings.ClientId);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.AccessToken);

                    var payload = new
                    {
                        broadcaster_id = broadcasterId,
                        sender_id = broadcasterId,
                        message = message
                    };

                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    var response = await client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError($"Failed to send chat message. Status: {response.StatusCode}, Error: {errorContent}");
                        throw new Exception($"Failed to send chat message: {response.StatusCode}");
                    }

                    // Update throttle
                    _lastDiscordNotifications.AddOrUpdate(broadcasterId,
                        (DateTimeOffset.UtcNow, true),
                        (key, old) => (DateTimeOffset.UtcNow, true));

                    _logger.LogInformation($"Sent Discord invite to channel {broadcasterId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send Discord invite.");
                    _lastDiscordNotifications.AddOrUpdate(broadcasterId,
                        (DateTimeOffset.UtcNow, false),
                        (key, old) => (DateTimeOffset.UtcNow, false));
                }
            }
        }

        private bool TryGetBroadcasterId(System.Text.Json.JsonElement eventData, out string? broadcasterId)
        {
            broadcasterId = null;
            if (eventData.TryGetProperty("broadcaster_user_id", out var idProp))
                broadcasterId = idProp.GetString();
            return broadcasterId != null;
        }
    }
}
