using System;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests
{
    public class BotCredentialsTableEntityTests
    {
        [Fact]
        public void Defaults_ShouldUseSystemForgeBotKeys()
        {
            var entity = new BotCredentialsTableEntity();

            Assert.Equal(BotCredentialsTableEntity.Partition, entity.PartitionKey);
            Assert.Equal(BotCredentialsTableEntity.Row, entity.RowKey);
            Assert.Equal(string.Empty, entity.Username);
            Assert.Equal(string.Empty, entity.AccessToken);
            Assert.Equal(string.Empty, entity.RefreshToken);
            Assert.Equal(DateTimeOffset.MinValue, entity.TokenExpiry);
        }

        [Fact]
        public void CanSetProperties_ShouldRoundTripValues()
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new BotCredentialsTableEntity
            {
                PartitionKey = "p",
                RowKey = "r",
                Username = "bot",
                AccessToken = "access",
                RefreshToken = "refresh",
                TokenExpiry = now
            };

            Assert.Equal("p", entity.PartitionKey);
            Assert.Equal("r", entity.RowKey);
            Assert.Equal("bot", entity.Username);
            Assert.Equal("access", entity.AccessToken);
            Assert.Equal("refresh", entity.RefreshToken);
            Assert.Equal(now, entity.TokenExpiry);
        }
    }
}
