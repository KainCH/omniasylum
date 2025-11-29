using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchClientManager : ITwitchClientManager
    {
        private readonly ConcurrentDictionary<string, TwitchClient> _clients = new ConcurrentDictionary<string, TwitchClient>();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITwitchMessageHandler _messageHandler;
        private readonly ILogger<TwitchClientManager> _logger;

        public TwitchClientManager(
            IServiceScopeFactory scopeFactory,
            ITwitchMessageHandler messageHandler,
            ILogger<TwitchClientManager> logger)
        {
            _scopeFactory = scopeFactory;
            _messageHandler = messageHandler;
            _logger = logger;
        }

        public async Task ConnectUserAsync(string userId)
        {
            if (_clients.ContainsKey(userId))
            {
                return;
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var authService = scope.ServiceProvider.GetRequiredService<ITwitchAuthService>();
                var user = await userRepository.GetUserAsync(userId);

                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", LogSanitizer.Sanitize(userId));
                    return;
                }

                // Check if token needs refresh (buffer of 5 minutes)
                if (user.TokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    _logger.LogInformation("🔄 Refreshing expired token for user {UserId} before IRC connect", LogSanitizer.Sanitize(userId));
                    var newToken = await authService.RefreshTokenAsync(user.RefreshToken);
                    if (newToken != null)
                    {
                        user.AccessToken = newToken.AccessToken;
                        user.RefreshToken = newToken.RefreshToken;
                        user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);
                        await userRepository.SaveUserAsync(user);
                        _logger.LogInformation("✅ Token refreshed for user {UserId}, expires at {Expiry}", LogSanitizer.Sanitize(userId), user.TokenExpiry);
                    }
                    else
                    {
                        _logger.LogError("❌ Failed to refresh token for user {UserId} - cannot connect to IRC", LogSanitizer.Sanitize(userId));
                        return;
                    }
                }

                var credentials = new ConnectionCredentials(user.Username, user.AccessToken);
                var clientOptions = new ClientOptions
                {
                    MessagesAllowedInPeriod = 750,
                    ThrottlingPeriod = TimeSpan.FromSeconds(30)
                };
                var customClient = new WebSocketClient(clientOptions);
                var client = new TwitchClient(customClient);

                client.Initialize(credentials, user.Username);

                client.OnLog += (s, e) => _logger.LogDebug("Twitch Client {UserId}: {Data}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(e.Data));
                client.OnConnected += (s, e) => _logger.LogInformation("Twitch Client {UserId} connected", LogSanitizer.Sanitize(userId));
                client.OnMessageReceived += (s, e) => HandleMessage(userId, e.ChatMessage);

                if (client.Connect())
                {
                    _clients.TryAdd(userId, client);
                }
                else
                {
                    _logger.LogError("Failed to connect Twitch Client for {UserId}", LogSanitizer.Sanitize(userId));
                }
            }
        }

        public Task DisconnectUserAsync(string userId)
        {
            if (_clients.TryRemove(userId, out var client))
            {
                client.Disconnect();
            }
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(string userId, string message)
        {
            if (_clients.TryGetValue(userId, out var client))
            {
                // Assuming sending to own channel
                client.SendMessage(client.TwitchUsername, message);
            }
            return Task.CompletedTask;
        }

        public BotStatus GetUserBotStatus(string userId)
        {
            bool connected = _clients.ContainsKey(userId) && _clients[userId].IsConnected;
            return new BotStatus
            {
                Connected = connected,
                Reason = connected ? "Connected" : "Not connected"
            };
        }

        private async void HandleMessage(string userId, TwitchLib.Client.Models.ChatMessage chatMessage)
        {
            await _messageHandler.HandleMessageAsync(userId, chatMessage, SendMessageAsync);
        }
    }
}
