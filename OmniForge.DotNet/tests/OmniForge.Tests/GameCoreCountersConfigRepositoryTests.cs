using System;
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
    public class GameCoreCountersConfigRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly GameCoreCountersConfigRepository _repository;

        public GameCoreCountersConfigRepositoryTests()
        {
            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient.Setup(x => x.GetTableClient("gamecorecounters")).Returns(_mockTableClient.Object);
            _repository = new GameCoreCountersConfigRepository(_mockServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task InitializeAsync_ShouldCreateTableIfNotExists()
        {
            await _repository.InitializeAsync();
            _mockTableClient.Verify(x => x.CreateIfNotExistsAsync(default), Times.Once);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnNull_WhenNotFound()
        {
            _mockTableClient
                .Setup(x => x.GetEntityAsync<GameCoreCountersConfigTableEntity>("u1", "g1", null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetAsync("u1", "g1");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ShouldMapEntityToModel()
        {
            var entity = new GameCoreCountersConfigTableEntity
            {
                PartitionKey = "u1",
                RowKey = "g1",
                DeathsEnabled = true,
                SwearsEnabled = false,
                ScreamsEnabled = true,
                BitsEnabled = true,
                UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            var response = Response.FromValue(entity, Mock.Of<Response>());
            _mockTableClient
                .Setup(x => x.GetEntityAsync<GameCoreCountersConfigTableEntity>("u1", "g1", null, default))
                .ReturnsAsync(response);

            var result = await _repository.GetAsync("u1", "g1");

            Assert.NotNull(result);
            Assert.Equal("u1", result!.UserId);
            Assert.Equal("g1", result.GameId);
            Assert.True(result.DeathsEnabled);
            Assert.False(result.SwearsEnabled);
            Assert.True(result.ScreamsEnabled);
            Assert.True(result.BitsEnabled);
        }

        [Fact]
        public async Task SaveAsync_WhenConfigNull_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.SaveAsync("u1", "g1", null!));
        }

        [Fact]
        public async Task SaveAsync_ShouldUpsertEntity_AndDefaultUpdatedAt()
        {
            GameCoreCountersConfigTableEntity? captured = null;

            _mockTableClient
                .Setup(x => x.UpsertEntityAsync(It.IsAny<GameCoreCountersConfigTableEntity>(), TableUpdateMode.Replace, default))
                .Callback<GameCoreCountersConfigTableEntity, TableUpdateMode, System.Threading.CancellationToken>((e, _, _) => captured = e)
                .ReturnsAsync(Mock.Of<Response>());

            var config = new GameCoreCountersConfig(
                UserId: "u1",
                GameId: "g1",
                DeathsEnabled: false,
                SwearsEnabled: true,
                ScreamsEnabled: false,
                BitsEnabled: false,
                UpdatedAt: default);

            await _repository.SaveAsync("u1", "g1", config);

            Assert.NotNull(captured);
            Assert.Equal("u1", captured!.PartitionKey);
            Assert.Equal("g1", captured.RowKey);
            Assert.False(captured.DeathsEnabled);
            Assert.True(captured.SwearsEnabled);
            Assert.False(captured.ScreamsEnabled);
            Assert.False(captured.BitsEnabled);
            Assert.NotEqual(default, captured.UpdatedAt);
        }
    }
}
