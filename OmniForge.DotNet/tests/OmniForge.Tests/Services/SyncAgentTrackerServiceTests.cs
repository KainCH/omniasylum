using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class SyncAgentTrackerServiceTests
    {
        private readonly SyncAgentTrackerService _tracker;

        public SyncAgentTrackerServiceTests()
        {
            var mockScopeFactory = new Mock<IServiceScopeFactory>();
            var mockLogger = new Mock<ILogger<SyncAgentTrackerService>>();
            _tracker = new SyncAgentTrackerService(mockScopeFactory.Object, mockLogger.Object);
        }

        [Fact]
        public async Task RegisterAgent_ShouldMakeAgentConnected()
        {
            await _tracker.RegisterAgentAsync("user1", "conn1", "obs");

            Assert.True(_tracker.IsAgentConnected("user1"));
            var state = _tracker.GetAgentState("user1");
            Assert.NotNull(state);
            Assert.Equal("obs", state!.SoftwareType);
            Assert.Equal("conn1", state.ConnectionId);
        }

        [Fact]
        public async Task UnregisterAgent_ShouldDisconnect()
        {
            await _tracker.RegisterAgentAsync("user1", "conn1", "obs");
            await _tracker.UnregisterAgentAsync("user1", "conn1");

            Assert.False(_tracker.IsAgentConnected("user1"));
            Assert.Null(_tracker.GetAgentState("user1"));
        }

        [Fact]
        public async Task UnregisterAgent_WrongConnectionId_ShouldNotDisconnect()
        {
            await _tracker.RegisterAgentAsync("user1", "conn1", "obs");
            await _tracker.UnregisterAgentAsync("user1", "wrong-conn");

            Assert.True(_tracker.IsAgentConnected("user1"));
        }

        [Fact]
        public async Task UpdateCurrentScene_ShouldUpdateState()
        {
            await _tracker.RegisterAgentAsync("user1", "conn1", "obs");
            await _tracker.UpdateCurrentSceneAsync("user1", "Gaming");

            var state = _tracker.GetAgentState("user1");
            Assert.Equal("Gaming", state?.CurrentScene);
        }

        [Fact]
        public async Task RegisterAgent_ShouldOverridePrevious()
        {
            await _tracker.RegisterAgentAsync("user1", "conn1", "obs");
            await _tracker.RegisterAgentAsync("user1", "conn2", "streamlabs");

            var state = _tracker.GetAgentState("user1");
            Assert.Equal("conn2", state?.ConnectionId);
            Assert.Equal("streamlabs", state?.SoftwareType);
        }

        [Fact]
        public void GetAgentState_WhenNotConnected_ShouldReturnNull()
        {
            Assert.Null(_tracker.GetAgentState("nonexistent"));
            Assert.False(_tracker.IsAgentConnected("nonexistent"));
        }
    }
}
