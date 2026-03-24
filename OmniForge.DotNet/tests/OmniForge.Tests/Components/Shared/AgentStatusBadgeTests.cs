using Bunit;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Shared;
using Xunit;

namespace OmniForge.Tests.Components.Shared
{
    public class AgentStatusBadgeTests : BunitContext
    {
        private readonly Mock<ISyncAgentTracker> _mockTracker = new();

        public AgentStatusBadgeTests()
        {
            Services.AddSingleton(_mockTracker.Object);
        }

        [Fact]
        public void WhenAgentConnected_ShouldShowConnectedBadge()
        {
            _mockTracker.Setup(x => x.GetAgentState("user1"))
                .Returns(new AgentState { UserId = "user1", SoftwareType = "obs", CurrentScene = "Gaming" });

            var cut = Render(b =>
            {
                b.OpenComponent<AgentStatusBadge>(0);
                b.AddAttribute(1, nameof(AgentStatusBadge.UserId), "user1");
                b.CloseComponent();
            });

            Assert.Contains("bg-success", cut.Markup);
            Assert.Contains("Connected", cut.Markup);
            Assert.Contains("OBS", cut.Markup);
        }

        [Fact]
        public void WhenAgentDisconnected_ShouldShowDisconnectedBadge()
        {
            _mockTracker.Setup(x => x.GetAgentState("user1")).Returns((AgentState?)null);

            var cut = Render(b =>
            {
                b.OpenComponent<AgentStatusBadge>(0);
                b.AddAttribute(1, nameof(AgentStatusBadge.UserId), "user1");
                b.CloseComponent();
            });

            Assert.Contains("bg-secondary", cut.Markup);
            Assert.Contains("Disconnected", cut.Markup);
        }

        [Fact]
        public void WhenNoUserId_ShouldRenderNothing()
        {
            var cut = Render(b =>
            {
                b.OpenComponent<AgentStatusBadge>(0);
                b.AddAttribute(1, nameof(AgentStatusBadge.UserId), "");
                b.CloseComponent();
            });

            Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
        }
    }
}
