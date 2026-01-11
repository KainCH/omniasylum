using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class GameContextRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly GameContextRepository _repository;

        public GameContextRepositoryTests()
        {
            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient.Setup(x => x.GetTableClient("gamecontext")).Returns(_mockTableClient.Object);
            _repository = new GameContextRepository(_mockServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnNull_WhenNotFound()
        {
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>("u1", "active", null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetAsync("u1");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ShouldMapFields_AndParseUpdatedAtString()
        {
            var updated = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var entity = new TableEntity("u1", "active")
            {
                ["activeGameId"] = "g1",
                ["activeGameName"] = "Game",
                ["updatedAt"] = updated.ToString("O")
            };

            var response = Response.FromValue(entity, Mock.Of<Response>());
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>("u1", "active", null, default))
                .ReturnsAsync(response);

            var result = await _repository.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Equal("u1", result!.UserId);
            Assert.Equal("g1", result.ActiveGameId);
            Assert.Equal("Game", result.ActiveGameName);
            Assert.Equal(updated, result.UpdatedAt);
        }

        [Fact]
        public async Task SaveAsync_ShouldUpsertEntity_WithEmptyStringsForNulls()
        {
            var context = new GameContext
            {
                UserId = "u1",
                ActiveGameId = null,
                ActiveGameName = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _repository.SaveAsync(context);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e => e.PartitionKey == "u1" && e.RowKey == "active" && (string)e["activeGameId"]! == string.Empty),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task ClearAsync_WhenNotFound_ShouldNoOp()
        {
            _mockTableClient
                .Setup(x => x.DeleteEntityAsync("u1", "active", default, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            await _repository.ClearAsync("u1");

            _mockTableClient.Verify(x => x.DeleteEntityAsync("u1", "active", default, default), Times.Once);
        }
    }
}
