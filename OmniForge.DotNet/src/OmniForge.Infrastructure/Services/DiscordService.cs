using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class DiscordService : IDiscordService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DiscordService> _logger;

        public DiscordService(HttpClient httpClient, ILogger<DiscordService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task SendTestNotificationAsync(User user)
        {
            if (string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                _logger.LogWarning("Attempted to send test notification but no webhook URL is configured for user {Username}", user.Username);
                return;
            }

            _logger.LogInformation("Sending Discord test notification for {Username}", user.Username);

            var payload = CreateDiscordPayload(
                "Discord Integration Test",
                $"This is a test notification for **{user.DisplayName}**.\n\nIf you see this, your Discord webhook is configured correctly! 🎉",
                user,
                new DiscordEmbedOptions { Color = 0x00FF00 }
            );

            await SendWebhookAsync(user.DiscordWebhookUrl, payload);
        }

        public async Task SendNotificationAsync(User user, string eventType, object data)
        {
            if (string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                return;
            }

            // Check if notification is enabled
            if (!IsNotificationEnabled(user, eventType))
            {
                _logger.LogInformation("Discord notification disabled for {EventType} by user {Username}", eventType, user.Username);
                return;
            }

            string title;
            string? description = null;
            int color;
            var options = new DiscordEmbedOptions();

            // Use dynamic to access properties of the anonymous object or dictionary passed as data
            dynamic eventData = data;

            switch (eventType)
            {
                case "death_milestone":
                    title = $"💀 Death Milestone: {GetProperty(eventData, "count")}";
                    description = $"**{user.DisplayName}** has reached {GetProperty(eventData, "count")} deaths!\n\n📊 **Progress:** {GetProperty(eventData, "previousMilestone") ?? 0} → {GetProperty(eventData, "count")}";
                    color = 0xFF4444; // Red
                    break;

                case "swear_milestone":
                    title = $"🤬 Swear Milestone: {GetProperty(eventData, "count")}";
                    description = $"**{user.DisplayName}** has reached {GetProperty(eventData, "count")} swears!\n\n📊 **Progress:** {GetProperty(eventData, "previousMilestone") ?? 0} → {GetProperty(eventData, "count")}";
                    color = 0xFF8800; // Orange
                    break;

                case "scream_milestone":
                    title = $"😱 Scream Milestone: {GetProperty(eventData, "count")}";
                    description = $"**{user.DisplayName}** has reached {GetProperty(eventData, "count")} screams!\n\n📊 **Progress:** {GetProperty(eventData, "previousMilestone") ?? 0} → {GetProperty(eventData, "count")}";
                    color = 0xFFFF00; // Yellow
                    break;

                case "stream_start":
                    title = $"🔴 {user.DisplayName} is now live on Twitch!";
                    description = null;
                    color = 0x00FF00; // Green

                    var streamTitle = !string.IsNullOrEmpty((string?)GetProperty(eventData, "title")) ? (string?)GetProperty(eventData, "title") : "Stream Title Not Set";
                    var gameName = !string.IsNullOrEmpty((string?)GetProperty(eventData, "game")) ? (string?)GetProperty(eventData, "game") : "Unknown Category";

                    options.Fields = new List<DiscordField>
                    {
                        new DiscordField { Name = "📺 Title", Value = streamTitle!, Inline = false },
                        new DiscordField { Name = "🎮 Streaming", Value = gameName!, Inline = true }
                    };

                    var thumbnailUrl = (string?)GetProperty(eventData, "thumbnailUrl");
                    if (!string.IsNullOrEmpty(thumbnailUrl))
                    {
                        options.ImageUrl = thumbnailUrl;
                    }
                    else
                    {
                        options.ImageUrl = $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{user.Username.ToLower()}-640x360.jpg?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    }

                    options.Url = $"https://twitch.tv/{user.Username}";
                    options.Buttons = new List<DiscordButton>
                    {
                        new DiscordButton
                        {
                            Label = "🎮 **READY TO WATCH? CLICK HERE!**",
                            Url = $"https://twitch.tv/{user.Username}",
                            Style = 5
                        }
                    };
                    break;

                case "stream_end":
                    title = "🔴 Stream Ended";
                    description = $"**{user.DisplayName}** has ended the stream.\n\n⏱️ **Duration:** {GetProperty(eventData, "duration") ?? "Unknown"}\n💙 **Thanks for watching!**";
                    color = 0xFF4444; // Red
                    break;

                default:
                    title = (string?)GetProperty(eventData, "title") ?? "📢 OmniForge Notification";
                    description = (string?)GetProperty(eventData, "description") ?? $"Event: {eventType}";
                    color = (int?)GetProperty(eventData, "color") ?? 0x5865F2; // Discord blurple
                    break;
            }

            options.Color = color;
            var payload = CreateDiscordPayload(title, description, user, options);

            var webhookUrl = user.DiscordWebhookUrl;
            if (options.Buttons != null && options.Buttons.Count > 0)
            {
                webhookUrl += "?with_components=true";
            }

            await SendWebhookAsync(webhookUrl, payload);
        }

        private object? GetProperty(object obj, string propertyName)
        {
            if (obj == null) return null;

            // Handle Dictionary
            if (obj is IDictionary<string, object> dict)
            {
                return dict.ContainsKey(propertyName) ? dict[propertyName] : null;
            }

            // Handle JsonElement (if passed from deserialized JSON)
            if (obj is JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty(propertyName, out var value))
                {
                    return value.ToString();
                }
                return null;
            }

            // Handle anonymous types or regular objects via reflection
            var prop = obj.GetType().GetProperty(propertyName);
            return prop?.GetValue(obj);
        }

        private bool IsNotificationEnabled(User user, string eventType)
        {
            if (user.DiscordSettings?.EnabledNotifications == null) return true; // Default to true if settings missing

            return eventType switch
            {
                "death_milestone" => user.DiscordSettings.EnabledNotifications.DeathMilestone,
                "swear_milestone" => user.DiscordSettings.EnabledNotifications.SwearMilestone,
                "scream_milestone" => user.DiscordSettings.EnabledNotifications.ScreamMilestone,
                "stream_start" => user.DiscordSettings.EnabledNotifications.StreamStart,
                "stream_end" => user.DiscordSettings.EnabledNotifications.StreamEnd,
                "follower_goal" => user.DiscordSettings.EnabledNotifications.FollowerGoal,
                "subscriber_milestone" => user.DiscordSettings.EnabledNotifications.SubscriberMilestone,
                "channel_point_redemption" => user.DiscordSettings.EnabledNotifications.ChannelPointRedemption,
                _ => false
            };
        }

        private async Task SendWebhookAsync(string url, object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending Discord webhook to {Url}", url);

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Discord webhook failed with {StatusCode}: {ErrorText}", response.StatusCode, errorText);
                    throw new HttpRequestException($"Discord webhook failed: {response.StatusCode} {errorText}");
                }

                _logger.LogInformation("Discord notification sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Discord notification");
                throw;
            }
        }

        private object CreateDiscordPayload(string title, string? description, User user, DiscordEmbedOptions options)
        {
            var embed = new
            {
                title = title,
                description = description,
                color = options.Color,
                url = options.Url,
                thumbnail = new { url = user.ProfileImageUrl },
                footer = new { text = "OmniForge Stream Tools" },
                timestamp = DateTime.UtcNow.ToString("o"),
                fields = options.Fields,
                image = !string.IsNullOrEmpty(options.ImageUrl) ? new { url = options.ImageUrl } : null
            };

            var payload = new
            {
                username = "OmniForge",
                avatar_url = options.BotAvatar ?? user.ProfileImageUrl,
                embeds = new[] { embed },
                components = options.Buttons != null && options.Buttons.Count > 0 ? new[]
                {
                    new
                    {
                        type = 1, // Action Row
                        components = options.Buttons
                    }
                } : null
            };

            return payload;
        }

        private class DiscordEmbedOptions
        {
            public string? BotAvatar { get; set; }
            public int Color { get; set; } = 0x5865F2;
            public string? Url { get; set; }
            public string? ImageUrl { get; set; }
            public List<DiscordField>? Fields { get; set; }
            public List<DiscordButton>? Buttons { get; set; }
        }

        private class DiscordField
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public bool Inline { get; set; }
        }

        private class DiscordButton
        {
            public int Type { get; } = 2; // Button
            public int Style { get; set; } = 5; // Link
            public string Label { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }
    }
}
