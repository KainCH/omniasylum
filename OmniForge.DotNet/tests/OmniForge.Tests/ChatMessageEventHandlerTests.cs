using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using OmniForge.Infrastructure.Services.EventHandlers;
using Xunit;

namespace OmniForge.Tests
{
    public class ChatMessageEventHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();
        private readonly Mock<IDiscordInviteSender> _mockDiscordInviteSender = new();
        private readonly Mock<IChatCommandProcessor> _mockChatCommandProcessor = new();
        private readonly Mock<ITwitchApiService> _mockTwitchApiService = new();
        private readonly Mock<IMonitoringRegistry> _mockMonitoringRegistry = new();
        private readonly Mock<ITwitchBotEligibilityService> _mockBotEligibilityService = new();
        private readonly Mock<IUserRepository> _mockUserRepository = new();
        private readonly Mock<ILogger<ChatMessageHandler>> _mockLogger = new();
        private readonly ChatMessageHandler _handler;

        public ChatMessageEventHandlerTests()
        {
            _handler = new ChatMessageHandler(
                _mockScopeFactory.Object,
                _mockLogger.Object,
                _mockDiscordInviteSender.Object,
                _mockChatCommandProcessor.Object,
                _mockTwitchApiService.Object,
                _mockMonitoringRegistry.Object,
                _mockBotEligibilityService.Object,
                _mockUserRepository.Object);
        }

        [Fact]
        public async Task HandleAsync_ShouldInvokeProcessor_ForModCommand()
        {
            // Arrange
            var json = @"{
                ""broadcaster_user_id"": ""broadcaster123"",
                ""chatter_user_id"": ""mod456"",
                ""message"": { ""text"": ""!death+"" },
                ""badges"": [ { ""set_id"": ""moderator"" } ]
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            ChatCommandContext? capturedContext = null;
            _mockChatCommandProcessor
                .Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Callback<ChatCommandContext, System.Func<string, string, Task>>((ctx, sender) => capturedContext = ctx)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.HandleAsync(evt);

            // Assert
            Assert.NotNull(capturedContext);
            Assert.Equal("broadcaster123", capturedContext!.UserId);
            Assert.Equal("!death+", capturedContext.Message);
            Assert.True(capturedContext.IsModerator);
            Assert.False(capturedContext.IsBroadcaster);
            Assert.False(capturedContext.IsSubscriber);
        }

        [Fact]
        public async Task HandleAsync_ShouldTreatBroadcasterAsMod()
        {
            // Arrange
            var json = @"{
                ""broadcaster_user_id"": ""broadcaster123"",
                ""chatter_user_id"": ""broadcaster123"",
                ""message"": { ""text"": ""!stats"" },
                ""badges"": []
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            ChatCommandContext? capturedContext = null;
            _mockChatCommandProcessor
                .Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Callback<ChatCommandContext, System.Func<string, string, Task>>((ctx, sender) => capturedContext = ctx)
                .Returns(Task.CompletedTask);

            // Act
            await _handler.HandleAsync(evt);

            // Assert
            Assert.NotNull(capturedContext);
            Assert.Equal("broadcaster123", capturedContext!.UserId);
            Assert.True(capturedContext.IsBroadcaster);
            Assert.True(capturedContext.IsModerator); // broadcaster implies mod for commands
        }

        [Fact]
        public async Task HandleAsync_ShouldFlagSubscriber_WhenBadgePresent()
        {
            var json = @"{
                ""broadcaster_user_id"": ""broadcaster123"",
                ""chatter_user_id"": ""user999"",
                ""message"": { ""text"": ""!custom"" },
                ""badges"": [ { ""set_id"": ""subscriber"" } ]
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            ChatCommandContext? capturedContext = null;
            _mockChatCommandProcessor
                .Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Callback<ChatCommandContext, System.Func<string, string, Task>>((ctx, sender) => capturedContext = ctx)
                .Returns(Task.CompletedTask);

            await _handler.HandleAsync(evt);

            Assert.NotNull(capturedContext);
            Assert.True(capturedContext!.IsSubscriber);
        }

        [Fact]
        public async Task HandleAsync_ShouldSendChatReply_ForInfoCommand()
        {
            var json = @"{
                ""broadcaster_user_id"": ""broadcaster123"",
                ""chatter_user_id"": ""user999"",
                ""message_id"": ""msg-1"",
                ""message"": { ""text"": ""!custom"" },
                ""badges"": []
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            _mockChatCommandProcessor
                .Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Callback<ChatCommandContext, System.Func<string, string, Task>>(async (ctx, sender) =>
                {
                    await sender(ctx.UserId, "Custom Response");
                })
                .Returns(Task.CompletedTask);

            _mockMonitoringRegistry
                .Setup(r => r.TryGetState("broadcaster123", out It.Ref<MonitoringState>.IsAny))
                .Returns(false);

            _mockUserRepository
                .Setup(r => r.GetUserAsync("broadcaster123"))
                .ReturnsAsync(new User { TwitchUserId = "broadcaster123", AccessToken = "broadcaster-access-token" });

            _mockBotEligibilityService
                .Setup(s => s.GetEligibilityAsync("broadcaster123", "broadcaster-access-token", default))
                .ReturnsAsync(new BotEligibilityResult(true, "bot-1", "ok"));

            await _handler.HandleAsync(evt);

            _mockTwitchApiService.Verify(s => s.SendChatMessageAsBotAsync("broadcaster123", "bot-1", "Custom Response", "msg-1"), Times.Once);
            _mockTwitchApiService.Verify(s => s.SendChatMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_ShouldSendChatReply_UsingMonitoringRegistryBotState()
        {
            var json = @"{
                ""broadcaster_user_id"": ""broadcaster123"",
                ""chatter_user_id"": ""user999"",
                ""message_id"": ""msg-1"",
                ""message"": { ""text"": ""!custom"" },
                ""badges"": []
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            _mockChatCommandProcessor
                .Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Callback<ChatCommandContext, System.Func<string, string, Task>>(async (ctx, sender) =>
                {
                    await sender(ctx.UserId, "Custom Response");
                })
                .Returns(Task.CompletedTask);

            var state = new MonitoringState(true, "bot-1", System.DateTimeOffset.UtcNow);
            _mockMonitoringRegistry
                .Setup(r => r.TryGetState("broadcaster123", out state))
                .Returns(true);

            await _handler.HandleAsync(evt);

            _mockTwitchApiService.Verify(s => s.SendChatMessageAsBotAsync("broadcaster123", "bot-1", "Custom Response", "msg-1"), Times.Once);
            _mockTwitchApiService.Verify(s => s.SendChatMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_ShouldNotSendChatReply_ForCounterCommand()
        {
            var json = @"{
                ""broadcaster_user_id"": ""broadcaster123"",
                ""chatter_user_id"": ""user999"",
                ""message_id"": ""msg-1"",
                ""message"": { ""text"": ""!deaths"" },
                ""badges"": []
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            _mockChatCommandProcessor
                .Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Returns(Task.CompletedTask);

            await _handler.HandleAsync(evt);

            _mockTwitchApiService.Verify(s => s.SendChatMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_ShouldSendDiscordInvite_OnKeyword()
        {
            var json = @"{
                ""broadcaster_user_id"": ""broadcaster123"",
                ""chatter_user_id"": ""user999"",
                ""message"": { ""text"": ""!discord"" },
                ""badges"": []
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            await _handler.HandleAsync(evt);

            _mockDiscordInviteSender.Verify(s => s.SendDiscordInviteAsync("broadcaster123"), Times.Once);
        }
    }
}
