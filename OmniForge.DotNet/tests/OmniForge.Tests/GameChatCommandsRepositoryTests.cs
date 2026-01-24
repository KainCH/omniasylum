using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class GameChatCommandsRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly GameChatCommandsRepository _repository;

        public GameChatCommandsRepositoryTests()
        {
            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient
                .Setup(x => x.GetTableClient("gamechatcommands"))
                .Returns(_mockTableClient.Object);

            _repository = new GameChatCommandsRepository(_mockServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task InitializeAsync_ShouldCreateTableIfNotExists()
        {
            await _repository.InitializeAsync();
            _mockTableClient.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnNull_WhenNotFound()
        {
            _mockTableClient
                .Setup(x => x.GetEntityAsync<GameChatCommandsTableEntity>(
                    "u1",
                    "g1",
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetAsync("u1", "g1");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ShouldDeserializeCommandsConfig_WhenFound()
        {
            var config = new ChatCommandConfiguration();
            config.Commands.Add("!d+", new ChatCommandDefinition
            {
                Action = "increment",
                Counter = "deaths",
                Permission = "moderator"
            });

            var entity = GameChatCommandsTableEntity.FromConfiguration("u1", "g1", config);
            var response = Response.FromValue(entity, Mock.Of<Response>());

            _mockTableClient
                .Setup(x => x.GetEntityAsync<GameChatCommandsTableEntity>(
                    "u1",
                    "g1",
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await _repository.GetAsync("u1", "g1");

            Assert.NotNull(result);
            Assert.True(result!.Commands.ContainsKey("!d+"));
            Assert.Equal("increment", result.Commands["!d+"].Action);
            Assert.Equal("deaths", result.Commands["!d+"].Counter);
            Assert.Equal("moderator", result.Commands["!d+"].Permission);
        }

        [Fact]
        public async Task SaveAsync_ShouldUpsertEntity()
        {
            GameChatCommandsTableEntity? captured = null;

            _mockTableClient
                .Setup(x => x.UpsertEntityAsync(It.IsAny<GameChatCommandsTableEntity>(), TableUpdateMode.Replace, It.IsAny<CancellationToken>()))
                .Callback<GameChatCommandsTableEntity, TableUpdateMode, CancellationToken>((e, _, __) => captured = e)
                .ReturnsAsync(Mock.Of<Response>());

            var config = new ChatCommandConfiguration();
            config.Commands.Add("!stats", new ChatCommandDefinition
            {
                Response = "stats",
                Permission = "everyone"
            });

            await _repository.SaveAsync("u1", "g1", config);

            Assert.NotNull(captured);
            Assert.Equal("u1", captured!.PartitionKey);
            Assert.Equal("g1", captured.RowKey);
            Assert.Contains("!stats", captured.commandsConfig);
        }

        [Fact]
        public void GameChatCommandsTableEntity_ToConfiguration_InvalidJson_ReturnsEmptyConfig()
        {
            var entity = new GameChatCommandsTableEntity
            {
                PartitionKey = "u1",
                RowKey = "g1",
                commandsConfig = "{not-json}"
            };

            var config = entity.ToConfiguration();

            Assert.NotNull(config);
            Assert.Empty(config.Commands);
        }
    }
}
