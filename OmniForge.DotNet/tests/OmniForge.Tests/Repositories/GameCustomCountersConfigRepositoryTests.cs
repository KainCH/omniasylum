using System;
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

namespace OmniForge.Tests.Repositories
{
    public class GameCustomCountersConfigRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly GameCustomCountersConfigRepository _repository;

        public GameCustomCountersConfigRepositoryTests()
        {
            var config = Options.Create(new AzureTableConfiguration
            {
                GameCustomCountersConfigTable = "gamecustomcounters"
            });

            _mockServiceClient
                .Setup(x => x.GetTableClient("gamecustomcounters"))
                .Returns(_mockTableClient.Object);

            _repository = new GameCustomCountersConfigRepository(_mockServiceClient.Object, config);
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
                .Setup(x => x.GetEntityAsync<GameCustomCountersConfigTableEntity>(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetAsync("u1", "g1");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ShouldMapEntityToModel()
        {
            var config = new CustomCounterConfiguration();
            config.Counters["deaths"] = new CustomCounterDefinition { Name = "Deaths" };

            var entity = GameCustomCountersConfigTableEntity.FromConfiguration("u1", "g1", config);

            var response = Response.FromValue(entity, Mock.Of<Response>());
            _mockTableClient
                .Setup(x => x.GetEntityAsync<GameCustomCountersConfigTableEntity>(
                    "u1",
                    "g1",
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await _repository.GetAsync("u1", "g1");

            Assert.NotNull(result);
            Assert.True(result!.Counters.ContainsKey("deaths"));
        }

        [Fact]
        public async Task SaveAsync_ShouldUpsertEntity()
        {
            GameCustomCountersConfigTableEntity? captured = null;

            _mockTableClient
                .Setup(x => x.UpsertEntityAsync(It.IsAny<GameCustomCountersConfigTableEntity>(), TableUpdateMode.Replace, It.IsAny<CancellationToken>()))
                .Callback<GameCustomCountersConfigTableEntity, TableUpdateMode, CancellationToken>((e, _, __) => captured = e)
                .ReturnsAsync(Mock.Of<Response>());

            var config = new CustomCounterConfiguration();
            config.Counters["counterA"] = new CustomCounterDefinition { Name = "Counter A" };

            await _repository.SaveAsync("u1", "g1", config);

            Assert.NotNull(captured);
            Assert.Equal("u1", captured!.PartitionKey);
            Assert.Equal("g1", captured.RowKey);
            Assert.Contains("counterA", captured.countersConfig);
        }
    }
}
