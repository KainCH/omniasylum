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
        public DateTimeOffset tokenExpiry { get; set; }
        public string role { get; set; } = "streamer";
        public string features { get; set; } = "{}";
        public string overlaySettings { get; set; } = "{}";
        public string discordSettings { get; set; } = "{}";
        public string discordWebhookUrl { get; set; } = string.Empty;
        public string discordInviteLink { get; set; } = string.Empty;
        public string managedStreamers { get; set; } = "[]";
        public bool isActive { get; set; } = true;
        public string streamStatus { get; set; } = "offline";
        public DateTimeOffset createdAt { get; set; }
        public DateTimeOffset lastLogin { get; set; }

        public User ToDomain()
        {
            return new User
            {
                TwitchUserId = twitchUserId,
                Username = username,
                DisplayName = displayName,
                Email = email,
                ProfileImageUrl = profileImageUrl,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenExpiry = tokenExpiry,
                Role = role,
                Features = JsonSerializer.Deserialize<FeatureFlags>(features) ?? new FeatureFlags(),
                OverlaySettings = JsonSerializer.Deserialize<OverlaySettings>(overlaySettings) ?? new OverlaySettings(),
                DiscordSettings = JsonSerializer.Deserialize<DiscordSettings>(discordSettings) ?? new DiscordSettings(),
                DiscordWebhookUrl = discordWebhookUrl,
                DiscordInviteLink = discordInviteLink,
                ManagedStreamers = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(managedStreamers) ?? new System.Collections.Generic.List<string>(),
                IsActive = isActive,
                StreamStatus = streamStatus,
                CreatedAt = createdAt,
                LastLogin = lastLogin
            };
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
