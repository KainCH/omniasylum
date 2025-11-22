using System;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests
{
    public class TableEntityTests
    {
        [Fact]
        public void UserTableEntity_ToDomain_ShouldMapCorrectly()
        {
            // Arrange
            var entity = new UserTableEntity
            {
                TwitchUserId = "123",
                Username = "testuser",
                DisplayName = "Test User",
                Email = "test@example.com",
                ProfileImageUrl = "http://image.url",
                AccessToken = "access",
                RefreshToken = "refresh",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
                Role = "admin",
                Features = "{\"ChatCommands\":true}",
                OverlaySettings = "{\"Theme\":{\"BackgroundColor\":\"black\"}}",
                DiscordWebhookUrl = "http://discord.url",
                DiscordInviteLink = "http://discord.gg",
                IsActive = true,
                StreamStatus = "live",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                LastLogin = DateTimeOffset.UtcNow
            };

            // Act
            var domain = entity.ToDomain();

            // Assert
            Assert.Equal(entity.TwitchUserId, domain.TwitchUserId);
            Assert.Equal(entity.Username, domain.Username);
            Assert.Equal(entity.DisplayName, domain.DisplayName);
            Assert.Equal(entity.Email, domain.Email);
            Assert.Equal(entity.ProfileImageUrl, domain.ProfileImageUrl);
            Assert.Equal(entity.AccessToken, domain.AccessToken);
            Assert.Equal(entity.RefreshToken, domain.RefreshToken);
            Assert.Equal(entity.TokenExpiry, domain.TokenExpiry);
            Assert.Equal(entity.Role, domain.Role);
            Assert.True(domain.Features.ChatCommands);
            Assert.Equal("black", domain.OverlaySettings.Theme.BackgroundColor);
            Assert.Equal(entity.DiscordWebhookUrl, domain.DiscordWebhookUrl);
            Assert.Equal(entity.DiscordInviteLink, domain.DiscordInviteLink);
            Assert.Equal(entity.IsActive, domain.IsActive);
            Assert.Equal(entity.StreamStatus, domain.StreamStatus);
            Assert.Equal(entity.CreatedAt, domain.CreatedAt);
            Assert.Equal(entity.LastLogin, domain.LastLogin);
        }

        [Fact]
        public void UserTableEntity_FromDomain_ShouldMapCorrectly()
        {
            // Arrange
            var domain = new User
            {
                TwitchUserId = "123",
                Username = "testuser",
                Features = new FeatureFlags { ChatCommands = true },
                OverlaySettings = new OverlaySettings { Theme = new OverlayTheme { BackgroundColor = "black" } }
            };

            // Act
            var entity = UserTableEntity.FromDomain(domain);

            // Assert
            Assert.Equal("user", entity.PartitionKey);
            Assert.Equal(domain.TwitchUserId, entity.RowKey);
            Assert.Equal(domain.TwitchUserId, entity.TwitchUserId);
            Assert.Equal(domain.Username, entity.Username);
            Assert.Contains("ChatCommands", entity.Features);
            Assert.Contains("black", entity.OverlaySettings);
        }

        [Fact]
        public void CounterTableEntity_ToDomain_ShouldMapCorrectly()
        {
            // Arrange
            var entity = new CounterTableEntity
            {
                PartitionKey = "123",
                Deaths = 5,
                Swears = 10,
                Screams = 2,
                Bits = 100,
                LastUpdated = DateTimeOffset.UtcNow,
                StreamStarted = DateTimeOffset.UtcNow.AddHours(-1),
                LastNotifiedStreamId = "stream1"
            };

            // Act
            var domain = entity.ToDomain();

            // Assert
            Assert.Equal(entity.PartitionKey, domain.TwitchUserId);
            Assert.Equal(entity.Deaths, domain.Deaths);
            Assert.Equal(entity.Swears, domain.Swears);
            Assert.Equal(entity.Screams, domain.Screams);
            Assert.Equal(entity.Bits, domain.Bits);
            Assert.Equal(entity.LastUpdated, domain.LastUpdated);
            Assert.Equal(entity.StreamStarted, domain.StreamStarted);
            Assert.Equal(entity.LastNotifiedStreamId, domain.LastNotifiedStreamId);
        }

        [Fact]
        public void CounterTableEntity_FromDomain_ShouldMapCorrectly()
        {
            // Arrange
            var domain = new Counter
            {
                TwitchUserId = "123",
                Deaths = 5,
                Swears = 10,
                Screams = 2,
                Bits = 100,
                LastUpdated = DateTimeOffset.UtcNow,
                StreamStarted = DateTimeOffset.UtcNow.AddHours(-1),
                LastNotifiedStreamId = "stream1"
            };

            // Act
            var entity = CounterTableEntity.FromDomain(domain);

            // Assert
            Assert.Equal(domain.TwitchUserId, entity.PartitionKey);
            Assert.Equal("counters", entity.RowKey);
            Assert.Equal(domain.Deaths, entity.Deaths);
            Assert.Equal(domain.Swears, entity.Swears);
            Assert.Equal(domain.Screams, entity.Screams);
            Assert.Equal(domain.Bits, entity.Bits);
            Assert.Equal(domain.LastUpdated, entity.LastUpdated);
            Assert.Equal(domain.StreamStarted, entity.StreamStarted);
            Assert.Equal(domain.LastNotifiedStreamId, entity.LastNotifiedStreamId);
        }
    }
}
