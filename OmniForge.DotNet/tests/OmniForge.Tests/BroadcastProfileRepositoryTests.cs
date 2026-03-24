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
    public class BroadcastProfileRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly BroadcastProfileRepository _repository;

        public BroadcastProfileRepositoryTests()
        {
            _mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>()))
                .Returns(_mockTableClient.Object);
            _repository = new BroadcastProfileRepository(_mockTableServiceClient.Object, Options.Create(new AzureTableConfiguration()));
        }

        [Fact]
        public async Task GetAsync_WhenExists_ReturnsDomainObject()
        {
            var profile = new BroadcastProfile
            {
                UserId = "user1",
                ProfileId = "profile1",
                Name = "Game Setup",
                ChecklistItems = new List<ChecklistItem>
                {
                    new ChecklistItem { Id = "c1", Label = "Start OBS", CheckType = "manual", SortOrder = 1 }
                }
            };
            var json = JsonSerializer.Serialize(profile);
            var entity = new BroadcastProfileTableEntity
            {
                PartitionKey = "user1",
                RowKey = "profile1",
                ProfileJson = json,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _mockTableClient.Setup(x => x.GetEntityAsync<BroadcastProfileTableEntity>(
                "user1", "profile1", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response<BroadcastProfileTableEntity>>(r => r.Value == entity));

            var result = await _repository.GetAsync("user1", "profile1");

            Assert.NotNull(result);
            Assert.Equal("user1", result!.UserId);
            Assert.Equal("profile1", result.ProfileId);
            Assert.Equal("Game Setup", result.Name);
            Assert.Single(result.ChecklistItems);
            Assert.Equal("Start OBS", result.ChecklistItems[0].Label);
            Assert.Equal("manual", result.ChecklistItems[0].CheckType);
        }

        [Fact]
        public async Task GetAsync_WhenNotFound_ReturnsNull()
        {
            _mockTableClient.Setup(x => x.GetEntityAsync<BroadcastProfileTableEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

            var result = await _repository.GetAsync("user1", "missing");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_WhenJsonInvalid_ReturnsFallbackProfile()
        {
            var entity = new BroadcastProfileTableEntity
            {
                PartitionKey = "user1",
                RowKey = "profile1",
                ProfileJson = "not valid json {{{"
            };

            _mockTableClient.Setup(x => x.GetEntityAsync<BroadcastProfileTableEntity>(
                "user1", "profile1", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response<BroadcastProfileTableEntity>>(r => r.Value == entity));

            var result = await _repository.GetAsync("user1", "profile1");

            Assert.NotNull(result);
            Assert.Equal("user1", result!.UserId);
            Assert.Equal("profile1", result.ProfileId);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsListFromTable()
        {
            var entity = new BroadcastProfileTableEntity
            {
                PartitionKey = "user1",
                RowKey = "profile1",
                ProfileJson = JsonSerializer.Serialize(new BroadcastProfile { UserId = "user1", ProfileId = "profile1", Name = "Main" })
            };
            var page = Page<BroadcastProfileTableEntity>.FromValues(new[] { entity }, null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<BroadcastProfileTableEntity>.FromPages(new[] { page });

            _mockTableClient.Setup(x => x.QueryAsync<BroadcastProfileTableEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            var result = await _repository.GetAllAsync("user1");

            Assert.Single(result);
            Assert.Equal("profile1", result[0].ProfileId);
        }

        [Fact]
        public async Task SaveAsync_UpsertsCalled()
        {
            var profile = new BroadcastProfile { UserId = "user1", ProfileId = "profile1", Name = "Daily" };

            await _repository.SaveAsync(profile);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<BroadcastProfileTableEntity>(e => e.PartitionKey == "user1" && e.RowKey == "profile1"),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_WhenExists_DeletesCalled()
        {
            await _repository.DeleteAsync("user1", "profile1");

            _mockTableClient.Verify(x => x.DeleteEntityAsync("user1", "profile1", It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_WhenNotFound_DoesNotThrow()
        {
            _mockTableClient.Setup(x => x.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

            var ex = await Record.ExceptionAsync(() => _repository.DeleteAsync("user1", "missing"));
            Assert.Null(ex);
        }
    }
}
