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
    public class UserRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient;
        private readonly Mock<TableClient> _mockTableClient;
        private readonly UserRepository _repository;

        public UserRepositoryTests()
        {
            _mockTableServiceClient = new Mock<TableServiceClient>();
            _mockTableClient = new Mock<TableClient>();

            _mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>()))
                .Returns(_mockTableClient.Object);

            _repository = new UserRepository(_mockTableServiceClient.Object);
        }

        [Fact]
        public async Task GetUserAsync_ShouldReturnUser_WhenUserExists()
        {
            // Arrange
            var userId = "123";
            var userEntity = new UserTableEntity
            {
                PartitionKey = "user",
                RowKey = userId,
                TwitchUserId = userId,
                Username = "testuser",
                Features = "{}",
                OverlaySettings = "{}"
            };

            var mockResponse = Mock.Of<Response<UserTableEntity>>(r => r.Value == userEntity);

            _mockTableClient.Setup(x => x.GetEntityAsync<UserTableEntity>(
                "user",
                userId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetUserAsync(userId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(userId, result.TwitchUserId);
            Assert.Equal("testuser", result.Username);
        }

        [Fact]
        public async Task GetUserAsync_ShouldReturnNull_WhenUserNotFound()
        {
            // Arrange
            var userId = "unknown";
            var exception = new RequestFailedException(404, "Not Found", "ResourceNotFound", null);

            _mockTableClient.Setup(x => x.GetEntityAsync<UserTableEntity>(
                "user",
                userId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _repository.GetUserAsync(userId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SaveUserAsync_ShouldUpsertEntity()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "123",
                Username = "testuser"
            };

            _mockTableClient.Setup(x => x.UpsertEntityAsync(
                It.IsAny<UserTableEntity>(),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response>());

            // Act
            await _repository.SaveUserAsync(user);

            // Assert
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<UserTableEntity>(e => e.TwitchUserId == "123" && e.Username == "testuser"),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
