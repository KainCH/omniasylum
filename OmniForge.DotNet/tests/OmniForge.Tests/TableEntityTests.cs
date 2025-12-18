using System;
using System.Collections.Generic;
using System.Text.Json;
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

        #region ChatCommandConfigTableEntity Tests

        [Fact]
        public void ChatCommandConfigTableEntity_DefaultValues_ShouldBeCorrect()
        {
            var entity = new ChatCommandConfigTableEntity();

            Assert.Equal(string.Empty, entity.PartitionKey);
            Assert.Equal("chatCommands", entity.RowKey);
            Assert.Equal("{}", entity.commandsConfig);
        }

        [Fact]
        public void ChatCommandConfigTableEntity_ToConfiguration_ShouldReturnEmptyConfig_WhenCommandsConfigIsEmpty()
        {
            var entity = new ChatCommandConfigTableEntity { commandsConfig = string.Empty };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.NotNull(config.Commands);
        }

        [Fact]
        public void ChatCommandConfigTableEntity_ToConfiguration_ShouldReturnEmptyConfig_WhenCommandsConfigIsNull()
        {
            var entity = new ChatCommandConfigTableEntity { commandsConfig = null! };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
        }

        [Fact]
        public void ChatCommandConfigTableEntity_ToConfiguration_ShouldDeserializeValidJson()
        {
            var originalConfig = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!test", new ChatCommandDefinition { Response = "Test Response", Permission = "everyone", Cooldown = 5, Enabled = true } }
                }
            };
            var entity = new ChatCommandConfigTableEntity { commandsConfig = JsonSerializer.Serialize(originalConfig) };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.Contains("!test", config.Commands.Keys);
            Assert.Equal("Test Response", config.Commands["!test"].Response);
            Assert.Equal("everyone", config.Commands["!test"].Permission);
            Assert.Equal(5, config.Commands["!test"].Cooldown);
            Assert.True(config.Commands["!test"].Enabled);
        }

        [Fact]
        public void ChatCommandConfigTableEntity_ToConfiguration_ShouldReturnDefaultOnInvalidJson()
        {
            var entity = new ChatCommandConfigTableEntity { commandsConfig = "invalid json {{{" };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.NotNull(config.Commands);
        }

        [Fact]
        public void ChatCommandConfigTableEntity_FromConfiguration_ShouldCreateCorrectEntity()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!hello", new ChatCommandDefinition { Response = "Hello!", Permission = "everyone" } }
                }
            };

            var entity = ChatCommandConfigTableEntity.FromConfiguration("user123", config);

            Assert.Equal("user123", entity.PartitionKey);
            Assert.Equal("chatCommands", entity.RowKey);
            Assert.Contains("!hello", entity.commandsConfig);
            Assert.Contains("Hello!", entity.commandsConfig);
            Assert.True(entity.LastUpdated <= DateTimeOffset.UtcNow);
            Assert.True(entity.LastUpdated > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        [Fact]
        public void ChatCommandConfigTableEntity_RoundTrip_ShouldPreserveData()
        {
            var originalConfig = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!deaths", new ChatCommandDefinition { Response = "Deaths: {{deaths}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
                    { "!death+", new ChatCommandDefinition { Action = "increment", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } }
                }
            };

            var entity = ChatCommandConfigTableEntity.FromConfiguration("user123", originalConfig);
            var restoredConfig = entity.ToConfiguration();

            Assert.Equal(2, restoredConfig.Commands.Count);
            Assert.Contains("!deaths", restoredConfig.Commands.Keys);
            Assert.Contains("!death+", restoredConfig.Commands.Keys);
            Assert.Equal("Deaths: {{deaths}}", restoredConfig.Commands["!deaths"].Response);
            Assert.Equal("increment", restoredConfig.Commands["!death+"].Action);
        }

        #endregion

        #region CustomCounterConfigTableEntity Tests

        [Fact]
        public void CustomCounterConfigTableEntity_DefaultValues_ShouldBeCorrect()
        {
            var entity = new CustomCounterConfigTableEntity();

            Assert.Equal(string.Empty, entity.PartitionKey);
            Assert.Equal("customCounters", entity.RowKey);
            Assert.Equal("{}", entity.countersConfig);
        }

        [Fact]
        public void CustomCounterConfigTableEntity_ToConfiguration_ShouldReturnEmptyConfig_WhenCountersConfigIsEmpty()
        {
            var entity = new CustomCounterConfigTableEntity { countersConfig = string.Empty };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.NotNull(config.Counters);
        }

        [Fact]
        public void CustomCounterConfigTableEntity_ToConfiguration_ShouldReturnEmptyConfig_WhenCountersConfigIsNull()
        {
            var entity = new CustomCounterConfigTableEntity { countersConfig = null! };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
        }

        [Fact]
        public void CustomCounterConfigTableEntity_ToConfiguration_ShouldDeserializeValidJson()
        {
            var originalConfig = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "kills", new CustomCounterDefinition { Name = "Kills", Icon = "crosshair", IncrementBy = 1 } }
                }
            };
            var entity = new CustomCounterConfigTableEntity { countersConfig = JsonSerializer.Serialize(originalConfig) };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.Contains("kills", config.Counters.Keys);
            Assert.Equal("Kills", config.Counters["kills"].Name);
            Assert.Equal("crosshair", config.Counters["kills"].Icon);
            Assert.Equal(1, config.Counters["kills"].IncrementBy);
        }

        [Fact]
        public void CustomCounterConfigTableEntity_ToConfiguration_ShouldReturnDefaultOnInvalidJson()
        {
            var entity = new CustomCounterConfigTableEntity { countersConfig = "invalid json {{{" };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.NotNull(config.Counters);
        }

        [Fact]
        public void CustomCounterConfigTableEntity_FromConfiguration_ShouldCreateCorrectEntity()
        {
            var config = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "headshots", new CustomCounterDefinition { Name = "Headshots", Icon = "bullseye" } }
                }
            };

            var entity = CustomCounterConfigTableEntity.FromConfiguration("user456", config);

            Assert.Equal("user456", entity.PartitionKey);
            Assert.Equal("customCounters", entity.RowKey);
            Assert.Contains("headshots", entity.countersConfig);
            Assert.Contains("Headshots", entity.countersConfig);
            Assert.True(entity.lastUpdated <= DateTimeOffset.UtcNow);
            Assert.True(entity.lastUpdated > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        [Fact]
        public void CustomCounterConfigTableEntity_RoundTrip_ShouldPreserveData()
        {
            var originalConfig = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "kills", new CustomCounterDefinition { Name = "Kills", Icon = "crosshair", IncrementBy = 1 } },
                    { "assists", new CustomCounterDefinition { Name = "Assists", Icon = "handshake", DecrementBy = 2 } }
                }
            };

            var entity = CustomCounterConfigTableEntity.FromConfiguration("user789", originalConfig);
            var restoredConfig = entity.ToConfiguration();

            Assert.Equal(2, restoredConfig.Counters.Count);
            Assert.Contains("kills", restoredConfig.Counters.Keys);
            Assert.Contains("assists", restoredConfig.Counters.Keys);
            Assert.Equal("Kills", restoredConfig.Counters["kills"].Name);
            Assert.Equal("Assists", restoredConfig.Counters["assists"].Name);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ChatCommandConfigTableEntity_ToConfiguration_EmptyCommands_ShouldWork()
        {
            var config = new ChatCommandConfiguration { Commands = new Dictionary<string, ChatCommandDefinition>() };
            var entity = new ChatCommandConfigTableEntity { commandsConfig = JsonSerializer.Serialize(config) };

            var result = entity.ToConfiguration();

            Assert.NotNull(result);
            Assert.Empty(result.Commands);
        }

        [Fact]
        public void CustomCounterConfigTableEntity_ToConfiguration_EmptyCounters_ShouldWork()
        {
            var config = new CustomCounterConfiguration { Counters = new Dictionary<string, CustomCounterDefinition>() };
            var entity = new CustomCounterConfigTableEntity { countersConfig = JsonSerializer.Serialize(config) };

            var result = entity.ToConfiguration();

            Assert.NotNull(result);
            Assert.Empty(result.Counters);
        }

        [Fact]
        public void ChatCommandConfigTableEntity_ToConfiguration_WithSpecialCharacters_ShouldWork()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!special", new ChatCommandDefinition { Response = "Hello! \"World\" <test> & more", Permission = "everyone" } }
                }
            };
            var entity = ChatCommandConfigTableEntity.FromConfiguration("user", config);
            var result = entity.ToConfiguration();

            Assert.Equal("Hello! \"World\" <test> & more", result.Commands["!special"].Response);
        }

        [Fact]
        public void CustomCounterConfigTableEntity_ToConfiguration_WithUnicodeCharacters_ShouldWork()
        {
            var config = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "emoji", new CustomCounterDefinition { Name = "💀 Deaths 💀", Icon = "skull" } }
                }
            };
            var entity = CustomCounterConfigTableEntity.FromConfiguration("user", config);
            var result = entity.ToConfiguration();

            Assert.Equal("💀 Deaths 💀", result.Counters["emoji"].Name);
        }

        [Fact]
        public void UserTableEntity_ToDomain_ShouldHandleNullFeatures()
        {
            var entity = new UserTableEntity
            {
                twitchUserId = "123",
                username = "testuser",
                features = null!,
                overlaySettings = null!
            };

            var domain = entity.ToDomain();

            Assert.NotNull(domain.Features);
            Assert.NotNull(domain.OverlaySettings);
        }

        [Fact]
        public void UserTableEntity_ToDomain_ShouldHandleEmptyFeatures()
        {
            var entity = new UserTableEntity
            {
                twitchUserId = "123",
                username = "testuser",
                features = "",
                overlaySettings = ""
            };

            var domain = entity.ToDomain();

            Assert.NotNull(domain.Features);
            Assert.NotNull(domain.OverlaySettings);
            // Verify defaults are applied (StreamAlerts should default to true)
            Assert.True(domain.Features.StreamAlerts);
            Assert.True(domain.Features.ChatCommands);
            Assert.True(domain.Features.DiscordNotifications);
        }

        [Fact]
        public void UserTableEntity_ToDomain_ShouldHandleEmptyObjectJsonFeatures()
        {
            var entity = new UserTableEntity
            {
                twitchUserId = "123",
                username = "testuser",
                features = "{}",
                overlaySettings = "{}"
            };

            var domain = entity.ToDomain();

            Assert.NotNull(domain.Features);
            Assert.NotNull(domain.OverlaySettings);
            // Verify defaults are applied even when JSON is {}
            Assert.True(domain.Features.StreamAlerts);
            Assert.True(domain.Features.ChatCommands);
            Assert.True(domain.Features.DiscordNotifications);
        }

        [Fact]
        public void UserTableEntity_ToDomain_ShouldHandleInvalidJsonFeatures()
        {
            var entity = new UserTableEntity
            {
                twitchUserId = "123",
                username = "testuser",
                features = "invalid json {{{",
                overlaySettings = "invalid json {{{"
            };

            var domain = entity.ToDomain();

            Assert.NotNull(domain.Features);
            Assert.NotNull(domain.OverlaySettings);
        }

        [Fact]
        public void CounterTableEntity_ToDomain_ShouldHandleNullStreamStarted()
        {
            var entity = new CounterTableEntity
            {
                PartitionKey = "123",
                deaths = 5,
                streamStarted = null
            };

            var domain = entity.ToDomain();

            Assert.Null(domain.StreamStarted);
        }

        [Fact]
        public void CounterTableEntity_FromDomain_ShouldHandleNullStreamStarted()
        {
            var domain = new Counter
            {
                TwitchUserId = "123",
                Deaths = 5,
                StreamStarted = null
            };

            var entity = CounterTableEntity.FromDomain(domain);

            Assert.Null(entity.streamStarted);
        }

        #endregion
    }
}
