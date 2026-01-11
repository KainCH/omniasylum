using System;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using Xunit;

namespace OmniForge.Tests.Entities
{
    public class GameChatCommandsTableEntityTests
    {
        [Fact]
        public void ToConfiguration_WhenEmptyJson_ShouldReturnDefault()
        {
            var entity = new GameChatCommandsTableEntity
            {
                commandsConfig = ""
            };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.Empty(config.Commands);
            Assert.Equal(1, config.MaxIncrementAmount);
        }

        [Fact]
        public void ToConfiguration_WhenInvalidJson_ShouldReturnDefault()
        {
            var entity = new GameChatCommandsTableEntity
            {
                commandsConfig = "{not valid json]"
            };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.Empty(config.Commands);
            Assert.Equal(1, config.MaxIncrementAmount);
        }

        [Fact]
        public void FromConfiguration_ThenToConfiguration_ShouldRoundTrip()
        {
            var config = new ChatCommandConfiguration
            {
                MaxIncrementAmount = 3
            };

            config.Commands["!hello"] = new ChatCommandDefinition
            {
                Response = "hi",
                Permission = "moderator",
                Enabled = true,
                Cooldown = 5,
                Custom = true
            };

            var entity = GameChatCommandsTableEntity.FromConfiguration("u1", "g1", config);

            Assert.Equal("u1", entity.PartitionKey);
            Assert.Equal("g1", entity.RowKey);
            Assert.False(string.IsNullOrWhiteSpace(entity.commandsConfig));
            Assert.NotEqual(default, entity.LastUpdated);

            var roundTrip = entity.ToConfiguration();
            Assert.Equal(3, roundTrip.MaxIncrementAmount);
            Assert.True(roundTrip.Commands.ContainsKey("!hello"));
            Assert.Equal("hi", roundTrip.Commands["!hello"].Response);
            Assert.Equal("moderator", roundTrip.Commands["!hello"].Permission);
        }
    }
}
