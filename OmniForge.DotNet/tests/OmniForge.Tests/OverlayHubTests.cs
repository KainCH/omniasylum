using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using OmniForge.Web.Hubs;
using Xunit;

namespace OmniForge.Tests
{
    public class OverlayHubTests
    {
        private readonly OverlayHub _hub;
        private readonly Mock<IGroupManager> _mockGroups;
        private readonly Mock<HubCallerContext> _mockContext;

        public OverlayHubTests()
        {
            _mockGroups = new Mock<IGroupManager>();
            _mockContext = new Mock<HubCallerContext>();

            _hub = new OverlayHub
            {
                Groups = _mockGroups.Object,
                Context = _mockContext.Object
            };
        }

        [Fact]
        public async Task JoinGroup_ShouldAddToGroup()
        {
            // Arrange
            var userId = "123";
            var connectionId = "conn1";
            _mockContext.Setup(x => x.ConnectionId).Returns(connectionId);

            // Act
            await _hub.JoinGroup(userId);

            // Assert
            _mockGroups.Verify(x => x.AddToGroupAsync(connectionId, $"user:{userId}", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task LeaveGroup_ShouldRemoveFromGroup()
        {
            // Arrange
            var userId = "123";
            var connectionId = "conn1";
            _mockContext.Setup(x => x.ConnectionId).Returns(connectionId);

            // Act
            await _hub.LeaveGroup(userId);

            // Assert
            _mockGroups.Verify(x => x.RemoveFromGroupAsync(connectionId, $"user:{userId}", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
