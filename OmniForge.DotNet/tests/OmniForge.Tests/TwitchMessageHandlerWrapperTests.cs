using System.Threading.Tasks;
using Moq;
using OmniForge.Infrastructure.Services;
using TwitchLib.Client.Models;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchMessageHandlerWrapperTests
    {
        [Fact]
        public async Task HandleMessageAsync_ShouldMapChatMessage_ToContext()
        {
            // Arrange
            var mockProcessor = new Mock<IChatCommandProcessor>();
            var handler = new TwitchMessageHandler(mockProcessor.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<TwitchMessageHandler>>());

            var chatMessage = new ChatMessage(
                botUsername: "bot",
                userId: "user123",
                userName: "user",
                displayName: "User",
                colorHex: "",
                color: System.Drawing.Color.Black,
                emoteSet: null,
                message: "!deaths",
                userType: TwitchLib.Client.Enums.UserType.Viewer,
                channel: "channel",
                id: "id",
                isSubscriber: true,
                subscribedMonthCount: 0,
                roomId: "room",
                isTurbo: false,
                isModerator: true,
                isMe: false,
                isBroadcaster: false,
                isVip: false,
                isPartner: false,
                isStaff: false,
                noisy: TwitchLib.Client.Enums.Noisy.False,
                rawIrcMessage: "",
                emoteReplacedMessage: "",
                badges: null,
                cheerBadge: null,
                bits: 0,
                bitsInDollars: 0);

            ChatCommandContext? captured = null;
            mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<ChatCommandContext>(), It.IsAny<System.Func<string, string, Task>>()))
                .Callback<ChatCommandContext, System.Func<string, string, Task>>((ctx, _) => captured = ctx)
                .Returns(Task.CompletedTask);

            // Act
            await handler.HandleMessageAsync("broadcaster123", chatMessage, (uid, msg) => Task.CompletedTask);

            // Assert
            Assert.NotNull(captured);
            Assert.Equal("broadcaster123", captured!.UserId);
            Assert.Equal("!deaths", captured.Message);
            Assert.True(captured.IsModerator);
            Assert.True(captured.IsSubscriber);
            Assert.False(captured.IsBroadcaster);
        }
    }
}
