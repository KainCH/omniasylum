using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class ChatterEmailCacheServiceTests
    {
        private readonly Mock<ITwitchApiService> _mockTwitchApiService;
        private readonly Mock<ILogger<ChatterEmailCacheService>> _mockLogger;
        private readonly ChatterEmailCacheService _service;

        public ChatterEmailCacheServiceTests()
        {
            _mockTwitchApiService = new Mock<ITwitchApiService>();
            _mockLogger = new Mock<ILogger<ChatterEmailCacheService>>();
            _service = new ChatterEmailCacheService(_mockTwitchApiService.Object, _mockLogger.Object);
        }

        #region SyncChattersAsync Tests

        [Fact]
        public async Task SyncChattersAsync_ShouldReturnZero_WhenNoChattersFound()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            _mockTwitchApiService
                .Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = new List<HelixChatter>() });

            // Act
            var result = await _service.SyncChattersAsync(broadcasterId);

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task SyncChattersAsync_ShouldReturnChatterCount_WhenChattersFound()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            var chatters = new List<HelixChatter>
            {
                new HelixChatter { UserId = "user1", UserLogin = "user1", UserName = "User One" },
                new HelixChatter { UserId = "user2", UserLogin = "user2", UserName = "User Two" },
                new HelixChatter { UserId = "user3", UserLogin = "user3", UserName = "User Three" }
            };

            _mockTwitchApiService
                .Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = chatters });
            _mockTwitchApiService
                .Setup(x => x.GetUsersByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new HelixUsersResponse { Data = new List<HelixUser>() });

            // Act
            var result = await _service.SyncChattersAsync(broadcasterId);

            // Assert
            Assert.Equal(3, result);
        }

        [Fact]
        public async Task SyncChattersAsync_ShouldCacheUserEmails_WhenAvailable()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            var chatters = new List<HelixChatter>
            {
                new HelixChatter { UserId = "user1", UserLogin = "user1", UserName = "User One" }
            };
            var users = new List<HelixUser>
            {
                new HelixUser
                {
                    Id = "user1",
                    Login = "user1",
                    DisplayName = "User One",
                    Email = "user1@example.com",
                    ProfileImageUrl = "https://example.com/image.png"
                }
            };

            _mockTwitchApiService
                .Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = chatters });
            _mockTwitchApiService
                .Setup(x => x.GetUsersByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new HelixUsersResponse { Data = users });

            // Act
            await _service.SyncChattersAsync(broadcasterId);

            // Assert
            var cachedUser = _service.GetUserByEmail(broadcasterId, "user1@example.com");
            Assert.NotNull(cachedUser);
            Assert.Equal("User One", cachedUser.DisplayName);
            Assert.Equal("user1@example.com", cachedUser.Email);
        }

        [Fact]
        public async Task SyncChattersAsync_ShouldContinue_WhenGetUsersFails()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            var chatters = new List<HelixChatter>
            {
                new HelixChatter { UserId = "user1", UserLogin = "user1", UserName = "User One" }
            };

            _mockTwitchApiService
                .Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = chatters });
            _mockTwitchApiService
                .Setup(x => x.GetUsersByIdsAsync(It.IsAny<List<string>>()))
                .ThrowsAsync(new Exception("API error"));

            // Act
            var result = await _service.SyncChattersAsync(broadcasterId);

            // Assert - Should still return chatter count even if email lookup fails
            Assert.Equal(1, result);
        }

        [Fact]
        public async Task SyncChattersAsync_ShouldReturnZero_WhenExceptionOccurs()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            _mockTwitchApiService
                .Setup(x => x.GetChattersAsync(broadcasterId))
                .ThrowsAsync(new Exception("Connection failed"));

            // Act
            var result = await _service.SyncChattersAsync(broadcasterId);

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task SyncChattersAsync_ShouldHandleEmailCaseInsensitivity()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            var chatters = new List<HelixChatter>
            {
                new HelixChatter { UserId = "user1", UserLogin = "user1", UserName = "User One" }
            };
            var users = new List<HelixUser>
            {
                new HelixUser { Id = "user1", Login = "user1", DisplayName = "User One", Email = "USER1@EXAMPLE.COM" }
            };

            _mockTwitchApiService
                .Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = chatters });
            _mockTwitchApiService
                .Setup(x => x.GetUsersByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new HelixUsersResponse { Data = users });

            // Act
            await _service.SyncChattersAsync(broadcasterId);

            // Assert - Should find user regardless of email case
            var cachedUser = _service.GetUserByEmail(broadcasterId, "user1@example.com");
            Assert.NotNull(cachedUser);
        }

        #endregion

        #region GetUserByEmail Tests

        [Fact]
        public void GetUserByEmail_ShouldReturnNull_WhenEmailEmpty()
        {
            // Act
            var result = _service.GetUserByEmail("broadcaster123", "");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetUserByEmail_ShouldReturnNull_WhenEmailNull()
        {
            // Act
            var result = _service.GetUserByEmail("broadcaster123", null!);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetUserByEmail_ShouldReturnNull_WhenNotCached()
        {
            // Act
            var result = _service.GetUserByEmail("broadcaster123", "unknown@example.com");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetUserByEmail_ShouldReturnCachedUser()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            await PopulateCacheAsync(broadcasterId, "testuser@example.com", "TestUser");

            // Act
            var result = _service.GetUserByEmail(broadcasterId, "testuser@example.com");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("TestUser", result.DisplayName);
        }

        [Fact]
        public async Task GetUserByEmail_ShouldBeCaseInsensitive()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            await PopulateCacheAsync(broadcasterId, "testuser@example.com", "TestUser");

            // Act
            var result = _service.GetUserByEmail(broadcasterId, "TESTUSER@EXAMPLE.COM");

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region GetUserById Tests

        [Fact]
        public void GetUserById_ShouldReturnNull_WhenUserIdEmpty()
        {
            // Act
            var result = _service.GetUserById("broadcaster123", "");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetUserById_ShouldReturnNull_WhenNotCached()
        {
            // Act
            var result = _service.GetUserById("broadcaster123", "unknown_user_id");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetUserById_ShouldReturnCachedUser()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            await PopulateCacheAsync(broadcasterId, "test@example.com", "TestUser", "user456");

            // Act
            var result = _service.GetUserById(broadcasterId, "user456");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("TestUser", result.DisplayName);
        }

        #endregion

        #region Pending Donations Tests

        [Fact]
        public void StorePendingDonation_ShouldStoreDonation()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            var donation = new PendingPayPalDonation
            {
                TransactionId = "TXN123",
                PayerEmail = "donor@example.com",
                Amount = 10.00m
            };

            // Act
            _service.StorePendingDonation(broadcasterId, donation);

            // Assert
            var pendingDonations = _service.GetPendingDonations(broadcasterId);
            Assert.Single(pendingDonations);
            Assert.Equal("TXN123", pendingDonations.First().TransactionId);
        }

        [Fact]
        public async Task StorePendingDonation_ShouldMatchImmediately_WhenEmailCached()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            await PopulateCacheAsync(broadcasterId, "donor@example.com", "DonorUser", "donor123");

            var donation = new PendingPayPalDonation
            {
                TransactionId = "TXN123",
                PayerEmail = "donor@example.com",
                Amount = 10.00m
            };

            // Act
            _service.StorePendingDonation(broadcasterId, donation);

            // Assert
            Assert.NotNull(donation.MatchedUser);
            Assert.Equal("DonorUser", donation.MatchedUser.DisplayName);
        }

        [Fact]
        public void GetPendingDonations_ShouldReturnAllPendingDonations()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            _service.StorePendingDonation(broadcasterId, new PendingPayPalDonation { TransactionId = "TXN1", PayerEmail = "a@b.com", Amount = 5 });
            _service.StorePendingDonation(broadcasterId, new PendingPayPalDonation { TransactionId = "TXN2", PayerEmail = "c@d.com", Amount = 10 });
            _service.StorePendingDonation(broadcasterId, new PendingPayPalDonation { TransactionId = "TXN3", PayerEmail = "e@f.com", Amount = 15 });

            // Act
            var pendingDonations = _service.GetPendingDonations(broadcasterId).ToList();

            // Assert
            Assert.Equal(3, pendingDonations.Count);
        }

        [Fact]
        public void RemovePendingDonation_ShouldRemoveSpecificDonation()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            _service.StorePendingDonation(broadcasterId, new PendingPayPalDonation { TransactionId = "TXN1", PayerEmail = "a@b.com", Amount = 5 });
            _service.StorePendingDonation(broadcasterId, new PendingPayPalDonation { TransactionId = "TXN2", PayerEmail = "c@d.com", Amount = 10 });

            // Act
            _service.RemovePendingDonation(broadcasterId, "TXN1");

            // Assert
            var pendingDonations = _service.GetPendingDonations(broadcasterId).ToList();
            Assert.Single(pendingDonations);
            Assert.Equal("TXN2", pendingDonations.First().TransactionId);
        }

        [Fact]
        public async Task SyncChattersAsync_ShouldMatchPendingDonations()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            var donation = new PendingPayPalDonation
            {
                TransactionId = "TXN123",
                PayerEmail = "donor@example.com",
                Amount = 10.00m
            };
            _service.StorePendingDonation(broadcasterId, donation);

            var chatters = new List<HelixChatter>
            {
                new HelixChatter { UserId = "donor_id", UserLogin = "donoruser", UserName = "DonorUser" }
            };
            var users = new List<HelixUser>
            {
                new HelixUser { Id = "donor_id", Login = "donoruser", DisplayName = "DonorUser", Email = "donor@example.com" }
            };

            _mockTwitchApiService.Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = chatters });
            _mockTwitchApiService.Setup(x => x.GetUsersByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new HelixUsersResponse { Data = users });

            // Act
            await _service.SyncChattersAsync(broadcasterId);

            // Assert - Pending donation should be matched
            var pendingDonations = _service.GetPendingDonations(broadcasterId).ToList();
            Assert.Single(pendingDonations);
            Assert.NotNull(pendingDonations.First().MatchedUser);
            Assert.Equal("DonorUser", pendingDonations.First().MatchedUser!.DisplayName);
        }

        #endregion

        #region Cache Management Tests

        [Fact]
        public void ClearCache_ShouldRemoveAllCacheForBroadcaster()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            _service.StorePendingDonation(broadcasterId, new PendingPayPalDonation { TransactionId = "TXN1", PayerEmail = "a@b.com", Amount = 5 });

            // Act
            _service.ClearCache(broadcasterId);

            // Assert
            var pendingDonations = _service.GetPendingDonations(broadcasterId);
            Assert.Empty(pendingDonations);
        }

        [Fact]
        public async Task ClearCache_ShouldNotAffectOtherBroadcasters()
        {
            // Arrange
            var broadcaster1 = "broadcaster1";
            var broadcaster2 = "broadcaster2";
            await PopulateCacheAsync(broadcaster1, "user1@example.com", "User1", "id1");
            await PopulateCacheAsync(broadcaster2, "user2@example.com", "User2", "id2");

            // Act
            _service.ClearCache(broadcaster1);

            // Assert
            Assert.Null(_service.GetUserByEmail(broadcaster1, "user1@example.com"));
            Assert.NotNull(_service.GetUserByEmail(broadcaster2, "user2@example.com"));
        }

        [Fact]
        public async Task GetStats_ShouldReturnCorrectCounts()
        {
            // Arrange
            var broadcasterId = "broadcaster123";
            var chatters = new List<HelixChatter>
            {
                new HelixChatter { UserId = "user1", UserLogin = "user1", UserName = "User1" },
                new HelixChatter { UserId = "user2", UserLogin = "user2", UserName = "User2" }
            };
            var users = new List<HelixUser>
            {
                new HelixUser { Id = "user1", Login = "user1", DisplayName = "User1", Email = "user1@example.com" }
            };

            _mockTwitchApiService.Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = chatters });
            _mockTwitchApiService.Setup(x => x.GetUsersByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new HelixUsersResponse { Data = users });

            await _service.SyncChattersAsync(broadcasterId);
            _service.StorePendingDonation(broadcasterId, new PendingPayPalDonation { TransactionId = "TXN1", PayerEmail = "test@test.com", Amount = 5 });

            // Act
            var stats = _service.GetStats(broadcasterId);

            // Assert
            Assert.Equal(2, stats.ChatterCount);
            Assert.Equal(1, stats.EmailCount);
            Assert.Equal(1, stats.PendingDonationCount);
            Assert.NotNull(stats.LastSyncAt);
        }

        [Fact]
        public void GetStats_ShouldReturnEmptyStats_WhenNoCacheExists()
        {
            // Act
            var stats = _service.GetStats("unknown_broadcaster");

            // Assert
            Assert.Equal(0, stats.ChatterCount);
            Assert.Equal(0, stats.EmailCount);
            Assert.Equal(0, stats.PendingDonationCount);
            Assert.Null(stats.LastSyncAt);
        }

        #endregion

        #region Broadcaster Isolation Tests

        [Fact]
        public async Task Cache_ShouldBeIsolatedByBroadcaster()
        {
            // Arrange
            var broadcaster1 = "broadcaster1";
            var broadcaster2 = "broadcaster2";
            await PopulateCacheAsync(broadcaster1, "shared@example.com", "User1", "id1");
            await PopulateCacheAsync(broadcaster2, "shared@example.com", "User2", "id2");

            // Act
            var user1 = _service.GetUserByEmail(broadcaster1, "shared@example.com");
            var user2 = _service.GetUserByEmail(broadcaster2, "shared@example.com");

            // Assert - Each broadcaster should have their own cache
            Assert.Equal("User1", user1?.DisplayName);
            Assert.Equal("User2", user2?.DisplayName);
        }

        [Fact]
        public void PendingDonations_ShouldBeIsolatedByBroadcaster()
        {
            // Arrange
            var broadcaster1 = "broadcaster1";
            var broadcaster2 = "broadcaster2";
            _service.StorePendingDonation(broadcaster1, new PendingPayPalDonation { TransactionId = "TXN1", PayerEmail = "a@b.com", Amount = 5 });
            _service.StorePendingDonation(broadcaster2, new PendingPayPalDonation { TransactionId = "TXN2", PayerEmail = "c@d.com", Amount = 10 });

            // Act
            var donations1 = _service.GetPendingDonations(broadcaster1).ToList();
            var donations2 = _service.GetPendingDonations(broadcaster2).ToList();

            // Assert
            Assert.Single(donations1);
            Assert.Single(donations2);
            Assert.Equal("TXN1", donations1.First().TransactionId);
            Assert.Equal("TXN2", donations2.First().TransactionId);
        }

        #endregion

        #region Helper Methods

        private async Task PopulateCacheAsync(string broadcasterId, string email, string displayName, string userId = "user123")
        {
            var chatters = new List<HelixChatter>
            {
                new HelixChatter { UserId = userId, UserLogin = displayName.ToLowerInvariant(), UserName = displayName }
            };
            var users = new List<HelixUser>
            {
                new HelixUser { Id = userId, Login = displayName.ToLowerInvariant(), DisplayName = displayName, Email = email }
            };

            _mockTwitchApiService.Setup(x => x.GetChattersAsync(broadcasterId))
                .ReturnsAsync(new HelixChattersResponse { Data = chatters });
            _mockTwitchApiService.Setup(x => x.GetUsersByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new HelixUsersResponse { Data = users });

            await _service.SyncChattersAsync(broadcasterId);
        }

        #endregion
    }
}
