using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class OvertimeTrackerService
    {
        private readonly ConcurrentDictionary<string, OvertimeState> _pending = new();
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<OvertimeTrackerService> _logger;

        public OvertimeTrackerService(IOverlayNotifier overlayNotifier, ILogger<OvertimeTrackerService> logger)
        {
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public void Schedule(string userId, string sceneName, OvertimeConfig config, int delayMinutes)
        {
            Cancel(userId);

            var cts = new CancellationTokenSource();
            var state = new OvertimeState
            {
                UserId = userId,
                SceneName = sceneName,
                Config = config,
                Cts = cts
            };

            _pending[userId] = state;

            _ = RunOvertimeAsync(state, delayMinutes, cts.Token);
        }

        public void Cancel(string userId)
        {
            if (_pending.TryRemove(userId, out var existing))
            {
                existing.Cts.Cancel();
                existing.Cts.Dispose();
                _logger.LogDebug("Overtime cancelled for userId={UserId}", userId);
            }
        }

        public bool HasPendingOvertime(string userId)
        {
            return _pending.ContainsKey(userId);
        }

        private async Task RunOvertimeAsync(OvertimeState state, int delayMinutes, CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), ct);

                // Timer expired, still on same scene — fire overtime
                if (!ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Overtime triggered for userId={UserId}, scene={Scene}", state.UserId, state.SceneName);
                    await _overlayNotifier.NotifyOvertimeAsync(state.UserId, state.Config, state.SceneName);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when scene changes before overtime
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in overtime tracker for userId={UserId}", state.UserId);
            }
            finally
            {
                _pending.TryRemove(state.UserId, out _);
            }
        }

        private class OvertimeState
        {
            public string UserId { get; set; } = string.Empty;
            public string SceneName { get; set; } = string.Empty;
            public OvertimeConfig Config { get; set; } = new();
            public CancellationTokenSource Cts { get; set; } = new();
        }
    }
}
