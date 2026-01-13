using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Schedules periodic Discord invite advertisements in Twitch chat.
    /// Fires immediately on start and then at a random interval between 15 and 30 minutes.
    /// </summary>
    public class DiscordInviteBroadcastScheduler : IDiscordInviteBroadcastScheduler
    {
        private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(30);

        private readonly IDiscordInviteSender _inviteSender;
        private readonly ILogger<DiscordInviteBroadcastScheduler> _logger;

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _loops = new(StringComparer.Ordinal);

        public DiscordInviteBroadcastScheduler(
            IDiscordInviteSender inviteSender,
            ILogger<DiscordInviteBroadcastScheduler> logger)
        {
            _inviteSender = inviteSender;
            _logger = logger;
        }

        public Task StartAsync(string broadcasterId)
        {
            if (string.IsNullOrWhiteSpace(broadcasterId))
            {
                return Task.CompletedTask;
            }

            // Idempotent start.
            if (_loops.ContainsKey(broadcasterId))
            {
                return Task.CompletedTask;
            }

            var cts = new CancellationTokenSource();
            if (!_loops.TryAdd(broadcasterId, cts))
            {
                cts.Dispose();
                return Task.CompletedTask;
            }

            _ = RunLoopAsync(broadcasterId, cts.Token);
            return Task.CompletedTask;
        }

        public Task StopAsync(string broadcasterId)
        {
            if (string.IsNullOrWhiteSpace(broadcasterId))
            {
                return Task.CompletedTask;
            }

            if (_loops.TryRemove(broadcasterId, out var cts))
            {
                using (cts)
                {
                    cts.Cancel();
                }
            }

            return Task.CompletedTask;
        }

        private async Task RunLoopAsync(string broadcasterId, CancellationToken cancellationToken)
        {
            try
            {
                // Fire immediately on stream start.
                await _inviteSender.SendDiscordInviteAsync(broadcasterId);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var delay = GetRandomDelay();

                    _logger.LogInformation(
                        "⏱️ Next Discord invite broadcast scheduled for broadcaster_id={BroadcasterId} in {DelayMinutes} minutes",
                        LogSanitizer.Sanitize(broadcasterId),
                        delay.TotalMinutes);

                    await Task.Delay(delay, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await _inviteSender.SendDiscordInviteAsync(broadcasterId);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Discord invite broadcast loop crashed for broadcaster_id={BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
            }
            finally
            {
                // Ensure cleanup if the loop ends unexpectedly.
                await StopAsync(broadcasterId);
            }
        }

        private static TimeSpan GetRandomDelay()
        {
            var minSeconds = (int)MinInterval.TotalSeconds;
            var maxSeconds = (int)MaxInterval.TotalSeconds;
            var seconds = Random.Shared.Next(minSeconds, maxSeconds + 1);
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
