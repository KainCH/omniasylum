using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
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
                var user = await userRepository.GetUserAsync(userId);

                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", userId);
                    return;
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

                client.OnLog += (s, e) => _logger.LogDebug("Twitch Client {UserId}: {Data}", userId, e.Data);
                client.OnConnected += (s, e) => _logger.LogInformation("Twitch Client {UserId} connected", userId);
                client.OnMessageReceived += (s, e) => HandleMessage(userId, e.ChatMessage);

                if (client.Connect())
                {
                    _clients.TryAdd(userId, client);
                }
                else
                {
                    _logger.LogError("Failed to connect Twitch Client for {UserId}", userId);
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
