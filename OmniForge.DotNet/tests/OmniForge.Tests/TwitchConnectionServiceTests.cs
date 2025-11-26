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
    }
}
