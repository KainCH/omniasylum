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
            // Ensure connected
            if (!_eventSubService.IsConnected)
            {
                try
                {
                    await _eventSubService.ConnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect to EventSub service.");
                    return SubscriptionResult.Failed;
                }
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var helixWrapper = scope.ServiceProvider.GetRequiredService<ITwitchHelixWrapper>();
                var user = await userRepository.GetUserAsync(userId);

                if (user == null)
                {
                    _logger.LogWarning("Cannot subscribe user {UserId}: User not found in database.", userId);
                    return SubscriptionResult.Failed;
                }

                if (string.IsNullOrEmpty(user.AccessToken))
                {
                    _logger.LogWarning("Cannot subscribe user {UserId}: Access token is missing.", userId);
                    return SubscriptionResult.Unauthorized;
                }

                try
                {
                    var condition = new Dictionary<string, string> { { "broadcaster_user_id", user.TwitchUserId } };
                    var sessionId = _eventSubService.SessionId;

                    if (string.IsNullOrEmpty(sessionId))
                    {
                        _logger.LogWarning("Cannot subscribe user {UserId}: Session ID is missing.", userId);
                        return SubscriptionResult.Failed;
                    }

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "stream.online", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "stream.offline", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    // Add Channel Events
                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.follow", "2", condition, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.subscribe", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.subscription.gift", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.subscription.message", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.cheer", "1", condition, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.raid", "1", new Dictionary<string, string> { { "to_broadcaster_user_id", user.TwitchUserId } }, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.chat.message", "1", new Dictionary<string, string> { { "broadcaster_user_id", user.TwitchUserId }, { "user_id", user.TwitchUserId } }, EventSubTransportMethod.Websocket, sessionId);

                    await helixWrapper.CreateEventSubSubscriptionAsync(
                        _twitchSettings.ClientId, user.AccessToken, "channel.chat.notification", "1", new Dictionary<string, string> { { "broadcaster_user_id", user.TwitchUserId }, { "user_id", user.TwitchUserId } }, EventSubTransportMethod.Websocket, sessionId);

                    _subscribedUsers.TryAdd(userId, true);
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
            // Note: Helix doesn't easily support "unsubscribe by user" without tracking subscription IDs.
            // For now, we'll just mark as unsubscribed in our local tracking.
            // In a real implementation, we should store subscription IDs returned by CreateEventSubSubscriptionAsync.
            _subscribedUsers.TryRemove(userId, out _);
            await Task.CompletedTask;
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
            // Start watchdog to ensure connection stays alive IF connected
            _connectionWatchdog = new Timer(CheckConnection, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            await Task.CompletedTask;
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
            _logger.LogWarning("EventSub Disconnected.");
            await Task.CompletedTask;
        }

        private async void CheckConnection(object? state)
        {
            if (!_eventSubService.IsConnected)
            {
                _logger.LogWarning("Watchdog detected disconnected state. Attempting to reconnect...");
                try
                {
                    await _eventSubService.ConnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Watchdog reconnection failed.");
                }
            }
            else
            {
                // Check keepalive
                var timeSinceLastKeepalive = DateTime.UtcNow - _eventSubService.LastKeepaliveTime;
                if (timeSinceLastKeepalive.TotalSeconds > 30) // Assuming default keepalive is ~10s
                {
                    _logger.LogWarning($"No keepalive received for {timeSinceLastKeepalive.TotalSeconds:F1}s. Reconnecting...");
                    try
                    {
                        await _eventSubService.DisconnectAsync();
                        await _eventSubService.ConnectAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Keepalive reconnection failed.");
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
