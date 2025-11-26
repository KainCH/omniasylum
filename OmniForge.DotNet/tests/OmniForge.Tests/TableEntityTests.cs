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
                twitchUserId = "123",
                username = "testuser",
                displayName = "Test User",
                email = "test@example.com",
                profileImageUrl = "http://image.url",
                accessToken = "access",
                refreshToken = "refresh",
                tokenExpiry = DateTimeOffset.UtcNow.AddHours(1),
                role = "admin",
                features = "{\"ChatCommands\":true}",
                overlaySettings = "{\"Theme\":{\"BackgroundColor\":\"black\"}}",
                discordWebhookUrl = "http://discord.url",
                discordInviteLink = "http://discord.gg",
                isActive = true,
                streamStatus = "live",
                createdAt = DateTimeOffset.UtcNow.AddDays(-1),
                lastLogin = DateTimeOffset.UtcNow
            };

            // Act
            var domain = entity.ToDomain();

            // Assert
            Assert.Equal(entity.twitchUserId, domain.TwitchUserId);
            Assert.Equal(entity.username, domain.Username);
            Assert.Equal(entity.displayName, domain.DisplayName);
            Assert.Equal(entity.email, domain.Email);
            Assert.Equal(entity.profileImageUrl, domain.ProfileImageUrl);
            Assert.Equal(entity.accessToken, domain.AccessToken);
            Assert.Equal(entity.refreshToken, domain.RefreshToken);
            Assert.Equal(entity.tokenExpiry, domain.TokenExpiry);
            Assert.Equal(entity.role, domain.Role);
            Assert.True(domain.Features.ChatCommands);
            Assert.Equal("black", domain.OverlaySettings.Theme.BackgroundColor);
            Assert.Equal(entity.discordWebhookUrl, domain.DiscordWebhookUrl);
            Assert.Equal(entity.discordInviteLink, domain.DiscordInviteLink);
            Assert.Equal(entity.isActive, domain.IsActive);
            Assert.Equal(entity.streamStatus, domain.StreamStatus);
            Assert.Equal(entity.createdAt, domain.CreatedAt);
            Assert.Equal(entity.lastLogin, domain.LastLogin);
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
            Assert.Equal(domain.TwitchUserId, entity.twitchUserId);
            Assert.Equal(domain.Username, entity.username);
            Assert.Contains("ChatCommands", entity.features);
            Assert.Contains("black", entity.overlaySettings);
        }

        [Fact]
        public void CounterTableEntity_ToDomain_ShouldMapCorrectly()
        {
            // Arrange
            var entity = new CounterTableEntity
            {
                PartitionKey = "123",
                deaths = 5,
                swears = 10,
                screams = 2,
                bits = 100,
                lastUpdated = DateTimeOffset.UtcNow,
                streamStarted = DateTimeOffset.UtcNow.AddHours(-1),
                lastNotifiedStreamId = "stream1"
            };

            // Act
            var domain = entity.ToDomain();

            // Assert
            Assert.Equal(entity.PartitionKey, domain.TwitchUserId);
            Assert.Equal(entity.deaths, domain.Deaths);
            Assert.Equal(entity.swears, domain.Swears);
            Assert.Equal(entity.screams, domain.Screams);
            Assert.Equal(entity.bits, domain.Bits);
            Assert.Equal(entity.lastUpdated, domain.LastUpdated);
            Assert.Equal(entity.streamStarted, domain.StreamStarted);
            Assert.Equal(entity.lastNotifiedStreamId, domain.LastNotifiedStreamId);
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
            Assert.Equal(domain.Deaths, entity.deaths);
            Assert.Equal(domain.Swears, entity.swears);
            Assert.Equal(domain.Screams, entity.screams);
            Assert.Equal(domain.Bits, entity.bits);
            Assert.Equal(domain.LastUpdated, entity.lastUpdated);
            Assert.Equal(domain.StreamStarted, entity.streamStarted);
            Assert.Equal(domain.LastNotifiedStreamId, entity.lastNotifiedStreamId);
        }
    }
}
