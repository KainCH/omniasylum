using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using System;
using Xunit;

namespace OmniForge.Tests
{
    public class MonitoringRegistryTests
    {
        [Fact]
        public void SetState_ThenTryGetState_ShouldReturnState()
        {
            var registry = new MonitoringRegistry();
            var state = new MonitoringState(UseBot: true, BotUserId: "bot-id", UpdatedAtUtc: DateTimeOffset.UtcNow);

            registry.SetState("broadcaster1", state);

            Assert.True(registry.TryGetState("broadcaster1", out var actual));
            Assert.True(actual.UseBot);
            Assert.Equal("bot-id", actual.BotUserId);
        }

        [Fact]
        public void Remove_ShouldDeleteState()
        {
            var registry = new MonitoringRegistry();
            registry.SetState("broadcaster1", new MonitoringState(UseBot: false, BotUserId: null, UpdatedAtUtc: DateTimeOffset.UtcNow));

            registry.Remove("broadcaster1");

            Assert.False(registry.TryGetState("broadcaster1", out _));
        }

        [Fact]
        public void GetBroadcastersUsingBot_ShouldReturnOnlyBotBroadcasters()
        {
            var registry = new MonitoringRegistry();
            registry.SetState("a", new MonitoringState(UseBot: true, BotUserId: "bot", UpdatedAtUtc: DateTimeOffset.UtcNow));
            registry.SetState("b", new MonitoringState(UseBot: false, BotUserId: null, UpdatedAtUtc: DateTimeOffset.UtcNow));

            var broadcasters = string.Join(",", registry.GetBroadcastersUsingBot());

            Assert.Equal("a", broadcasters);
        }

        [Fact]
        public void SetState_WhenBroadcasterIdBlank_ShouldDoNothing()
        {
            var registry = new MonitoringRegistry();
            registry.SetState(" ", new MonitoringState(UseBot: true, BotUserId: "bot", UpdatedAtUtc: DateTimeOffset.UtcNow));

            Assert.Empty(registry.GetAllStates());
        }

        [Fact]
        public void Remove_WhenBroadcasterIdBlank_ShouldDoNothing()
        {
            var registry = new MonitoringRegistry();
            registry.SetState("a", new MonitoringState(UseBot: true, BotUserId: "bot", UpdatedAtUtc: DateTimeOffset.UtcNow));

            registry.Remove(" ");

            Assert.True(registry.TryGetState("a", out _));
        }

        [Fact]
        public void TryGetState_IsCaseInsensitive()
        {
            var registry = new MonitoringRegistry();
            registry.SetState("BroadCaster", new MonitoringState(UseBot: true, BotUserId: "bot", UpdatedAtUtc: DateTimeOffset.UtcNow));

            Assert.True(registry.TryGetState("broadcaster", out var state));
            Assert.True(state.UseBot);
        }
    }
}
