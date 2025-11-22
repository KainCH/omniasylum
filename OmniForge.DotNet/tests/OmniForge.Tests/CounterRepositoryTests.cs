using System;
using System.Collections.Generic;
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
    public class CounterRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient;
        private readonly Mock<TableClient> _mockTableClient;
        private readonly CounterRepository _repository;

        public CounterRepositoryTests()
        {
            _mockTableServiceClient = new Mock<TableServiceClient>();
            _mockTableClient = new Mock<TableClient>();

            _mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>()))
                .Returns(_mockTableClient.Object);

            _repository = new CounterRepository(_mockTableServiceClient.Object);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldReturnCounters_WhenExists()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["Deaths"] = 5,
                ["Swears"] = 10
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "counters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.Equal(5, result.Deaths);
            Assert.Equal(10, result.Swears);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldReturnDefault_WhenNotFound()
        {
            // Arrange
            var userId = "123";
            var exception = new RequestFailedException(404, "Not Found", "NotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "counters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.Equal(0, result.Deaths);
            Assert.Equal(userId, result.TwitchUserId);
        }

        [Fact]
        public async Task SaveCountersAsync_ShouldUpsertEntity()
        {
            // Arrange
            var counter = new Counter
            {
                TwitchUserId = "123",
                Deaths = 1,
                CustomCounters = new Dictionary<string, int> { { "custom1", 5 } }
            };

            // Act
            await _repository.SaveCountersAsync(counter);

            // Assert
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == "123" &&
                    (int)e["Deaths"] == 1 &&
                    (int)e["custom1"] == 5),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetCustomCountersConfigAsync_ShouldReturnConfig_WhenExists()
        {
            // Arrange
            var userId = "123";
            var config = new CustomCounterConfiguration();
            config.Counters.Add("c1", new CustomCounterDefinition { Name = "C1", Icon = "💀" });

            var entity = CustomCounterConfigTableEntity.FromConfiguration(userId, config);

            var mockResponse = Mock.Of<Response<CustomCounterConfigTableEntity>>(r => r.Value == entity);

            _mockTableClient.Setup(x => x.GetEntityAsync<CustomCounterConfigTableEntity>(
                userId,
                "customCounters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCustomCountersConfigAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Counters.ContainsKey("c1"));
            Assert.Equal("C1", result.Counters["c1"].Name);
        }

        [Fact]
        public async Task SaveCustomCountersConfigAsync_ShouldUpsertEntity()
        {
            // Arrange
            var userId = "123";
            var config = new CustomCounterConfiguration();
            config.Counters.Add("c1", new CustomCounterDefinition { Name = "C1" });

            // Act
            await _repository.SaveCustomCountersConfigAsync(userId, config);

            // Assert
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<CustomCounterConfigTableEntity>(e =>
                    e.PartitionKey == userId &&
                    e.RowKey == "customCounters" &&
                    e.CountersConfig.Contains("C1")),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IncrementCounterAsync_ShouldIncrementStandardCounter_WhenExists()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["Deaths"] = 5
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "counters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.IncrementCounterAsync(userId, "deaths", 2);

            // Assert
            Assert.Equal(7, result.Deaths);
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e => (int)e["Deaths"] == 7),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IncrementCounterAsync_ShouldIncrementCustomCounter_WhenExists()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["custom1"] = 10
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "counters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.IncrementCounterAsync(userId, "custom1", 5);

            // Assert
            Assert.True(result.CustomCounters.ContainsKey("custom1"));
            Assert.Equal(15, result.CustomCounters["custom1"]);
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e => (int)e["custom1"] == 15),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IncrementCounterAsync_ShouldCreateAndIncrement_WhenNotExists()
        {
            // Arrange
            var userId = "123";
            var exception = new RequestFailedException(404, "Not Found", "NotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(
                userId,
                "counters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.IncrementCounterAsync(userId, "deaths", 1);

            // Assert
            Assert.Equal(1, result.Deaths);
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e => (int)e["Deaths"] == 1),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
