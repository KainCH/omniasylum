using System;
using System.Collections.Generic;
using System.Linq;
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
    public class CounterLibraryRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly CounterLibraryRepository _repository;

        public CounterLibraryRepositoryTests()
        {
            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient.Setup(x => x.GetTableClient("counterlibrary")).Returns(_mockTableClient.Object);
            _repository = new CounterLibraryRepository(_mockServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task InitializeAsync_ShouldCreateTableIfNotExists()
        {
            await _repository.InitializeAsync();
            _mockTableClient.Verify(x => x.CreateIfNotExistsAsync(default), Times.Once);
        }

        [Fact]
        public async Task GetAsync_WhenCounterIdBlank_ShouldReturnNull_WithoutCallingTable()
        {
            var result = await _repository.GetAsync(" ");
            Assert.Null(result);

            _mockTableClient.Verify(
                x => x.GetEntityAsync<CounterLibraryTableEntity>(It.IsAny<string>(), It.IsAny<string>(), null, default),
                Times.Never);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnNull_WhenNotFound()
        {
            _mockTableClient
                .Setup(x => x.GetEntityAsync<CounterLibraryTableEntity>("counter", "c1", null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetAsync("c1");
            Assert.Null(result);
        }

        [Fact]
        public async Task UpsertAsync_WhenNull_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.UpsertAsync(null!));
        }

        [Fact]
        public async Task UpsertAsync_WhenMissingCounterId_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpsertAsync(new CounterLibraryItem { CounterId = "" }));
        }

        [Fact]
        public async Task UpsertAsync_ShouldUpsertEntityWithSerializedMilestones()
        {
            var item = new CounterLibraryItem
            {
                CounterId = "c1",
                Name = "Test",
                Icon = "bi-star",
                Milestones = new[] { 10, 50 },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                LastUpdated = DateTimeOffset.UtcNow
            };

            await _repository.UpsertAsync(item);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<CounterLibraryTableEntity>(e => e.PartitionKey == "counter" && e.RowKey == "c1" && e.MilestonesJson.Contains("10")),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task ListAsync_ShouldOrderByName_AndHandleInvalidMilestonesJson()
        {
            var a = new CounterLibraryTableEntity { RowKey = "a", Name = "alpha", Icon = "i", MilestonesJson = "[1]" };
            var b = new CounterLibraryTableEntity { RowKey = "b", Name = "Beta", Icon = "i", MilestonesJson = "{bad json]" };

            var page = Page<CounterLibraryTableEntity>.FromValues(new[] { b, a }, null, Mock.Of<Response>());
            var pageable = AsyncPageable<CounterLibraryTableEntity>.FromPages(new[] { page });

            _mockTableClient
                .Setup(x => x.QueryAsync<CounterLibraryTableEntity>(It.IsAny<string>(), null, null, default))
                .Returns(pageable);

            var results = (await _repository.ListAsync()).ToList();

            Assert.Equal(2, results.Count);
            Assert.Equal("alpha", results[0].Name);
            Assert.Equal("Beta", results[1].Name);
            Assert.Empty(results[1].Milestones);
        }
    }
}
