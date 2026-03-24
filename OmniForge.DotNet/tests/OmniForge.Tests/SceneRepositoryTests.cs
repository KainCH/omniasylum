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
    public class SceneRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly SceneRepository _repository;

        public SceneRepositoryTests()
        {
            _mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>()))
                .Returns(_mockTableClient.Object);
            _repository = new SceneRepository(_mockTableServiceClient.Object, Options.Create(new AzureTableConfiguration()));
        }

        [Fact]
        public void EscapeRowKey_EscapesSpecialChars()
        {
            Assert.Equal("a_SLASH_b_BSLASH_c_HASH_d_QMARK_e", SceneRepository.EscapeRowKey("a/b\\c#d?e"));
        }

        [Fact]
        public void UnescapeRowKey_RestoresSpecialChars()
        {
            Assert.Equal("a/b\\c#d?e", SceneRepository.UnescapeRowKey("a_SLASH_b_BSLASH_c_HASH_d_QMARK_e"));
        }

        [Fact]
        public async Task GetSceneAsync_WhenExists_ReturnsDomainObject()
        {
            var entity = new SceneTableEntity
            {
                PartitionKey = "user1",
                RowKey = "GameScene",
                SceneName = "GameScene",
                Source = "obs",
                FirstSeen = DateTimeOffset.UtcNow.AddHours(-1),
                LastSeen = DateTimeOffset.UtcNow
            };
            _mockTableClient.Setup(x => x.GetEntityAsync<SceneTableEntity>(
                "user1", "GameScene", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response<SceneTableEntity>>(r => r.Value == entity));

            var result = await _repository.GetSceneAsync("user1", "GameScene");

            Assert.NotNull(result);
            Assert.Equal("user1", result!.UserId);
            Assert.Equal("GameScene", result.Name);
            Assert.Equal("obs", result.Source);
        }

        [Fact]
        public async Task GetSceneAsync_WhenNotFound_ReturnsNull()
        {
            _mockTableClient.Setup(x => x.GetEntityAsync<SceneTableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

            var result = await _repository.GetSceneAsync("user1", "Missing");

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveSceneAsync_UpsertsCalled()
        {
            var scene = new Scene
            {
                UserId = "user1",
                Name = "game/level",
                Source = "obs",
                FirstSeen = DateTimeOffset.UtcNow.AddHours(-1),
                LastSeen = DateTimeOffset.UtcNow
            };

            await _repository.SaveSceneAsync(scene);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<SceneTableEntity>(e => e.PartitionKey == "user1" && e.RowKey == "game_SLASH_level" && e.SceneName == "game/level"),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteSceneAsync_WhenExists_DeletesEntity()
        {
            await _repository.DeleteSceneAsync("user1", "GameScene");

            _mockTableClient.Verify(x => x.DeleteEntityAsync("user1", "GameScene", It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteSceneAsync_WhenNotFound_DoesNotThrow()
        {
            _mockTableClient.Setup(x => x.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

            var ex = await Record.ExceptionAsync(() => _repository.DeleteSceneAsync("user1", "Missing"));
            Assert.Null(ex);
        }

        [Fact]
        public async Task GetScenesAsync_ReturnsListFromTable()
        {
            var entity = new SceneTableEntity
            {
                PartitionKey = "user1",
                RowKey = "Scene1",
                SceneName = "Scene1",
                Source = "streamlabs"
            };
            var page = Page<SceneTableEntity>.FromValues(new[] { entity }, null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<SceneTableEntity>.FromPages(new[] { page });

            _mockTableClient.Setup(x => x.QueryAsync<SceneTableEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            var result = await _repository.GetScenesAsync("user1");

            Assert.Single(result);
            Assert.Equal("Scene1", result[0].Name);
        }
    }
}
