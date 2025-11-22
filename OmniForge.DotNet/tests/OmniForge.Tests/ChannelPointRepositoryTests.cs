using System;
using System.Collections.Generic;
using System.Linq;
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
    public class ChannelPointRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockTableServiceClient;
        private readonly Mock<TableClient> _mockTableClient;
        private readonly ChannelPointRepository _repository;

        public ChannelPointRepositoryTests()
        {
            _mockTableServiceClient = new Mock<TableServiceClient>();
            _mockTableClient = new Mock<TableClient>();

            _mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>()))
                .Returns(_mockTableClient.Object);

            _repository = new ChannelPointRepository(_mockTableServiceClient.Object);
        }

        [Fact]
        public async Task GetRewardAsync_ShouldReturnReward_WhenExists()
        {
            // Arrange
            var userId = "123";
            var rewardId = "abc";
            var entity = new ChannelPointRewardTableEntity
            {
                PartitionKey = userId,
                RowKey = $"reward-{rewardId}",
                RewardId = rewardId,
                RewardTitle = "Test Reward",
                Cost = 100,
                Action = "increment_deaths"
            };

            var mockResponse = Mock.Of<Response<ChannelPointRewardTableEntity>>(r => r.Value == entity);

            _mockTableClient.Setup(x => x.GetEntityAsync<ChannelPointRewardTableEntity>(
                userId,
                $"reward-{rewardId}",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _repository.GetRewardAsync(userId, rewardId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(rewardId, result.RewardId);
            Assert.Equal("Test Reward", result.RewardTitle);
        }

        [Fact]
        public async Task SaveRewardAsync_ShouldUpsertEntity()
        {
            // Arrange
            var reward = new ChannelPointReward
            {
                UserId = "123",
                RewardId = "abc",
                RewardTitle = "Test Reward",
                Cost = 100,
                Action = "increment_deaths"
            };

            // Act
            await _repository.SaveRewardAsync(reward);

            // Assert
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<ChannelPointRewardTableEntity>(e =>
                    e.PartitionKey == "123" &&
                    e.RowKey == "reward-abc" &&
                    e.RewardTitle == "Test Reward"),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteRewardAsync_ShouldDeleteEntity()
        {
            // Arrange
            var userId = "123";
            var rewardId = "abc";

            // Act
            await _repository.DeleteRewardAsync(userId, rewardId);

            // Assert
            _mockTableClient.Verify(x => x.DeleteEntityAsync(
                userId,
                $"reward-{rewardId}",
                default,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetRewardsAsync_ShouldReturnRewards_WhenExist()
        {
            // Arrange
            var userId = "123";
            var rewardEntity = new ChannelPointRewardTableEntity
            {
                PartitionKey = userId,
                RowKey = "reward-abc",
                RewardId = "abc",
                RewardTitle = "Test Reward",
                Cost = 100
            };

            var page = Page<ChannelPointRewardTableEntity>.FromValues(new[] { rewardEntity }, null, Mock.Of<Response>());
            var asyncPageable = AsyncPageable<ChannelPointRewardTableEntity>.FromPages(new[] { page });

            _mockTableClient.Setup(x => x.QueryAsync<ChannelPointRewardTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(asyncPageable);

            // Act
            var result = await _repository.GetRewardsAsync(userId);

            // Assert
            Assert.Single(result);
            var reward = result.First(); // Requires System.Linq
            Assert.Equal("abc", reward.RewardId);
            Assert.Equal("Test Reward", reward.RewardTitle);
        }
    }
}
