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

        public string TwitchUserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset TokenExpiry { get; set; }
        public string Role { get; set; } = "streamer";
        public string Features { get; set; } = "{}";
        public string OverlaySettings { get; set; } = "{}";
        public string DiscordSettings { get; set; } = "{}";
        public string DiscordWebhookUrl { get; set; } = string.Empty;
        public string DiscordInviteLink { get; set; } = string.Empty;
        public string ManagedStreamers { get; set; } = "[]";
        public bool IsActive { get; set; } = true;
        public string StreamStatus { get; set; } = "offline";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastLogin { get; set; }

        public User ToDomain()
        {
            return new User
            {
                TwitchUserId = TwitchUserId,
                Username = Username,
                DisplayName = DisplayName,
                Email = Email,
                ProfileImageUrl = ProfileImageUrl,
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                TokenExpiry = TokenExpiry,
                Role = Role,
                Features = JsonSerializer.Deserialize<FeatureFlags>(Features) ?? new FeatureFlags(),
                OverlaySettings = JsonSerializer.Deserialize<OverlaySettings>(OverlaySettings) ?? new OverlaySettings(),
                DiscordSettings = JsonSerializer.Deserialize<DiscordSettings>(DiscordSettings) ?? new DiscordSettings(),
                DiscordWebhookUrl = DiscordWebhookUrl,
                DiscordInviteLink = DiscordInviteLink,
                ManagedStreamers = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(ManagedStreamers) ?? new System.Collections.Generic.List<string>(),
                IsActive = IsActive,
                StreamStatus = StreamStatus,
                CreatedAt = CreatedAt,
                LastLogin = LastLogin
            };
        }

        public static UserTableEntity FromDomain(User user)
        {
            return new UserTableEntity
            {
                PartitionKey = "user",
                RowKey = user.TwitchUserId,
                TwitchUserId = user.TwitchUserId,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Email = user.Email,
                ProfileImageUrl = user.ProfileImageUrl,
                AccessToken = user.AccessToken,
                RefreshToken = user.RefreshToken,
                TokenExpiry = user.TokenExpiry,
                Role = user.Role,
                Features = JsonSerializer.Serialize(user.Features),
                OverlaySettings = JsonSerializer.Serialize(user.OverlaySettings),
                DiscordSettings = JsonSerializer.Serialize(user.DiscordSettings),
                DiscordWebhookUrl = user.DiscordWebhookUrl,
                DiscordInviteLink = user.DiscordInviteLink,
                ManagedStreamers = JsonSerializer.Serialize(user.ManagedStreamers),
                IsActive = user.IsActive,
                StreamStatus = user.StreamStatus,
                CreatedAt = user.CreatedAt,
                LastLogin = user.LastLogin
            };
        }
    }
}
