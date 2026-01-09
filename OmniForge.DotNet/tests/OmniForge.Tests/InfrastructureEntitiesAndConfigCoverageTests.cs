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
    }
}
