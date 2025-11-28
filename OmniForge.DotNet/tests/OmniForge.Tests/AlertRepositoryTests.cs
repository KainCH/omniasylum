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
using OmniForge.Infrastructure.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class AlertRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient;
        private readonly Mock<TableClient> _mockAlertsClient;
        private readonly Mock<TableClient> _mockUsersClient;
        private readonly AlertRepository _repository;

        public AlertRepositoryTests()
        {
            _mockTableServiceClient = new Mock<TableServiceClient>();
            _mockAlertsClient = new Mock<TableClient>();
            _mockUsersClient = new Mock<TableClient>();

            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockTableServiceClient.Setup(x => x.GetTableClient("alerts"))
                .Returns(_mockAlertsClient.Object);
            _mockTableServiceClient.Setup(x => x.GetTableClient("users"))
                .Returns(_mockUsersClient.Object);

            _repository = new AlertRepository(_mockTableServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task GetAlertAsync_ShouldReturnAlert_WhenExists()
        {
            // Arrange
            var userId = "123";
            var alertId = "abc";
            var entity = new AlertTableEntity
            {
                PartitionKey = userId,
                RowKey = alertId,
                name = "Test Alert",
                type = "sound"
            };

            var mockResponse = Mock.Of<Response<AlertTableEntity>>(r => r.Value == entity);

            _mockAlertsClient.Setup(x => x.GetEntityAsync<AlertTableEntity>(
                userId,
                alertId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetAlertAsync(userId, alertId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(alertId, result.Id);
            Assert.Equal("Test Alert", result.Name);
        }

        [Fact]
        public async Task GetAlertsAsync_ShouldReturnAlerts_WhenExist()
        {
            // Arrange
            var userId = "123";
            var entity = new AlertTableEntity
            {
                PartitionKey = userId,
                RowKey = "abc",
                name = "Test Alert",
                type = "sound"
            };

            var page = Page<AlertTableEntity>.FromValues(new[] { entity }, null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<AlertTableEntity>.FromPages(new[] { page });

            _mockAlertsClient.Setup(x => x.QueryAsync<AlertTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            // Act
            var result = await _repository.GetAlertsAsync(userId);

            // Assert
            Assert.Single(result);
            Assert.Equal("abc", result.First().Id);
        }

        [Fact]
        public async Task SaveAlertAsync_ShouldUpsertEntity()
        {
            // Arrange
            var alert = new Alert
            {
                UserId = "123",
                Id = "abc",
                Name = "Test Alert",
                Type = "sound"
            };

            // Act
            await _repository.SaveAlertAsync(alert);

            // Assert
            _mockAlertsClient.Verify(x => x.UpsertEntityAsync(
                It.Is<AlertTableEntity>(e =>
                    e.PartitionKey == "123" &&
                    e.RowKey == "abc" &&
                    e.name == "Test Alert"),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAlertAsync_ShouldDeleteEntity()
        {
            // Arrange
            var userId = "123";
            var alertId = "abc";

            // Act
            await _repository.DeleteAlertAsync(userId, alertId);

            // Assert
            _mockAlertsClient.Verify(x => x.DeleteEntityAsync(
                userId,
                alertId,
                default,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEventMappingsAsync_ShouldReturnMappings_WhenExist()
        {
            // Arrange
            var userId = "123";
            var mappings = new Dictionary<string, string> { { "event1", "alert1" } };
            var json = JsonSerializer.Serialize(mappings);
            var entity = new TableEntity(userId, "event-mappings")
            {
                ["mappings"] = json
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == entity);

            _mockUsersClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "event-mappings",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetEventMappingsAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ContainsKey("event1"));
            Assert.Equal("alert1", result["event1"]);
        }

        [Fact]
        public async Task SaveEventMappingsAsync_ShouldUpsertEntity()
        {
            // Arrange
            var userId = "123";
            var mappings = new Dictionary<string, string> { { "event1", "alert1" } };

            // Act
            await _repository.SaveEventMappingsAsync(userId, mappings);

            // Assert
            _mockUsersClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == userId &&
                    e.RowKey == "event-mappings" &&
                    e.ContainsKey("mappings")),
                TableUpdateMode.Merge,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAlertAsync_ShouldReturnNull_WhenNotFound()
        {
            // Arrange
            var userId = "123";
            var alertId = "nonexistent";

            _mockAlertsClient.Setup(x => x.GetEntityAsync<AlertTableEntity>(
                userId,
                alertId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not found"));

            // Act
            var result = await _repository.GetAlertAsync(userId, alertId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task InitializeAsync_ShouldCreateTablesIfNotExist()
        {
            // Act
            await _repository.InitializeAsync();

            // Assert
            _mockAlertsClient.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
            _mockUsersClient.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEventMappingsAsync_ShouldReturnEmptyDictionary_WhenNotFound()
        {
            // Arrange
            var userId = "123";

            _mockUsersClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "event-mappings",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not found"));

            // Act
            var result = await _repository.GetEventMappingsAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetEventMappingsAsync_ShouldReturnEmptyDictionary_WhenMappingsFieldMissing()
        {
            // Arrange
            var userId = "123";
            var entity = new TableEntity(userId, "event-mappings");
            // Note: No "mappings" key

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == entity);

            _mockUsersClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "event-mappings",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetEventMappingsAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetAlertsAsync_ShouldReturnEmptyList_WhenNoAlerts()
        {
            // Arrange
            var userId = "123";

            var page = Page<AlertTableEntity>.FromValues(Array.Empty<AlertTableEntity>(), null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<AlertTableEntity>.FromPages(new[] { page });

            _mockAlertsClient.Setup(x => x.QueryAsync<AlertTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            // Act
            var result = await _repository.GetAlertsAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task SaveAlertAsync_ShouldSerializeAllProperties()
        {
            // Arrange
            var alert = new Alert
            {
                UserId = "123",
                Id = "abc",
                Name = "Complex Alert",
                Type = "sound",
                Sound = "sounds/test.mp3",
                Duration = 5000,
                IsDefault = true
            };

            // Act
            await _repository.SaveAlertAsync(alert);

            // Assert
            _mockAlertsClient.Verify(x => x.UpsertEntityAsync(
                It.Is<AlertTableEntity>(e =>
                    e.PartitionKey == "123" &&
                    e.RowKey == "abc" &&
                    e.name == "Complex Alert" &&
                    e.type == "sound" &&
                    e.sound == "sounds/test.mp3" &&
                    e.duration == 5000 &&
                    e.isDefault == true),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAlertsAsync_ShouldReturnMultipleAlerts()
        {
            // Arrange
            var userId = "123";
            var entities = new[]
            {
                new AlertTableEntity { PartitionKey = userId, RowKey = "alert1", name = "Alert 1", type = "sound" },
                new AlertTableEntity { PartitionKey = userId, RowKey = "alert2", name = "Alert 2", type = "visual" },
                new AlertTableEntity { PartitionKey = userId, RowKey = "alert3", name = "Alert 3", type = "sound" }
            };

            var page = Page<AlertTableEntity>.FromValues(entities, null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<AlertTableEntity>.FromPages(new[] { page });

            _mockAlertsClient.Setup(x => x.QueryAsync<AlertTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            // Act
            var result = await _repository.GetAlertsAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count());
        }

        [Fact]
        public async Task SaveEventMappingsAsync_ShouldIncludeUpdatedAt()
        {
            // Arrange
            var userId = "123";
            var mappings = new Dictionary<string, string>
            {
                { "death", "alert1" },
                { "swear", "alert2" }
            };

            // Act
            await _repository.SaveEventMappingsAsync(userId, mappings);

            // Assert
            _mockUsersClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == userId &&
                    e.RowKey == "event-mappings" &&
                    e.ContainsKey("mappings") &&
                    e.ContainsKey("updatedAt")),
                TableUpdateMode.Merge,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
