using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Web.Hubs;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class SignalROverlayNotifierTests
    {
        private readonly Mock<IHubContext<OverlayHub>> _mockHubContext;
        private readonly Mock<IHubClients> _mockClients;
        private readonly Mock<IClientProxy> _mockClientProxy;
        private readonly SignalROverlayNotifier _notifier;

        public SignalROverlayNotifierTests()
        {
            _mockHubContext = new Mock<IHubContext<OverlayHub>>();
            _mockClients = new Mock<IHubClients>();
            _mockClientProxy = new Mock<IClientProxy>();

            _mockHubContext.Setup(x => x.Clients).Returns(_mockClients.Object);
            _mockClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);

            _notifier = new SignalROverlayNotifier(_mockHubContext.Object);
        }

        [Fact]
        public async Task NotifyCounterUpdateAsync_ShouldSendUpdateToGroup()
        {
            // Arrange
            var userId = "123";
            var counter = new Counter { Deaths = 1 };

            // Act
            await _notifier.NotifyCounterUpdateAsync(userId, counter);

            // Assert
            _mockClients.Verify(x => x.Group($"user:{userId}"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync(
                "ReceiveCounterUpdate",
                It.Is<object[]>(o => o.Length == 1 && o[0] == counter),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
