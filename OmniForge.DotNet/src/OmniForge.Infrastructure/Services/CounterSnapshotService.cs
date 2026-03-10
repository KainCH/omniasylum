using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    /// <summary>
    /// Periodically snapshots each user's live counter values into GameCountersRepository so
    /// that per-game counter history stays current even without explicit saves or game switches.
    /// Only runs for users whose stream is currently live; game-switch snapshots are handled
    /// directly by <see cref="GameSwitchService"/>.
    /// </summary>
    public sealed class CounterSnapshotService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IStreamMonitorService _streamMonitorService;
        private readonly ILogger<CounterSnapshotService> _logger;

        public CounterSnapshotService(
            IServiceScopeFactory scopeFactory,
            IStreamMonitorService streamMonitorService,
            ILogger<CounterSnapshotService> logger)
        {
            _scopeFactory = scopeFactory;
            _streamMonitorService = streamMonitorService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✅ CounterSnapshotService started (interval: {Interval})", Interval);

            // Stagger the first run by the full interval so it doesn't hit storage at startup.
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await TakeSnapshotsAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }

        private async Task TakeSnapshotsAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var gameContextRepo = scope.ServiceProvider.GetRequiredService<IGameContextRepository>();
                var counterRepo = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                var gameCountersRepo = scope.ServiceProvider.GetRequiredService<IGameCountersRepository>();

                var users = await userRepo.GetAllUsersAsync().ConfigureAwait(false);
                var snapshotted = 0;

                foreach (var user in users)
                {
                    if (ct.IsCancellationRequested) break;

                    // Skip inactive accounts to avoid unnecessary storage reads/writes
                    if (!user.IsActive) continue;

                    var userId = user.TwitchUserId;
                    if (string.IsNullOrWhiteSpace(userId)) continue;

                    // Only snapshot while the stream is live; game-switch snapshots are
                    // handled directly by GameSwitchService regardless of live status.
                    if (!_streamMonitorService.IsUserLive(userId)) continue;

                    try
                    {
                        var ctx = await gameContextRepo.GetAsync(userId).ConfigureAwait(false);
                        var gameId = ctx?.ActiveGameId;
                        if (string.IsNullOrWhiteSpace(gameId)) continue;

                        var live = await counterRepo.GetCountersAsync(userId).ConfigureAwait(false);
                        if (live == null) continue;

                        await gameCountersRepo.SaveAsync(userId, gameId, live).ConfigureAwait(false);
                        snapshotted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "❌ CounterSnapshotService: failed to snapshot counters for user {UserId}",
                            userId);
                    }
                }

                if (snapshotted > 0)
                {
                    _logger.LogInformation(
                        "🔄 CounterSnapshotService: snapshotted live counters for {Count} user(s)",
                        snapshotted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "❌ CounterSnapshotService: unexpected error during snapshot run");
            }
        }
    }
}
