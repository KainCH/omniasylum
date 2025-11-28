using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    public class SeriesRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient;
        private readonly Mock<TableClient> _mockTableClient;
        private readonly SeriesRepository _repository;

        public SeriesRepositoryTests()
        {
            _mockTableServiceClient = new Mock<TableServiceClient>();
            _mockTableClient = new Mock<TableClient>();

            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockTableServiceClient.Setup(x => x.GetTableClient("series"))
                .Returns(_mockTableClient.Object);

            _repository = new SeriesRepository(_mockTableServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task GetSeriesAsync_ShouldReturnSeries_WhenExist()
        {
            // Arrange
            var userId = "123";
            var entity = new TableEntity(userId, "abc")
            {
                ["name"] = "Test Series",
                ["description"] = "Test Description",
                ["snapshot"] = JsonSerializer.Serialize(new Counter { Deaths = 10 }),
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["lastUpdated"] = DateTimeOffset.UtcNow,
                ["isActive"] = true
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
                ["name"] = "Test Series",
                ["snapshot"] = JsonSerializer.Serialize(new Counter { Deaths = 10 })
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
                    (string)e["name"] == "Test Series"),
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
                    (string)e["name"] == "Test Series"),
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

        [Fact]
        public async Task GetSeriesByIdAsync_ShouldReturnNull_WhenNotFound()
        {
            // Arrange
            var userId = "123";
            var seriesId = "nonexistent";
            var exception = new RequestFailedException(404, "Not Found", "NotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                seriesId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.GetSeriesByIdAsync(userId, seriesId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetSeriesAsync_ShouldReturnEmptyList_WhenNoSeries()
        {
            // Arrange
            var userId = "123";
            var page = Page<TableEntity>.FromValues(Array.Empty<TableEntity>(), null, Mock.Of<Response>());
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
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetSeriesAsync_ShouldReturnMultipleSeries()
        {
            // Arrange
            var userId = "123";
            var entity1 = new TableEntity(userId, "series1")
            {
                ["name"] = "Series 1",
                ["description"] = "Description 1",
                ["snapshot"] = JsonSerializer.Serialize(new Counter { Deaths = 10 }),
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["lastUpdated"] = DateTimeOffset.UtcNow,
                ["isActive"] = true
            };
            var entity2 = new TableEntity(userId, "series2")
            {
                ["name"] = "Series 2",
                ["description"] = "Description 2",
                ["snapshot"] = JsonSerializer.Serialize(new Counter { Deaths = 20 }),
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["lastUpdated"] = DateTimeOffset.UtcNow,
                ["isActive"] = false
            };

            var page = Page<TableEntity>.FromValues(new[] { entity1, entity2 }, null, Mock.Of<Response>());
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
            Assert.Equal(2, result.Count());
            Assert.Contains(result, s => s.Name == "Series 1" && s.Snapshot.Deaths == 10);
            Assert.Contains(result, s => s.Name == "Series 2" && s.Snapshot.Deaths == 20);
        }

        [Fact]
        public async Task CreateSeriesAsync_ShouldIncludeAllProperties()
        {
            // Arrange
            var series = new Series
            {
                UserId = "123",
                Id = "abc",
                Name = "Full Series",
                Description = "Full Description",
                Snapshot = new Counter { Deaths = 10, Swears = 5, Screams = 3 },
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdated = DateTimeOffset.UtcNow,
                IsActive = true
            };

            // Act
            await _repository.CreateSeriesAsync(series);

            // Assert
            _mockTableClient.Verify(x => x.AddEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == "123" &&
                    e.RowKey == "abc" &&
                    (string)e["name"] == "Full Series" &&
                    (string)e["description"] == "Full Description" &&
                    (bool)e["isActive"] == true),
                default), Times.Once);
        }

        [Fact]
        public async Task GetSeriesAsync_ShouldHandleMissingOptionalProperties()
        {
            // Arrange
            var userId = "123";
            var entity = new TableEntity(userId, "abc")
            {
                ["name"] = "Minimal Series",
                ["snapshot"] = JsonSerializer.Serialize(new Counter())
                // description, createdAt, lastUpdated, isActive are missing
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
            Assert.Equal("Minimal Series", series.Name);
            // Description defaults to empty string when not set
            Assert.True(string.IsNullOrEmpty(series.Description));
        }
    }
}
