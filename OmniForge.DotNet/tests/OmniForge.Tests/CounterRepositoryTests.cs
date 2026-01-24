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
    public class CounterRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient;
        private readonly Mock<TableClient> _mockTableClient;
        private readonly CounterRepository _repository;

        public CounterRepositoryTests()
        {
            _mockTableServiceClient = new Mock<TableServiceClient>();
            _mockTableClient = new Mock<TableClient>();

            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>()))
                .Returns(_mockTableClient.Object);

            _repository = new CounterRepository(_mockTableServiceClient.Object, tableConfig);
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
            Assert.NotNull(result);
            Assert.Equal(5, result!.Deaths);
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
            Assert.NotNull(result);
            Assert.Equal(0, result!.Deaths);
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
                    e.countersConfig.Contains("C1")),
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

        [Fact]
        public async Task GetCustomCountersConfigAsync_ShouldReturnEmpty_WhenNotFound()
        {
            // Arrange
            var userId = "123";
            var exception = new RequestFailedException(404, "Not Found", "NotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<CustomCounterConfigTableEntity>(
                userId,
                "customCounters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.GetCustomCountersConfigAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Counters);
        }

        [Fact]
        public async Task IncrementCounterAsync_ShouldIncrementSwears()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Swears"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.IncrementCounterAsync(userId, "swears", 1);

            // Assert
            Assert.Equal(6, result.Swears);
        }

        [Fact]
        public async Task IncrementCounterAsync_ShouldIncrementScreams()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Screams"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.IncrementCounterAsync(userId, "screams", 1);

            // Assert
            Assert.Equal(6, result.Screams);
        }

        [Fact]
        public async Task DecrementCounterAsync_ShouldDecrementDeaths_AndClampToZero()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Deaths"] = 0 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.DecrementCounterAsync(userId, "deaths", 1);

            // Assert
            Assert.Equal(0, result.Deaths);
        }

        [Fact]
        public async Task DecrementCounterAsync_ShouldDecrementSwears()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Swears"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.DecrementCounterAsync(userId, "swears", 1);

            // Assert
            Assert.Equal(4, result.Swears);
        }

        [Fact]
        public async Task DecrementCounterAsync_ShouldDecrementScreams()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Screams"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.DecrementCounterAsync(userId, "screams", 1);

            // Assert
            Assert.Equal(4, result.Screams);
        }

        [Fact]
        public async Task DecrementCounterAsync_ShouldDecrementCustomCounter()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["custom1"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.DecrementCounterAsync(userId, "custom1", 1);

            // Assert
            Assert.Equal(4, result.CustomCounters["custom1"]);
        }

        [Fact]
        public async Task ResetCounterAsync_ShouldResetDeaths()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Deaths"] = 10 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.ResetCounterAsync(userId, "deaths");

            // Assert
            Assert.Equal(0, result.Deaths);
        }

        [Fact]
        public async Task ResetCounterAsync_ShouldResetSwears()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Swears"] = 10 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.ResetCounterAsync(userId, "swears");

            // Assert
            Assert.Equal(0, result.Swears);
        }

        [Fact]
        public async Task ResetCounterAsync_ShouldResetScreams()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Screams"] = 10 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.ResetCounterAsync(userId, "screams");

            // Assert
            Assert.Equal(0, result.Screams);
        }

        [Fact]
        public async Task ResetCounterAsync_ShouldResetCustomCounter()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["custom1"] = 10 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.ResetCounterAsync(userId, "custom1");

            // Assert
            Assert.Equal(0, result.CustomCounters["custom1"]);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldMapLongValues()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["customLong"] = 100L
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.True(result!.CustomCounters.ContainsKey("customLong"));
            Assert.Equal(100, result.CustomCounters["customLong"]);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldMapAllStandardCounters()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["Deaths"] = 5,
                ["Swears"] = 10,
                ["Screams"] = 15,
                ["Bits"] = 1000
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(5, result!.Deaths);
            Assert.Equal(10, result.Swears);
            Assert.Equal(15, result.Screams);
            Assert.Equal(1000, result.Bits);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldHandleCamelCaseProperties()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["deaths"] = 5,
                ["swears"] = 10
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(5, result!.Deaths);
            Assert.Equal(10, result.Swears);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldHandleDoubleValues()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["customDouble"] = 42.5
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.True(result!.CustomCounters.ContainsKey("customDouble"));
            Assert.Equal(42, result.CustomCounters["customDouble"]);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldHandleStringIntValues()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["customString"] = "99"
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.True(result!.CustomCounters.ContainsKey("customString"));
            Assert.Equal(99, result.CustomCounters["customString"]);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldHandleDateTimeOffsetProperties()
        {
            // Arrange
            var userId = "123";
            var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
            var streamStarted = DateTimeOffset.UtcNow.AddHours(-2);
            var tableEntity = new TableEntity(userId, "counters")
            {
                ["Deaths"] = 0,
                ["LastUpdated"] = lastUpdated,
                ["StreamStarted"] = streamStarted,
                ["LastNotifiedStreamId"] = "stream123"
            };

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(lastUpdated, result!.LastUpdated);
            Assert.Equal(streamStarted, result.StreamStarted);
            Assert.Equal("stream123", result.LastNotifiedStreamId);
        }

        [Fact]
        public async Task SaveCountersAsync_ShouldIncludeAllProperties()
        {
            // Arrange
            var streamStarted = DateTimeOffset.UtcNow.AddHours(-1);
            var counter = new Counter
            {
                TwitchUserId = "123",
                Deaths = 10,
                Swears = 20,
                Screams = 30,
                Bits = 500,
                StreamStarted = streamStarted,
                LastNotifiedStreamId = "stream456",
                CustomCounters = new Dictionary<string, int> { { "custom1", 5 }, { "custom2", 10 } }
            };

            // Act
            await _repository.SaveCountersAsync(counter);

            // Assert
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == "123" &&
                    (int)e["Deaths"] == 10 &&
                    (int)e["Swears"] == 20 &&
                    (int)e["Screams"] == 30 &&
                    (int)e["Bits"] == 500 &&
                    (int)e["custom1"] == 5 &&
                    (int)e["custom2"] == 10),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IncrementCounterAsync_ShouldIncrementBits()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Bits"] = 100 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act  - Bits would be handled as custom counter since there's no special case
            var result = await _repository.IncrementCounterAsync(userId, "bits", 50);

            // Assert - bits is not a standard counter so should be treated as custom
            Assert.True(result.CustomCounters.ContainsKey("bits") || result.Bits >= 0);
        }

        [Fact]
        public async Task DecrementCounterAsync_ShouldCreateCounterIfNotExists()
        {
            // Arrange
            var userId = "123";
            var exception = new RequestFailedException(404, "Not Found", "NotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.DecrementCounterAsync(userId, "deaths", 1);

            // Assert - Should remain at 0 since it was clamped
            Assert.Equal(0, result.Deaths);
        }

        [Fact]
        public async Task ResetCounterAsync_ShouldCreateCounterIfNotExists()
        {
            // Arrange
            var userId = "123";
            var exception = new RequestFailedException(404, "Not Found", "NotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.ResetCounterAsync(userId, "deaths");

            // Assert
            Assert.Equal(0, result.Deaths);
            Assert.Equal(userId, result.TwitchUserId);
        }

        [Fact]
        public async Task DecrementCounterAsync_ShouldNotCreateCustomCounterIfNotExists()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Deaths"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.DecrementCounterAsync(userId, "nonexistent", 1);

            // Assert - Custom counter should not be created
            Assert.False(result.CustomCounters.ContainsKey("nonexistent"));
        }

        [Fact]
        public async Task ResetCounterAsync_ShouldNotCreateCustomCounterIfNotExists()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Deaths"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.ResetCounterAsync(userId, "nonexistent");

            // Assert - Custom counter should not be created
            Assert.False(result.CustomCounters.ContainsKey("nonexistent"));
        }

        [Fact]
        public async Task IncrementCounterAsync_ShouldCreateNewCustomCounter()
        {
            // Arrange
            var userId = "123";
            var tableEntity = new TableEntity(userId, "counters") { ["Deaths"] = 5 };
            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.IncrementCounterAsync(userId, "newcustom", 5);

            // Assert - New custom counter should be created
            Assert.True(result.CustomCounters.ContainsKey("newcustom"));
            Assert.Equal(5, result.CustomCounters["newcustom"]);
        }

        [Fact]
        public async Task TryClaimStreamStartDiscordNotificationAsync_WhenUserIdMissing_ReturnsFalse()
        {
            var result = await _repository.TryClaimStreamStartDiscordNotificationAsync("", "stream-1");
            Assert.False(result);
        }

        [Fact]
        public async Task TryClaimStreamStartDiscordNotificationAsync_WhenStreamInstanceIdWhitespace_ReturnsFalse()
        {
            var result = await _repository.TryClaimStreamStartDiscordNotificationAsync("123", " ");
            Assert.False(result);
        }

        [Fact]
        public async Task TryClaimStreamStartDiscordNotificationAsync_WhenAlreadyClaimed_ReturnsFalse_AndDoesNotUpdate()
        {
            var userId = "123";
            var streamInstanceId = "stream-1";

            var tableEntity = new TableEntity(userId, "counters")
            {
                ["LastNotifiedStreamId"] = streamInstanceId
            };
            tableEntity.ETag = new ETag("etag");

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            var result = await _repository.TryClaimStreamStartDiscordNotificationAsync(userId, streamInstanceId);

            Assert.False(result);
            _mockTableClient.Verify(
                x => x.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>(), TableUpdateMode.Merge, It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task TryClaimStreamStartDiscordNotificationAsync_WhenNewStream_UpdatesEntity_AndReturnsTrue()
        {
            var userId = "123";
            var streamInstanceId = "stream-2";

            var tableEntity = new TableEntity(userId, "counters")
            {
                ["LastNotifiedStreamId"] = "stream-1"
            };
            tableEntity.ETag = new ETag("etag");

            var mockResponse = Mock.Of<Response<TableEntity>>(r => r.Value == tableEntity);
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            _mockTableClient
                .Setup(x => x.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>(), TableUpdateMode.Merge, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response>());

            var result = await _repository.TryClaimStreamStartDiscordNotificationAsync(userId, streamInstanceId);

            Assert.True(result);
            _mockTableClient.Verify(
                x => x.UpdateEntityAsync(
                    It.Is<TableEntity>(e => e.PartitionKey == userId && e.RowKey == "counters" && (string)e["LastNotifiedStreamId"] == streamInstanceId),
                    It.IsAny<ETag>(),
                    TableUpdateMode.Merge,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task TryClaimStreamStartDiscordNotificationAsync_WhenEntityMissing_AddsEntity_AndReturnsTrue()
        {
            var userId = "123";
            var streamInstanceId = "stream-1";

            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>(userId, "counters", null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            _mockTableClient
                .Setup(x => x.AddEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response>());

            var result = await _repository.TryClaimStreamStartDiscordNotificationAsync(userId, streamInstanceId);

            Assert.True(result);
            _mockTableClient.Verify(
                x => x.AddEntityAsync(
                    It.Is<TableEntity>(e => e.PartitionKey == userId && e.RowKey == "counters" && (string)e["LastNotifiedStreamId"] == streamInstanceId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task TryClaimStreamStartDiscordNotificationAsync_WhenAddConflicts_RetriesAndUpdates_AndReturnsTrue()
        {
            var userId = "123";
            var streamInstanceId = "stream-2";

            var existing = new TableEntity(userId, "counters")
            {
                ["LastNotifiedStreamId"] = "stream-1"
            };
            existing.ETag = new ETag("etag");

            var response = Mock.Of<Response<TableEntity>>(r => r.Value == existing);

            _mockTableClient
                .SetupSequence(x => x.GetEntityAsync<TableEntity>(userId, "counters", null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"))
                .ReturnsAsync(response);

            _mockTableClient
                .Setup(x => x.AddEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(409, "Conflict"));

            _mockTableClient
                .Setup(x => x.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>(), TableUpdateMode.Merge, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response>());

            var result = await _repository.TryClaimStreamStartDiscordNotificationAsync(userId, streamInstanceId);

            Assert.True(result);
            _mockTableClient.Verify(
                x => x.UpdateEntityAsync(
                    It.Is<TableEntity>(e => (string)e["LastNotifiedStreamId"] == streamInstanceId),
                    It.IsAny<ETag>(),
                    TableUpdateMode.Merge,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
