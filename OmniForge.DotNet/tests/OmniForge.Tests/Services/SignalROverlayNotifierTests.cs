using Microsoft.AspNetCore.SignalR;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Web.Hubs;
using OmniForge.Web.Services;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services
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
        public async Task NotifyCounterUpdateAsync_ShouldSendToGroup()
        {
            var counter = new Counter { TwitchUserId = "123" };
            await _notifier.NotifyCounterUpdateAsync("123", counter);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("counterUpdate", It.Is<object[]>(o => o[0] == counter), default), Times.Once);
        }

        [Fact]
        public async Task NotifyMilestoneReachedAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyMilestoneReachedAsync("123", "deaths", 10, 11, 10);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("milestoneReached", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifySettingsUpdateAsync_ShouldSendToGroup()
        {
            var settings = new OverlaySettings();
            await _notifier.NotifySettingsUpdateAsync("123", settings);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("overlaySettingsUpdate", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifyStreamStatusUpdateAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyStreamStatusUpdateAsync("123", "live");

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("streamStatusUpdate", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifyStreamStartedAsync_ShouldSendToGroup()
        {
            var counter = new Counter();
            await _notifier.NotifyStreamStartedAsync("123", counter);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("streamStarted", It.Is<object[]>(o => o[0] == counter), default), Times.Once);
        }

        [Fact]
        public async Task NotifyStreamEndedAsync_ShouldSendToGroup()
        {
            var counter = new Counter();
            await _notifier.NotifyStreamEndedAsync("123", counter);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("streamEnded", It.Is<object[]>(o => o[0] == counter), default), Times.Once);
        }

        [Fact]
        public async Task NotifyFollowerAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyFollowerAsync("123", "follower");

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("newFollower", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifySubscriberAsync_ShouldSendToGroup()
        {
            await _notifier.NotifySubscriberAsync("123", "sub", "1000", false);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("newSubscriber", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifyResubAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyResubAsync("123", "sub", 5, "1000", "msg");

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("resub", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifyGiftSubAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyGiftSubAsync("123", "gifter", "recipient", "1000", 1);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("giftSub", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifyBitsAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyBitsAsync("123", "cheerer", 100, "msg", 1000);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("bitsReceived", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifyRaidAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyRaidAsync("123", "raider", 10);

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("raidReceived", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task NotifyCustomAlertAsync_ShouldSendToGroup()
        {
            await _notifier.NotifyCustomAlertAsync("123", "custom", new { });

            _mockClients.Verify(x => x.Group("user:123"), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("customAlert", It.IsAny<object[]>(), default), Times.Once);
        }
    }
}
