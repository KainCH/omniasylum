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

            _mockTableServiceClient.Setup(x => x.GetTableClient("alerts"))
                .Returns(_mockAlertsClient.Object);
            _mockTableServiceClient.Setup(x => x.GetTableClient("users"))
                .Returns(_mockUsersClient.Object);

            _repository = new AlertRepository(_mockTableServiceClient.Object);
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
                Name = "Test Alert",
                Type = "sound"
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
                Name = "Test Alert",
                Type = "sound"
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
                    e.Name == "Test Alert"),
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
    }
}
