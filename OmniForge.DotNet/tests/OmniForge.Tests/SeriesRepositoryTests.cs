using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class SeriesRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient;
        private readonly Mock<TableClient> _mockTableClient;
        private readonly SeriesRepository _repository;

        public SeriesRepositoryTests()
        {
            _mockTableServiceClient = new Mock<TableServiceClient>();
            _mockTableClient = new Mock<TableClient>();

            _mockTableServiceClient.Setup(x => x.GetTableClient("series"))
                .Returns(_mockTableClient.Object);

            _repository = new SeriesRepository(_mockTableServiceClient.Object);
        }

        [Fact]
        public async Task GetSeriesAsync_ShouldReturnSeries_WhenExist()
        {
            // Arrange
            var userId = "123";
            var entity = new TableEntity(userId, "abc")
            {
                ["Name"] = "Test Series",
                ["Description"] = "Test Description",
                ["Snapshot"] = JsonSerializer.Serialize(new Counter { Deaths = 10 }),
                ["CreatedAt"] = DateTimeOffset.UtcNow,
                ["LastUpdated"] = DateTimeOffset.UtcNow,
                ["IsActive"] = true
            };

            var page = Page<TableEntity>.FromValues(new[] { entity }, null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<TableEntity>.FromPages(new[] { page });

            _mockTableClient.Setup(x => x.QueryAsync<TableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            // Act
            var result = await _repository.GetSeriesAsync(userId);

            // Assert
            Assert.Single(result);
            var series = result.First();
            Assert.Equal("abc", series.Id);
            Assert.Equal("Test Series", series.Name);
            Assert.Equal(10, series.Snapshot.Deaths);
        }

        [Fact]
        public async Task GetSeriesByIdAsync_ShouldReturnSeries_WhenExists()
        {
            // Arrange
            var userId = "123";
            var seriesId = "abc";
            var entity = new TableEntity(userId, seriesId)
            {
                ["Name"] = "Test Series",
                ["Snapshot"] = JsonSerializer.Serialize(new Counter { Deaths = 10 })
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == entity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                seriesId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetSeriesByIdAsync(userId, seriesId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(seriesId, result.Id);
            Assert.Equal("Test Series", result.Name);
        }

        [Fact]
        public async Task CreateSeriesAsync_ShouldAddEntity()
        {
            // Arrange
            var series = new Series
            {
                UserId = "123",
                Id = "abc",
                Name = "Test Series",
                Snapshot = new Counter { Deaths = 10 }
            };

            // Act
            await _repository.CreateSeriesAsync(series);

            // Assert
            _mockTableClient.Verify(x => x.AddEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == "123" &&
                    e.RowKey == "abc" &&
                    (string)e["Name"] == "Test Series"),
                default), Times.Once);
        }

        [Fact]
        public async Task UpdateSeriesAsync_ShouldUpsertEntity()
        {
            // Arrange
            var series = new Series
            {
                UserId = "123",
                Id = "abc",
                Name = "Test Series",
                Snapshot = new Counter { Deaths = 10 }
            };

            // Act
            await _repository.UpdateSeriesAsync(series);

            // Assert
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == "123" &&
                    e.RowKey == "abc" &&
                    (string)e["Name"] == "Test Series"),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task DeleteSeriesAsync_ShouldDeleteEntity()
        {
            // Arrange
            var userId = "123";
            var seriesId = "abc";

            // Act
            await _repository.DeleteSeriesAsync(userId, seriesId);

            // Assert
            _mockTableClient.Verify(x => x.DeleteEntityAsync(
                userId,
                seriesId,
                default,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
