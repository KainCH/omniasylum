using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.Client.Events;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    internal interface ITwitchBotClient
    {
        bool IsConnected { get; }
        event EventHandler<OnLogArgs>? OnLog;
        event EventHandler<OnConnectedArgs>? OnConnected;
        event EventHandler<OnMessageReceivedArgs>? OnMessageReceived;
        void Initialize(ConnectionCredentials credentials);
        bool Connect();
        void JoinChannel(string channel);
        void LeaveChannel(string channel);
        void SendMessage(string channel, string message);
    }

    internal interface ITwitchBotClientFactory
    {
        ITwitchBotClient Create(ClientOptions clientOptions);
    }

    internal sealed class TwitchLibBotClientFactory : ITwitchBotClientFactory
    {
        private sealed class TwitchLibBotClient : ITwitchBotClient
        {
            private readonly TwitchClient _client;

            public TwitchLibBotClient(TwitchClient client)
            {
                _client = client;
            }

            public bool IsConnected => _client.IsConnected;

            public event EventHandler<OnLogArgs>? OnLog
            {
                add => _client.OnLog += value;
                remove => _client.OnLog -= value;
            }

            public event EventHandler<OnConnectedArgs>? OnConnected
            {
                add => _client.OnConnected += value;
                remove => _client.OnConnected -= value;
            }

            public event EventHandler<OnMessageReceivedArgs>? OnMessageReceived
            {
                add => _client.OnMessageReceived += value;
                remove => _client.OnMessageReceived -= value;
            }

            public void Initialize(ConnectionCredentials credentials) => _client.Initialize(credentials);

            public bool Connect() => _client.Connect();

            public void JoinChannel(string channel) => _client.JoinChannel(channel);

            public void LeaveChannel(string channel) => _client.LeaveChannel(channel);

            public void SendMessage(string channel, string message) => _client.SendMessage(channel, message);
        }

        public ITwitchBotClient Create(ClientOptions clientOptions)
        {
            var customClient = new WebSocketClient(clientOptions);
            var client = new TwitchClient(customClient);
            return new TwitchLibBotClient(client);
        }
    }

    public class TwitchClientManager : ITwitchClientManager
    {
        private readonly ConcurrentDictionary<string, string> _userIdToChannel = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _channelToUserId = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly SemaphoreSlim _botConnectLock = new SemaphoreSlim(1, 1);
        private ITwitchBotClient? _botClient;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITwitchMessageHandler _messageHandler;
        private readonly TwitchSettings _twitchSettings;
        private readonly ILogger<TwitchClientManager> _logger;
        private readonly IDashboardFeedService? _dashboardFeedService;
        private readonly IAutoShoutoutService? _autoShoutoutService;
        private readonly IBotModerationService? _botModerationService;
        private readonly IBotReactionService? _botReactionService;

        private readonly ITwitchBotClientFactory _botClientFactory;

        public TwitchClientManager(
            IServiceScopeFactory scopeFactory,
            ITwitchMessageHandler messageHandler,
            IOptions<TwitchSettings> twitchSettings,
            ILogger<TwitchClientManager> logger,
            IDashboardFeedService dashboardFeedService,
            IAutoShoutoutService autoShoutoutService,
            IBotModerationService botModerationService,
            IBotReactionService botReactionService)
            : this(scopeFactory, messageHandler, twitchSettings, logger, new TwitchLibBotClientFactory(),
                   dashboardFeedService, autoShoutoutService, botModerationService, botReactionService)
        {
        }

        internal TwitchClientManager(
            IServiceScopeFactory scopeFactory,
            ITwitchMessageHandler messageHandler,
            IOptions<TwitchSettings> twitchSettings,
            ILogger<TwitchClientManager> logger,
            ITwitchBotClientFactory botClientFactory,
            IDashboardFeedService? dashboardFeedService = null,
            IAutoShoutoutService? autoShoutoutService = null,
            IBotModerationService? botModerationService = null,
            IBotReactionService? botReactionService = null)
        {
            _scopeFactory = scopeFactory;
            _messageHandler = messageHandler;
            _twitchSettings = twitchSettings.Value;
            _logger = logger;
            _botClientFactory = botClientFactory;
            _dashboardFeedService = dashboardFeedService;
            _autoShoutoutService = autoShoutoutService;
            _botModerationService = botModerationService;
            _botReactionService = botReactionService;
        }

        public async Task ConnectUserAsync(string userId)
        {
            if (_userIdToChannel.ContainsKey(userId))
            {
                return;
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var authService = scope.ServiceProvider.GetRequiredService<ITwitchAuthService>();
                var botCredentialRepository = scope.ServiceProvider.GetRequiredService<IBotCredentialRepository>();
                var user = await userRepository.GetUserAsync(userId).ConfigureAwait(false);

                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", LogValue.Safe(userId));
                    return;
                }

                var channelLogin = (user.Username ?? string.Empty).Trim().TrimStart('#');
                if (string.IsNullOrWhiteSpace(channelLogin))
                {
                    _logger.LogWarning("User {UserId} has no username/channel to join", LogValue.Safe(userId));
                    return;
                }

                var botClient = await EnsureBotConnectedAsync(botCredentialRepository, authService).ConfigureAwait(false);
                if (botClient == null)
                {
                    _logger.LogError("❌ Forge bot is not configured/connected. Cannot join channel for user {UserId}", LogValue.Safe(userId));
                    return;
                }

                // Join channel and track mappings for message routing.
                botClient.JoinChannel(channelLogin);
                _userIdToChannel.TryAdd(userId, channelLogin);
                _channelToUserId.AddOrUpdate(channelLogin.ToLowerInvariant(), userId, (_, __) => userId);
                _logger.LogInformation("✅ Forge bot joined channel {Channel} for user {UserId}",
                    LogValue.Safe(channelLogin),
                    LogValue.Safe(userId));
            }
        }

        public Task DisconnectUserAsync(string userId)
        {
            if (_userIdToChannel.TryRemove(userId, out var channel))
            {
                _channelToUserId.TryRemove(channel.ToLowerInvariant(), out _);
                var client = _botClient;
                if (client != null && client.IsConnected)
                {
                    client.LeaveChannel(channel);
                }
            }
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(string userId, string message)
        {
            var client = _botClient;
            if (client != null && client.IsConnected && _userIdToChannel.TryGetValue(userId, out var channel))
            {
                client.SendMessage(channel, message);
            }
            return Task.CompletedTask;
        }

        public BotStatus GetUserBotStatus(string userId)
        {
            var client = _botClient;
            bool connected = client != null && client.IsConnected && _userIdToChannel.ContainsKey(userId);
            return new BotStatus
            {
                Connected = connected,
                Reason = connected ? "Connected" : "Not connected"
            };
        }

        private void HandleMessage(TwitchLib.Client.Models.ChatMessage chatMessage)
        {
            FireAndForget(HandleMessageAsync(chatMessage), "HandleMessageAsync");
        }

        private async Task HandleMessageAsync(TwitchLib.Client.Models.ChatMessage chatMessage)
        {
            try
            {
                var channel = (chatMessage.Channel ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant();
                if (string.IsNullOrEmpty(channel)) return;

                if (!_channelToUserId.TryGetValue(channel, out var userId)) return;

                // 1. Bot moderation — awaited so enforcement (delete/ban) completes before
                //    command processing. If enforced, skip commands and reactions entirely.
                if (_botModerationService != null)
                {
                    var enforced = await _botModerationService.CheckAndEnforceAsync(
                        userId, chatMessage.UserId, chatMessage.Username,
                        chatMessage.Id, chatMessage.Message,
                        chatMessage.IsModerator, chatMessage.IsBroadcaster).ConfigureAwait(false);
                    if (enforced) return;
                }

                // 2. Dashboard feed
                _dashboardFeedService?.PushChatMessage(userId, new DashboardChatMessage(
                    userId,
                    chatMessage.Username,
                    chatMessage.DisplayName,
                    chatMessage.Message,
                    chatMessage.IsModerator,
                    chatMessage.IsBroadcaster,
                    chatMessage.IsSubscriber,
                    chatMessage.ColorHex,
                    DateTimeOffset.UtcNow));

                // 3. Regular message/command handling
                await _messageHandler.HandleMessageAsync(userId, chatMessage, SendMessageAsync).ConfigureAwait(false);

                // 4. Auto-shoutout (fire-and-forget)
                if (_autoShoutoutService != null)
                    FireAndForget(
                        _autoShoutoutService.HandleChatMessageAsync(
                            userId, chatMessage.UserId, chatMessage.Username,
                            chatMessage.DisplayName, chatMessage.IsModerator, chatMessage.IsBroadcaster),
                        "AutoShoutoutService.HandleChatMessageAsync");

                // 5. First-time chatter (fire-and-forget)
                if (_botReactionService != null)
                    FireAndForget(
                        _botReactionService.HandleFirstTimeChatAsync(userId, chatMessage.UserId, chatMessage.DisplayName),
                        "BotReactionService.HandleFirstTimeChatAsync");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling Twitch chat message");
            }
        }

        private void FireAndForget(Task task, string operation)
        {
            if (task.IsCompleted)
            {
                return;
            }

            _ = task.ContinueWith(
                t => _logger.LogError(t.Exception, "❌ Fire-and-forget operation failed: {Operation}", operation),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private async Task<ITwitchBotClient?> EnsureBotConnectedAsync(
            IBotCredentialRepository botCredentialRepository,
            ITwitchAuthService authService)
        {
            // Fast path
            var existing = _botClient;
            if (existing != null && existing.IsConnected)
            {
                return existing;
            }

            // Prevent concurrent connect attempts (including async token refresh and client connect)
            await _botConnectLock.WaitAsync().ConfigureAwait(false);
            try
            {
                existing = _botClient;
                if (existing != null && existing.IsConnected)
                {
                    return existing;
                }

                var creds = await botCredentialRepository.GetAsync().ConfigureAwait(false);
                if (creds == null)
                {
                    // Seed from configuration if present
                    if (!string.IsNullOrWhiteSpace(_twitchSettings.BotUsername)
                        && !string.IsNullOrWhiteSpace(_twitchSettings.BotRefreshToken))
                    {
                        creds = new BotCredentials
                        {
                            Username = _twitchSettings.BotUsername,
                            AccessToken = _twitchSettings.BotAccessToken,
                            RefreshToken = _twitchSettings.BotRefreshToken,
                            TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(-10)
                        };

                        await botCredentialRepository.SaveAsync(creds).ConfigureAwait(false);
                        _logger.LogInformation("✅ Seeded Forge bot credentials from configuration for {Username}", (creds.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Forge bot credentials not found. Configure via /auth/twitch/bot or set Twitch:BotUsername + Twitch:BotRefreshToken");
                        return null;
                    }
                }

                // Refresh bot token if needed (buffer of 5 minutes)
                if (creds.TokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    _logger.LogInformation("🔄 Refreshing Forge bot token for {Username}", (creds.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                    var refreshed = await authService.RefreshTokenAsync(creds.RefreshToken).ConfigureAwait(false);
                    if (refreshed == null)
                    {
                        _logger.LogError("❌ Failed to refresh Forge bot token for {Username}", (creds.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                        return null;
                    }

                    creds.AccessToken = refreshed.AccessToken;
                    creds.RefreshToken = refreshed.RefreshToken;
                    creds.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
                    await botCredentialRepository.SaveAsync(creds).ConfigureAwait(false);
                    _logger.LogInformation("✅ Forge bot token refreshed; expires at {Expiry}", creds.TokenExpiry);
                }

                var credentials = new ConnectionCredentials(creds.Username, creds.AccessToken);
                var clientOptions = new ClientOptions
                {
                    MessagesAllowedInPeriod = 750,
                    ThrottlingPeriod = TimeSpan.FromSeconds(30)
                };

                var client = _botClientFactory.Create(clientOptions);
                client.Initialize(credentials);

                client.OnLog += (s, e) => _logger.LogDebug("Forge Bot: {Data}", (e.Data ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                client.OnConnected += (s, e) => _logger.LogInformation("✅ Forge bot connected as {Username}", (creds.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                client.OnMessageReceived += (s, e) => HandleMessage(e.ChatMessage);

                try
                {
                    if (!client.Connect())
                    {
                        _logger.LogError("❌ Failed to connect Forge bot Twitch client");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to connect Forge bot Twitch client (exception)");
                    return null;
                }

                _botClient = client;
                return client;
            }
            finally
            {
                _botConnectLock.Release();
            }
        }
    }
}
