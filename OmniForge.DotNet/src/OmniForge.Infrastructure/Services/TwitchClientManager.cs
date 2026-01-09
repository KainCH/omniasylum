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
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using OmniForge.Infrastructure.Configuration;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchClientManager : ITwitchClientManager
    {
        private readonly ConcurrentDictionary<string, string> _userIdToChannel = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _channelToUserId = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly SemaphoreSlim _botConnectLock = new SemaphoreSlim(1, 1);
        private TwitchClient? _botClient;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITwitchMessageHandler _messageHandler;
        private readonly TwitchSettings _twitchSettings;
        private readonly ILogger<TwitchClientManager> _logger;

        public TwitchClientManager(
            IServiceScopeFactory scopeFactory,
            ITwitchMessageHandler messageHandler,
            IOptions<TwitchSettings> twitchSettings,
            ILogger<TwitchClientManager> logger)
        {
            _scopeFactory = scopeFactory;
            _messageHandler = messageHandler;
            _twitchSettings = twitchSettings.Value;
            _logger = logger;
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
                    _logger.LogWarning("User {UserId} not found", LogSanitizer.Sanitize(userId));
                    return;
                }

                var channelLogin = (user.Username ?? string.Empty).Trim().TrimStart('#');
                if (string.IsNullOrWhiteSpace(channelLogin))
                {
                    _logger.LogWarning("User {UserId} has no username/channel to join", LogSanitizer.Sanitize(userId));
                    return;
                }

                var botClient = await EnsureBotConnectedAsync(botCredentialRepository, authService).ConfigureAwait(false);
                if (botClient == null)
                {
                    _logger.LogError("❌ Forge bot is not configured/connected. Cannot join channel for user {UserId}", LogSanitizer.Sanitize(userId));
                    return;
                }

                // Join channel and track mappings for message routing.
                botClient.JoinChannel(channelLogin);
                _userIdToChannel.TryAdd(userId, channelLogin);
                _channelToUserId.AddOrUpdate(channelLogin.ToLowerInvariant(), userId, (_, __) => userId);
                _logger.LogInformation("✅ Forge bot joined channel {Channel} for user {UserId}", LogSanitizer.Sanitize(channelLogin), LogSanitizer.Sanitize(userId));
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
                if (string.IsNullOrEmpty(channel))
                {
                    return;
                }

                if (!_channelToUserId.TryGetValue(channel, out var userId))
                {
                    return;
                }

                await _messageHandler.HandleMessageAsync(userId, chatMessage, SendMessageAsync).ConfigureAwait(false);
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

        private async Task<TwitchClient?> EnsureBotConnectedAsync(
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
                        _logger.LogInformation("✅ Seeded Forge bot credentials from configuration for {Username}", LogSanitizer.Sanitize(creds.Username));
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

                var credentials = new ConnectionCredentials(creds.Username, creds.AccessToken);
                var clientOptions = new ClientOptions
                {
                    MessagesAllowedInPeriod = 750,
                    ThrottlingPeriod = TimeSpan.FromSeconds(30)
                };
                var customClient = new WebSocketClient(clientOptions);
                var client = new TwitchClient(customClient);

                client.Initialize(credentials);

                client.OnLog += (s, e) => _logger.LogDebug("Forge Bot: {Data}", LogSanitizer.Sanitize(e.Data));
                client.OnConnected += (s, e) => _logger.LogInformation("✅ Forge bot connected as {Username}", LogSanitizer.Sanitize(creds.Username));
                client.OnMessageReceived += (s, e) => HandleMessage(e.ChatMessage);

                if (!client.Connect())
                {
                    _logger.LogError("❌ Failed to connect Forge bot Twitch client");
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
