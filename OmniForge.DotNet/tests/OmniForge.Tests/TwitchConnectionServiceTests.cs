using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchConnectionServiceTests
    {
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
        private readonly Mock<ILogger<TwitchConnectionService>> _mockLogger;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly TwitchConnectionService _service;

        public TwitchConnectionServiceTests()
        {
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();
            _mockLogger = new Mock<ILogger<TwitchConnectionService>>();
            _mockUserRepository = new Mock<IUserRepository>();

            // Setup Service Provider to return Scope Factory
            _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
                .Returns(_mockScopeFactory.Object);

            // Setup Scope Factory to return Scope
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);

            // Setup Service Provider to return UserRepository
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository)))
                .Returns(_mockUserRepository.Object);

            _service = new TwitchConnectionService(
                _mockServiceProvider.Object,
                _mockTwitchClientManager.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task ConnectAllUsersAsync_ShouldConnectActiveUsers()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", Username = "user1", IsActive = true, AccessToken = "token" },
                new User { TwitchUserId = "2", Username = "user2", IsActive = false, AccessToken = "token" }, // Inactive
                new User { TwitchUserId = "3", Username = "user3", IsActive = true, AccessToken = "" } // No token
            };

            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(users);

            // Act
            await _service.ConnectAllUsersAsync();

            // Assert
            // Should connect user1
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("1"), Times.Once);

            // Should NOT connect user2 (inactive)
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("2"), Times.Never);

            // Should NOT connect user3 (no token)
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("3"), Times.Never);
        }

        [Fact]
        public async Task ConnectAllUsersAsync_ShouldHandleEmptyUsersList()
        {
            // Arrange
            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(new List<User>());

            // Act
            await _service.ConnectAllUsersAsync();

            // Assert
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ConnectAllUsersAsync_ShouldHandleRepositoryException()
        {
            // Arrange
            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ThrowsAsync(new Exception("Database error"));

            // Act - should not throw
            await _service.ConnectAllUsersAsync();

            // Assert - should log error
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ConnectAllUsersAsync_ShouldStopOnCancellation()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var users = new List<User>
            {
                new User { TwitchUserId = "1", Username = "user1", IsActive = true, AccessToken = "token" },
                new User { TwitchUserId = "2", Username = "user2", IsActive = true, AccessToken = "token" }
            };

            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(users);

            // Cancel before starting
            cts.Cancel();

            // Act
            await _service.ConnectAllUsersAsync(cts.Token);

            // Assert - should not attempt to connect after cancellation
            // The first user may or may not connect depending on timing
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("2"), Times.Never);
        }

        [Fact]
        public async Task ConnectAllUsersAsync_ShouldConnectMultipleActiveUsers()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", Username = "user1", IsActive = true, AccessToken = "token1" },
                new User { TwitchUserId = "2", Username = "user2", IsActive = true, AccessToken = "token2" },
                new User { TwitchUserId = "3", Username = "user3", IsActive = true, AccessToken = "token3" }
            };

            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(users);

            // Act
            await _service.ConnectAllUsersAsync();

            // Assert - should connect all active users
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("1"), Times.Once);
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("2"), Times.Once);
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("3"), Times.Once);
        }

        [Fact]
        public async Task ConnectAllUsersAsync_ShouldLogUserConnections()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", Username = "testuser", IsActive = true, AccessToken = "token" }
            };

            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(users);

            // Act
            await _service.ConnectAllUsersAsync();

            // Assert - should log connection attempt
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Connecting Twitch bot")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
