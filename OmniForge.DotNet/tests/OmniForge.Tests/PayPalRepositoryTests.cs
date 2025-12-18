using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class PayPalRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient;
        private readonly Mock<TableClient> _mockDonationsTable;
        private readonly Mock<ILogger<PayPalRepository>> _mockLogger;
        private readonly PayPalRepository _repository;

        public PayPalRepositoryTests()
        {
            _mockServiceClient = new Mock<TableServiceClient>();
            _mockDonationsTable = new Mock<TableClient>();
            _mockLogger = new Mock<ILogger<PayPalRepository>>();

            _mockServiceClient.Setup(x => x.GetTableClient("paypaldonations")).Returns(_mockDonationsTable.Object);

            _repository = new PayPalRepository(_mockServiceClient.Object, _mockLogger.Object);
        }

        #region Donation Tests

        [Fact]
        public async Task SaveDonationAsync_ShouldCallUpsertEntity()
        {
            // Arrange
            var donation = CreateSampleDonation();

            _mockDonationsTable.Setup(x => x.UpsertEntityAsync(
                It.IsAny<PayPalDonationTableEntity>(),
                TableUpdateMode.Replace,
                default))
                .ReturnsAsync(Mock.Of<Response>());

            // Act
            await _repository.SaveDonationAsync(donation);

            // Assert
            _mockDonationsTable.Verify(
                x => x.UpsertEntityAsync(
                    It.Is<PayPalDonationTableEntity>(e => e.PartitionKey == donation.UserId),
                    TableUpdateMode.Replace,
                    default),
                Times.Once);
        }

        [Fact]
        public async Task GetDonationAsync_ShouldReturnDonation_WhenExists()
        {
            // Arrange
            var userId = "user123";
            var transactionId = "TXN456";
            var entity = CreateDonationEntity(userId, transactionId);
            var response = Response.FromValue(entity, Mock.Of<Response>());

            _mockDonationsTable.Setup(x => x.GetEntityAsync<PayPalDonationTableEntity>(userId, transactionId, null, default))
                .ReturnsAsync(response);

            // Act
            var result = await _repository.GetDonationAsync(userId, transactionId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(transactionId, result!.TransactionId);
            Assert.Equal(userId, result.UserId);
        }

        [Fact]
        public async Task GetDonationAsync_ShouldReturnNull_WhenNotExists()
        {
            // Arrange
            _mockDonationsTable.Setup(x => x.GetEntityAsync<PayPalDonationTableEntity>(It.IsAny<string>(), It.IsAny<string>(), null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not found"));

            // Act
            var result = await _repository.GetDonationAsync("user123", "nonexistent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task TransactionExistsAsync_ShouldReturnTrue_WhenExists()
        {
            // Arrange
            var userId = "user123";
            var transactionId = "TXN456";
            var entity = CreateDonationEntity(userId, transactionId);
            var response = Response.FromValue(entity, Mock.Of<Response>());

            _mockDonationsTable.Setup(x => x.GetEntityAsync<PayPalDonationTableEntity>(userId, transactionId, null, default))
                .ReturnsAsync(response);

            // Act
            var exists = await _repository.TransactionExistsAsync(userId, transactionId);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public async Task TransactionExistsAsync_ShouldReturnFalse_WhenNotExists()
        {
            // Arrange
            _mockDonationsTable.Setup(x => x.GetEntityAsync<PayPalDonationTableEntity>(It.IsAny<string>(), It.IsAny<string>(), null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not found"));

            // Act
            var exists = await _repository.TransactionExistsAsync("user123", "nonexistent");

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public async Task GetRecentDonationsAsync_ShouldReturnDonations()
        {
            // Arrange
            var userId = "user123";
            var entities = new List<PayPalDonationTableEntity>
            {
                CreateDonationEntity(userId, "TXN1", 10.00m),
                CreateDonationEntity(userId, "TXN2", 25.00m),
                CreateDonationEntity(userId, "TXN3", 50.00m)
            };

            var mockAsyncPageable = CreateAsyncPageable(entities);
            _mockDonationsTable.Setup(x => x.QueryAsync<PayPalDonationTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable);

            // Act
            var results = await _repository.GetRecentDonationsAsync(userId, 50);

            // Assert
            Assert.Equal(3, results.Count());
        }

        [Fact]
        public async Task GetRecentDonationsAsync_ShouldRespectLimit()
        {
            // Arrange
            var userId = "user123";
            var entities = new List<PayPalDonationTableEntity>
            {
                CreateDonationEntity(userId, "TXN1", 10.00m),
                CreateDonationEntity(userId, "TXN2", 25.00m),
                CreateDonationEntity(userId, "TXN3", 50.00m)
            };

            var mockAsyncPageable = CreateAsyncPageable(entities);
            _mockDonationsTable.Setup(x => x.QueryAsync<PayPalDonationTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable);

            // Act
            var results = await _repository.GetRecentDonationsAsync(userId, 2);

            // Assert
            Assert.Equal(2, results.Count());
        }

        [Fact]
        public async Task GetPendingNotificationsAsync_ShouldReturnPendingDonations()
        {
            // Arrange
            var userId = "user123";
            var entity = CreateDonationEntity(userId, "TXN1", 25.00m);
            entity.NotificationSent = false;
            entity.VerificationStatus = (int)PayPalVerificationStatus.Verified;

            var mockAsyncPageable = CreateAsyncPageable(new List<PayPalDonationTableEntity> { entity });
            _mockDonationsTable.Setup(x => x.QueryAsync<PayPalDonationTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable);

            // Act
            var results = await _repository.GetPendingNotificationsAsync(userId);

            // Assert
            Assert.Single(results);
        }

        [Fact]
        public async Task MarkNotificationSentAsync_ShouldUpdateDonation()
        {
            // Arrange
            var userId = "user123";
            var transactionId = "TXN456";
            var entity = CreateDonationEntity(userId, transactionId);
            var response = Response.FromValue(entity, Mock.Of<Response>());

            _mockDonationsTable.Setup(x => x.GetEntityAsync<PayPalDonationTableEntity>(userId, transactionId, null, default))
                .ReturnsAsync(response);
            _mockDonationsTable.Setup(x => x.UpsertEntityAsync(
                It.IsAny<PayPalDonationTableEntity>(),
                TableUpdateMode.Replace,
                default))
                .ReturnsAsync(Mock.Of<Response>());

            // Act
            await _repository.MarkNotificationSentAsync(userId, transactionId);

            // Assert
            _mockDonationsTable.Verify(x => x.UpsertEntityAsync(
                It.Is<PayPalDonationTableEntity>(e => e.NotificationSent == true),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task UpdateVerificationStatusAsync_ShouldUpdateDonation()
        {
            // Arrange
            var userId = "user123";
            var transactionId = "TXN456";
            var entity = CreateDonationEntity(userId, transactionId);
            var response = Response.FromValue(entity, Mock.Of<Response>());

            _mockDonationsTable.Setup(x => x.GetEntityAsync<PayPalDonationTableEntity>(userId, transactionId, null, default))
                .ReturnsAsync(response);
            _mockDonationsTable.Setup(x => x.UpsertEntityAsync(
                It.IsAny<PayPalDonationTableEntity>(),
                TableUpdateMode.Replace,
                default))
                .ReturnsAsync(Mock.Of<Response>());

            // Act
            await _repository.UpdateVerificationStatusAsync(userId, transactionId, PayPalVerificationStatus.Verified);

            // Assert
            _mockDonationsTable.Verify(x => x.UpsertEntityAsync(
                It.Is<PayPalDonationTableEntity>(e => e.VerificationStatus == (int)PayPalVerificationStatus.Verified),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task DeleteOldDonationsAsync_ShouldDeleteOldRecords()
        {
            // Arrange
            var userId = "user123";
            var oldEntity = CreateDonationEntity(userId, "TXN1", 25.00m);
            oldEntity.ReceivedAt = DateTimeOffset.UtcNow.AddDays(-100); // Old record

            var mockAsyncPageable = CreateAsyncPageable(new List<PayPalDonationTableEntity> { oldEntity });
            _mockDonationsTable.Setup(x => x.QueryAsync<PayPalDonationTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable);
            _mockDonationsTable.Setup(x => x.DeleteEntityAsync(userId, "TXN1", It.IsAny<ETag>(), default))
                .ReturnsAsync(Mock.Of<Response>());

            // Act
            var deleted = await _repository.DeleteOldDonationsAsync(userId, 90);

            // Assert
            Assert.Equal(1, deleted);
            _mockDonationsTable.Verify(x => x.DeleteEntityAsync(userId, "TXN1", It.IsAny<ETag>(), default), Times.Once);
        }

        [Fact]
        public async Task DeleteOldDonationsAsync_ShouldNotDeleteRecentRecords()
        {
            // Arrange
            var userId = "user123";
            var recentEntity = CreateDonationEntity(userId, "TXN1", 25.00m);
            recentEntity.ReceivedAt = DateTimeOffset.UtcNow.AddDays(-10); // Recent record

            var mockAsyncPageable = CreateAsyncPageable(new List<PayPalDonationTableEntity> { recentEntity });
            _mockDonationsTable.Setup(x => x.QueryAsync<PayPalDonationTableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable);

            // Act
            var deleted = await _repository.DeleteOldDonationsAsync(userId, 90);

            // Assert
            Assert.Equal(0, deleted);
            _mockDonationsTable.Verify(x => x.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), default), Times.Never);
        }

        [Fact]
        public async Task InitializeAsync_ShouldCreateTableIfNotExists()
        {
            // Arrange - CreateIfNotExistsAsync returns Response<TableItem>
            // We don't need to verify its return value, just that it was called

            // Act
            await _repository.InitializeAsync();

            // Assert - Since we're using a mock, we just verify the method was called
            _mockDonationsTable.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Helper Methods

        private static PayPalDonation CreateSampleDonation()
        {
            return new PayPalDonation
            {
                UserId = "user123",
                TransactionId = "TXN456",
                PayerEmail = "donor@example.com",
                PayerName = "Generous Donor",
                Amount = 25.00m,
                Currency = "USD",
                PaymentStatus = "Completed",
                ReceiverEmail = "streamer@example.com",
                Message = "Great stream!",
                ReceivedAt = DateTimeOffset.UtcNow
            };
        }

        private static PayPalDonationTableEntity CreateDonationEntity(string userId, string transactionId, decimal amount = 25.00m)
        {
            return new PayPalDonationTableEntity
            {
                PartitionKey = userId,
                RowKey = transactionId,
                PayerEmail = "donor@example.com",
                PayerName = "Generous Donor",
                Amount = (double)amount,
                Currency = "USD",
                PaymentStatus = "Completed",
                ReceiverEmail = "streamer@example.com",
                Message = "Great stream!",
                ReceivedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                NotificationSent = false,
                VerificationStatus = (int)PayPalVerificationStatus.Pending
            };
        }

        private static AsyncPageable<PayPalDonationTableEntity> CreateAsyncPageable(IEnumerable<PayPalDonationTableEntity> entities)
        {
            var pages = new List<Page<PayPalDonationTableEntity>>
            {
                Page<PayPalDonationTableEntity>.FromValues(entities.ToList(), null, Mock.Of<Response>())
            };
            return AsyncPageable<PayPalDonationTableEntity>.FromPages(pages);
        }

        #endregion
    }
}
