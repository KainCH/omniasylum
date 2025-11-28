using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Sends Discord invite links in Twitch chat with throttling.
    /// </summary>
    public class DiscordInviteSender : IDiscordInviteSender
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DiscordInviteSender> _logger;
        private readonly TwitchSettings _twitchSettings;
        private readonly IDiscordNotificationTracker _notificationTracker;
        private readonly TimeSpan _throttleDuration = TimeSpan.FromMinutes(5);

        public DiscordInviteSender(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            ILogger<DiscordInviteSender> logger,
            IOptions<TwitchSettings> twitchSettings,
            IDiscordNotificationTracker notificationTracker)
        {
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _twitchSettings = twitchSettings.Value;
            _notificationTracker = notificationTracker;
        }

        public async Task SendDiscordInviteAsync(string broadcasterId)
        {
            // Check throttle
            var lastNotification = _notificationTracker.GetLastNotification(broadcasterId);
            if (lastNotification.HasValue)
            {
                if (DateTimeOffset.UtcNow - lastNotification.Value.Time < _throttleDuration)
                {
                    return; // Throttled
                }
            }

            using var scope = _scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await userRepository.GetUserAsync(broadcasterId);

            if (user == null || string.IsNullOrEmpty(user.AccessToken))
            {
                return;
            }

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
                    message
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to send chat message. Status: {StatusCode}, Error: {Error}",
                        response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to send chat message: {response.StatusCode}");
                }

                // Update tracker
                _notificationTracker.RecordNotification(broadcasterId, true);
                _logger.LogInformation("Sent Discord invite to channel {BroadcasterId}", broadcasterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Discord invite.");
                _notificationTracker.RecordNotification(broadcasterId, false);
            }
        }
    }
}
