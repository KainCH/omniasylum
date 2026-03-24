using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class ScheduledMessageService : IScheduledMessageService
    {
        private readonly ILogger<ScheduledMessageService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITwitchClientManager _twitchClientManager;

        private readonly ConcurrentDictionary<string, Timer> _timers = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTimeOffset>> _lastFired = new();

        public ScheduledMessageService(
            ILogger<ScheduledMessageService> logger,
            IServiceScopeFactory scopeFactory,
            ITwitchClientManager twitchClientManager)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _twitchClientManager = twitchClientManager;
        }

        public void StartForUser(string broadcasterId)
        {
            // Ensure last-fired tracking exists
            _lastFired.GetOrAdd(broadcasterId, _ => new ConcurrentDictionary<string, DateTimeOffset>());

            var timer = new Timer(async _ => await TickAsync(broadcasterId), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            var old = _timers.AddOrUpdate(broadcasterId, timer, (_, existing) =>
            {
                existing.Dispose();
                return timer;
            });
        }

        public void StopForUser(string broadcasterId)
        {
            if (_timers.TryRemove(broadcasterId, out var timer))
            {
                timer.Dispose();
            }
        }

        private async Task TickAsync(string broadcasterId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

                var user = await userRepository.GetUserAsync(broadcasterId);
                if (user?.BotSettings?.ScheduledMessages == null) return;

                var now = DateTimeOffset.UtcNow;
                var fired = _lastFired.GetOrAdd(broadcasterId, _ => new ConcurrentDictionary<string, DateTimeOffset>());

                foreach (var entry in user.BotSettings.ScheduledMessages)
                {
                    if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.Message)) continue;

                    var lastFiredAt = fired.GetValueOrDefault(entry.Id, DateTimeOffset.MinValue);
                    var intervalElapsed = now - lastFiredAt >= TimeSpan.FromMinutes(entry.IntervalMinutes);

                    if (intervalElapsed)
                    {
                        await _twitchClientManager.SendMessageAsync(broadcasterId, entry.Message);
                        fired[entry.Id] = now;
                        _logger.LogInformation("✅ Scheduled message sent for {Broadcaster}: {Id}", broadcasterId, entry.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ScheduledMessageService tick failed for {Broadcaster}", broadcasterId);
            }
        }
    }
}
