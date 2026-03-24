using System;
using System.Collections.Generic;
using System.Text.Json;
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
    public class SceneActionRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly SceneActionRepository _repository;

        public SceneActionRepositoryTests()
        {
            _mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>()))
                .Returns(_mockTableClient.Object);
            _repository = new SceneActionRepository(_mockTableServiceClient.Object, Options.Create(new AzureTableConfiguration()));
        }

        [Fact]
        public async Task GetAsync_WhenExists_ReturnsDomainObject()
        {
            var action = new SceneAction
            {
                UserId = "user1",
                SceneName = "GameScene",
                TimerEnabled = true,
                TimerDurationMinutes = 30,
                AutoStartTimer = false
            };
            var entity = new SceneActionTableEntity
            {
                PartitionKey = "user1",
                RowKey = "GameScene",
                ActionJson = JsonSerializer.Serialize(action),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _mockTableClient.Setup(x => x.GetEntityAsync<SceneActionTableEntity>(
                "user1", "GameScene", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response<SceneActionTableEntity>>(r => r.Value == entity));

            var result = await _repository.GetAsync("user1", "GameScene");

            Assert.NotNull(result);
            Assert.Equal("user1", result!.UserId);
            Assert.True(result.TimerEnabled);
            Assert.Equal(30, result.TimerDurationMinutes);
        }

        [Fact]
        public async Task GetAsync_WhenNotFound_ReturnsNull()
        {
            _mockTableClient.Setup(x => x.GetEntityAsync<SceneActionTableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

            var result = await _repository.GetAsync("user1", "Missing");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_WhenJsonInvalid_ReturnsFallbackWithUnescapedSceneName()
        {
            var entity = new SceneActionTableEntity
            {
                PartitionKey = "user1",
                RowKey = "game_SLASH_level",
                ActionJson = "not valid json {{{"
            };

            _mockTableClient.Setup(x => x.GetEntityAsync<SceneActionTableEntity>(
                "user1", "game_SLASH_level", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response<SceneActionTableEntity>>(r => r.Value == entity));

            var result = await _repository.GetAsync("user1", "game_SLASH_level");

            Assert.NotNull(result);
            Assert.Equal("user1", result!.UserId);
            Assert.Equal("game/level", result.SceneName);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsListFromTable()
        {
            var entity = new SceneActionTableEntity
            {
                PartitionKey = "user1",
                RowKey = "Scene1",
                ActionJson = JsonSerializer.Serialize(new SceneAction { UserId = "user1", SceneName = "Scene1" })
            };
            var page = Page<SceneActionTableEntity>.FromValues(new[] { entity }, null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<SceneActionTableEntity>.FromPages(new[] { page });

            _mockTableClient.Setup(x => x.QueryAsync<SceneActionTableEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            var result = await _repository.GetAllAsync("user1");

            Assert.Single(result);
            Assert.Equal("user1", result[0].UserId);
        }

        [Fact]
        public async Task SaveAsync_UsesEscapedRowKey()
        {
            var sceneAction = new SceneAction { UserId = "user1", SceneName = "game/level" };

            await _repository.SaveAsync(sceneAction);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<SceneActionTableEntity>(e => e.PartitionKey == "user1" && e.RowKey == "game_SLASH_level"),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_WhenExists_DeletesCalled()
        {
            await _repository.DeleteAsync("user1", "GameScene");

            _mockTableClient.Verify(x => x.DeleteEntityAsync("user1", "GameScene", It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_WhenNotFound_DoesNotThrow()
        {
            _mockTableClient.Setup(x => x.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

            var ex = await Record.ExceptionAsync(() => _repository.DeleteAsync("user1", "Missing"));
            Assert.Null(ex);
        }
    }
}
