using System;
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
            var counterEntity = new CounterTableEntity
            {
                PartitionKey = userId,
                RowKey = "counters",
                Deaths = 5,
                Swears = 10
            };

            var mockResponse = Mock.Of<Response<CounterTableEntity>>(r => r.Value == counterEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<CounterTableEntity>(
                userId,
                "counters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(5, result.Deaths);
            Assert.Equal(10, result.Swears);
        }

        [Fact]
        public async Task GetCountersAsync_ShouldReturnDefault_WhenNotFound()
        {
            // Arrange
            var userId = "unknown";
            var exception = new RequestFailedException(404, "Not Found", "ResourceNotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<CounterTableEntity>(
                userId,
                "counters",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.GetCountersAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(userId, result.TwitchUserId);
            Assert.Equal(0, result.Deaths); // Default value
        }

        [Fact]
        public async Task SaveCountersAsync_ShouldUpsertEntity()
        {
            // Arrange
            var counter = new Counter
            {
                TwitchUserId = "123",
                Deaths = 1,
                Swears = 2
            };

            _mockTableClient.Setup(x => x.UpsertEntityAsync(
                It.IsAny<CounterTableEntity>(),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response>());

            // Act
            await _repository.SaveCountersAsync(counter);

            // Assert
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<CounterTableEntity>(e => e.PartitionKey == "123" && e.Deaths == 1),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
