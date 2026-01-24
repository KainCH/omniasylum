using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services.EventHandlers;
using Xunit;

namespace OmniForge.Tests.EventHandlers
{
    public class ChannelUpdateHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();
        private readonly Mock<IServiceScope> _mockScope = new();
        private readonly Mock<IServiceProvider> _mockServiceProvider = new();
        private readonly Mock<ILogger<ChannelUpdateHandler>> _mockLogger = new();
        private readonly Mock<IGameSwitchService> _mockGameSwitchService = new();

        private readonly ChannelUpdateHandler _handler;

        public ChannelUpdateHandlerTests()
        {
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IGameSwitchService))).Returns(_mockGameSwitchService.Object);

            _handler = new ChannelUpdateHandler(_mockScopeFactory.Object, _mockLogger.Object);
        }

        [Fact]
        public void SubscriptionType_IsChannelUpdate()
        {
            Assert.Equal("channel.update", _handler.SubscriptionType);
        }

        [Fact]
        public async Task HandleAsync_WhenBroadcasterIdMissing_DoesNothing()
        {
            var eventData = JsonDocument.Parse("{}").RootElement;

            await _handler.HandleAsync(eventData);

            _mockGameSwitchService.Verify(x => x.HandleGameDetectedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenCategoryIdMissing_DoesNothing()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""category_name"": ""Some Game""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockGameSwitchService.Verify(x => x.HandleGameDetectedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_WhenValid_CallsGameSwitchService()
        {
            var eventData = JsonDocument.Parse(@"{
                ""broadcaster_user_id"": ""123"",
                ""category_id"": ""game1"",
                ""category_name"": ""Test Game""
            }").RootElement;

            await _handler.HandleAsync(eventData);

            _mockGameSwitchService.Verify(x => x.HandleGameDetectedAsync("123", "game1", "Test Game", It.IsAny<string?>()), Times.Once);
        }
    }
}
