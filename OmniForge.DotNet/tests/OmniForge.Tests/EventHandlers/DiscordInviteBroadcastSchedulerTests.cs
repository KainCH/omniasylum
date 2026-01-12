using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Infrastructure.Services.EventHandlers;
using Xunit;

namespace OmniForge.Tests.EventHandlers
{
    public class DiscordInviteBroadcastSchedulerTests
    {
        [Fact]
        public async Task StartAsync_WhenBroadcasterIdBlank_DoesNotStartLoop()
        {
            var sender = new Mock<IDiscordInviteSender>(MockBehavior.Strict);
            var logger = new Mock<ILogger<DiscordInviteBroadcastScheduler>>();

            var scheduler = new DiscordInviteBroadcastScheduler(sender.Object, logger.Object);

            await scheduler.StartAsync(" ");
            await scheduler.StartAsync(string.Empty);

            sender.Verify(s => s.SendDiscordInviteAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StopAsync_WhenBroadcasterIdBlank_DoesNothing()
        {
            var sender = new Mock<IDiscordInviteSender>(MockBehavior.Strict);
            var logger = new Mock<ILogger<DiscordInviteBroadcastScheduler>>();

            var scheduler = new DiscordInviteBroadcastScheduler(sender.Object, logger.Object);

            await scheduler.StopAsync(" ");
            await scheduler.StopAsync(string.Empty);

            sender.Verify(s => s.SendDiscordInviteAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StartAsync_ShouldSendInviteImmediately_AndBeIdempotent()
        {
            var sent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sendCount = 0;

            var sender = new Mock<IDiscordInviteSender>(MockBehavior.Strict);
            sender.Setup(s => s.SendDiscordInviteAsync(It.IsAny<string>()))
                .Returns<string>(broadcasterId =>
                {
                    Interlocked.Increment(ref sendCount);
                    sent.TrySetResult(broadcasterId);
                    return Task.CompletedTask;
                });

            var logger = new Mock<ILogger<DiscordInviteBroadcastScheduler>>();
            var scheduler = new DiscordInviteBroadcastScheduler(sender.Object, logger.Object);

            await scheduler.StartAsync("b1");
            await scheduler.StartAsync("b1");

            var broadcaster = await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("b1", broadcaster);

            // Delay is at least 15 minutes, so no second send should occur here.
            await Task.Delay(50);
            Assert.Equal(1, sendCount);

            await scheduler.StopAsync("b1");
        }

        [Fact]
        public async Task StartAsync_WhenSenderThrows_ShouldCleanupAndAllowRestart()
        {
            var firstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var callCount = 0;

            var sender = new Mock<IDiscordInviteSender>(MockBehavior.Strict);
            sender.Setup(s => s.SendDiscordInviteAsync(It.IsAny<string>()))
                .Returns<string>(_ =>
                {
                    var current = Interlocked.Increment(ref callCount);
                    if (current == 1)
                    {
                        firstCall.TrySetResult();
                        throw new InvalidOperationException("boom");
                    }

                    secondCall.TrySetResult();
                    return Task.CompletedTask;
                });

            var logger = new Mock<ILogger<DiscordInviteBroadcastScheduler>>();
            var scheduler = new DiscordInviteBroadcastScheduler(sender.Object, logger.Object);

            await scheduler.StartAsync("b1");
            await firstCall.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Ensure the background loop has a chance to clean itself up.
            await scheduler.StopAsync("b1");

            // Restart should eventually re-enter and invoke the sender again.
            for (var i = 0; i < 50 && !secondCall.Task.IsCompleted; i++)
            {
                await scheduler.StartAsync("b1");
                await Task.Delay(10);
            }

            await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(callCount >= 2);

            await scheduler.StopAsync("b1");
        }
    }
}
