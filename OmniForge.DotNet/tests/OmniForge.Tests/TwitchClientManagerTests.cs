using System;
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
    public class TwitchClientManagerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ITwitchMessageHandler> _mockMessageHandler;
        private readonly Mock<ILogger<TwitchClientManager>> _mockLogger;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<ITwitchAuthService> _mockTwitchAuthService;
        private readonly TwitchClientManager _twitchClientManager;

        public TwitchClientManagerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockMessageHandler = new Mock<ITwitchMessageHandler>();
            _mockLogger = new Mock<ILogger<TwitchClientManager>>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockTwitchAuthService = new Mock<ITwitchAuthService>();

            // Setup Scope Factory
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);

            // Setup Service Provider to return our mocked repositories
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository)))
                .Returns(_mockUserRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository)))
                .Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ITwitchAuthService)))
                .Returns(_mockTwitchAuthService.Object);

            _twitchClientManager = new TwitchClientManager(
                _mockScopeFactory.Object,
                _mockMessageHandler.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task ConnectUserAsync_ShouldCreateScopeAndGetUser()
        {
            // Arrange
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                Username = "testuser",
                AccessToken = "token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1) // Valid token
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId))
                .ReturnsAsync(user);

            // Act
            await _twitchClientManager.ConnectUserAsync(userId);

            // Assert
            _mockScopeFactory.Verify(x => x.CreateScope(), Times.Once);
            _mockUserRepository.Verify(x => x.GetUserAsync(userId), Times.Once);
        }

        [Fact]
        public async Task ConnectUserAsync_ShouldLogWarning_WhenUserNotFound()
        {
            // Arrange
            var userId = "unknown";
            _mockUserRepository.Setup(x => x.GetUserAsync(userId))
                .ReturnsAsync((User?)null);

            // Act
            await _twitchClientManager.ConnectUserAsync(userId);

            // Assert
            _mockScopeFactory.Verify(x => x.CreateScope(), Times.Once);
            _mockUserRepository.Verify(x => x.GetUserAsync(userId), Times.Once);

            // Verify logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not found")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
