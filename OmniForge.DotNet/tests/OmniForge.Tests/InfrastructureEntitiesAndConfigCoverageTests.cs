using System;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests
{
    public class InfrastructureEntitiesAndConfigCoverageTests
    {
        [Fact]
        public void RedisSettings_DefaultsAndSetters_Work()
        {
            var settings = new RedisSettings();
            Assert.Equal(string.Empty, settings.HostName);
            Assert.Equal(string.Empty, settings.KeyNamespace);

            settings.HostName = "example.redis:10000";
            settings.KeyNamespace = "dev";

            Assert.Equal("example.redis:10000", settings.HostName);
            Assert.Equal("dev", settings.KeyNamespace);
        }

        [Fact]
        public void BotCredentialsTableEntity_DefaultKeysAndProperties_Work()
        {
            var entity = new BotCredentialsTableEntity();

            Assert.Equal(BotCredentialsTableEntity.Partition, entity.PartitionKey);
            Assert.Equal(BotCredentialsTableEntity.Row, entity.RowKey);
            Assert.Equal(string.Empty, entity.Username);
            Assert.Equal(string.Empty, entity.AccessToken);
            Assert.Equal(string.Empty, entity.RefreshToken);
            Assert.Equal(DateTimeOffset.MinValue, entity.TokenExpiry);

            entity.Username = "omniforge_bot";
            entity.AccessToken = "access";
            entity.RefreshToken = "refresh";
            entity.TokenExpiry = DateTimeOffset.UtcNow;

            Assert.Equal("omniforge_bot", entity.Username);
            Assert.Equal("access", entity.AccessToken);
            Assert.Equal("refresh", entity.RefreshToken);
        }

        [Fact]
        public void GameCoreCountersConfigTableEntity_DefaultsAndSetters_Work()
        {
            var entity = new GameCoreCountersConfigTableEntity();

            Assert.Equal(string.Empty, entity.PartitionKey);
            Assert.Equal(string.Empty, entity.RowKey);
            Assert.True(entity.DeathsEnabled);
            Assert.True(entity.SwearsEnabled);
            Assert.True(entity.ScreamsEnabled);
            Assert.False(entity.BitsEnabled);
            Assert.NotEqual(default, entity.UpdatedAt);

            var updatedAt = new DateTimeOffset(2026, 1, 24, 12, 0, 0, TimeSpan.Zero);
            entity.PartitionKey = "u1";
            entity.RowKey = "g1";
            entity.DeathsEnabled = false;
            entity.SwearsEnabled = false;
            entity.ScreamsEnabled = false;
            entity.BitsEnabled = true;
            entity.UpdatedAt = updatedAt;

            Assert.Equal("u1", entity.PartitionKey);
            Assert.Equal("g1", entity.RowKey);
            Assert.False(entity.DeathsEnabled);
            Assert.False(entity.SwearsEnabled);
            Assert.False(entity.ScreamsEnabled);
            Assert.True(entity.BitsEnabled);
            Assert.Equal(updatedAt, entity.UpdatedAt);
        }
    }
}
