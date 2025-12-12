using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
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
        private readonly Mock<ILogger<ChatMessageHandler>> _mockLogger = new();
        private readonly ChatMessageHandler _handler;

        public ChatMessageEventHandlerTests()
        {
            _handler = new ChatMessageHandler(
                _mockScopeFactory.Object,
                _mockLogger.Object,
                _mockDiscordInviteSender.Object,
                _mockChatCommandProcessor.Object,
                _mockTwitchApiService.Object);
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
                ""message"": { ""text"": ""!deaths"" },
                ""badges"": []
            }";

            using var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement;

            _mockChatCommandProcessor
                .Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Callback<ChatCommandContext, System.Func<string, string, Task>>(async (ctx, sender) =>
                {
                    await sender(ctx.UserId, "Death Count: 1");
                })
                .Returns(Task.CompletedTask);

            await _handler.HandleAsync(evt);

            _mockTwitchApiService.Verify(s => s.SendChatMessageAsync("broadcaster123", "Death Count: 1", "msg-1", null), Times.Once);
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
