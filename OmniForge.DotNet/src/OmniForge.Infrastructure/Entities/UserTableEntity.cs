using System;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;
using System.Text.Json;

namespace OmniForge.Infrastructure.Entities
{
    public class UserTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "user";
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string twitchUserId { get; set; } = string.Empty;
        public string username { get; set; } = string.Empty;
        public string displayName { get; set; } = string.Empty;
        public string email { get; set; } = string.Empty;
        public string profileImageUrl { get; set; } = string.Empty;
        public string accessToken { get; set; } = string.Empty;
        public string refreshToken { get; set; } = string.Empty;
        public object? tokenExpiry { get; set; }
        public string role { get; set; } = "streamer";
        public string features { get; set; } = "{}";
        public string overlaySettings { get; set; } = "{}";
        public string discordSettings { get; set; } = "{}";
        public string discordWebhookUrl { get; set; } = string.Empty;
        public string discordInviteLink { get; set; } = string.Empty;
        public string managedStreamers { get; set; } = "[]";
        public bool isActive { get; set; } = true;
        public string streamStatus { get; set; } = "offline";
        public object? createdAt { get; set; }
        public object? lastLogin { get; set; }

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private T DeserializeSafe<T>(string json) where T : new()
        {
            if (string.IsNullOrEmpty(json)) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        private DateTimeOffset ParseDateTimeOffset(object? value)
        {
            if (value is DateTimeOffset dto) return dto;
            if (value is DateTime dt) return new DateTimeOffset(dt);
            if (value is string s && DateTimeOffset.TryParse(s, out var result)) return result;
            return default;
        }

        public User ToDomain()
        {
            var user = new User
            {
                TwitchUserId = twitchUserId,
                Username = username,
                DisplayName = displayName,
                Email = email,
                ProfileImageUrl = profileImageUrl,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenExpiry = ParseDateTimeOffset(tokenExpiry),
                Role = role,
                Features = DeserializeSafe<FeatureFlags>(features),
                OverlaySettings = DeserializeSafe<OverlaySettings>(overlaySettings),
                DiscordSettings = DeserializeSafe<DiscordSettings>(discordSettings),
                DiscordWebhookUrl = discordWebhookUrl,
                DiscordInviteLink = discordInviteLink,
                ManagedStreamers = DeserializeSafe<System.Collections.Generic.List<string>>(managedStreamers),
                IsActive = isActive,
                StreamStatus = streamStatus,
                CreatedAt = ParseDateTimeOffset(createdAt),
                LastLogin = ParseDateTimeOffset(lastLogin)
            };

            // Ensure nested properties are not null (deserialization might leave them null if JSON has explicit nulls)
            if (user.OverlaySettings.Counters == null) user.OverlaySettings.Counters = new OverlayCounters();
            if (user.OverlaySettings.Theme == null) user.OverlaySettings.Theme = new OverlayTheme();
            if (user.OverlaySettings.Animations == null) user.OverlaySettings.Animations = new OverlayAnimations();
            if (user.OverlaySettings.BitsGoal == null) user.OverlaySettings.BitsGoal = new BitsGoal();
            if (user.OverlaySettings.Position == null) user.OverlaySettings.Position = "top-right";

            if (user.DiscordSettings.EnabledNotifications == null) user.DiscordSettings.EnabledNotifications = new DiscordEnabledNotifications();
            if (user.DiscordSettings.MilestoneThresholds == null) user.DiscordSettings.MilestoneThresholds = new DiscordMilestoneThresholds();

            return user;
        }

        public static UserTableEntity FromDomain(User user)
        {
            return new UserTableEntity
            {
                PartitionKey = "user",
                RowKey = user.TwitchUserId,
                twitchUserId = user.TwitchUserId,
                username = user.Username,
                displayName = user.DisplayName,
                email = user.Email,
                profileImageUrl = user.ProfileImageUrl,
                accessToken = user.AccessToken,
                refreshToken = user.RefreshToken,
                tokenExpiry = user.TokenExpiry,
                role = user.Role,
                features = JsonSerializer.Serialize(user.Features),
                overlaySettings = JsonSerializer.Serialize(user.OverlaySettings),
                discordSettings = JsonSerializer.Serialize(user.DiscordSettings),
                discordWebhookUrl = user.DiscordWebhookUrl,
                discordInviteLink = user.DiscordInviteLink,
                managedStreamers = JsonSerializer.Serialize(user.ManagedStreamers),
                isActive = user.IsActive,
                streamStatus = user.StreamStatus,
                createdAt = user.CreatedAt,
                lastLogin = user.LastLogin
            };
        }
    }
}
