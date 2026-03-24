using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class SceneActionService : ISceneActionService
    {
        private readonly ISceneActionRepository _sceneActionRepo;
        private readonly IUserRepository _userRepo;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly OvertimeTrackerService _overtimeTracker;
        private readonly ILogger<SceneActionService> _logger;

        public SceneActionService(
            ISceneActionRepository sceneActionRepo,
            IUserRepository userRepo,
            IOverlayNotifier overlayNotifier,
            OvertimeTrackerService overtimeTracker,
            ILogger<SceneActionService> logger)
        {
            _sceneActionRepo = sceneActionRepo;
            _userRepo = userRepo;
            _overlayNotifier = overlayNotifier;
            _overtimeTracker = overtimeTracker;
            _logger = logger;
        }

        public async Task HandleSceneChangedAsync(string userId, string newScene, string? previousScene)
        {
            _logger.LogInformation("Processing scene change for userId={UserId}: {Previous} -> {New}",
                userId, previousScene ?? "(none)", newScene);

            // Cancel any pending overtime from previous scene
            _overtimeTracker.Cancel(userId);

            // Notify overlay of scene change
            await _overlayNotifier.NotifySceneChangedAsync(userId, newScene);

            var user = await _userRepo.GetUserAsync(userId);
            if (user == null) return;

            // Load scene action config for the new scene
            var sceneAction = await _sceneActionRepo.GetAsync(userId, newScene);

            bool willStartTimer = sceneAction is { TimerEnabled: true, AutoStartTimer: true } && sceneAction.TimerDurationMinutes > 0;

            // If the new scene won't start a timer, stop any running timer immediately
            if (!willStartTimer && user.OverlaySettings.TimerManualRunning)
            {
                user.OverlaySettings.TimerManualRunning = false;
                user.OverlaySettings.TimerManualStartUtc = null;
                user.OverlaySettings.TimerHidden = true;
                await _userRepo.SaveUserAsync(user);
                await _overlayNotifier.NotifySettingsUpdateAsync(userId, user.OverlaySettings);
                _logger.LogInformation("Timer stopped on scene change for userId={UserId}, scene={Scene}", userId, newScene);
            }

            if (sceneAction == null)
            {
                _logger.LogDebug("No scene action configured for userId={UserId}, scene={Scene}", userId, newScene);
                return;
            }

            // Build a single combined notification for counter visibility + timer changes so the
            // client never receives a stale intermediate config (e.g. TimerHidden=true followed
            // immediately by TimerManualRunning=true, which could cause a visible flicker or miss).
            bool hasCounterOverride = sceneAction.CounterVisibility.Count > 0;

            if (willStartTimer)
            {
                user.OverlaySettings.TimerDurationMinutes = sceneAction.TimerDurationMinutes;
                user.OverlaySettings.TimerManualRunning = true;
                user.OverlaySettings.TimerManualStartUtc = DateTimeOffset.UtcNow;
                user.OverlaySettings.TimerHidden = false;
                await _userRepo.SaveUserAsync(user);
                _logger.LogInformation("Timer started for userId={UserId}: {Minutes}min on scene={Scene}",
                    userId, sceneAction.TimerDurationMinutes, newScene);
            }

            if (hasCounterOverride || willStartTimer)
            {
                // Clone the (already-updated) settings and apply counter visibility on top.
                var notify = CloneOverlaySettings(user.OverlaySettings);
                if (hasCounterOverride)
                    ApplyCounterVisibility(notify.Counters, sceneAction);
                await _overlayNotifier.NotifySettingsUpdateAsync(userId, notify);
            }

            // Schedule overtime if configured AND timer was actually started
            if (sceneAction.Overtime.Enabled && willStartTimer)
            {
                _overtimeTracker.Schedule(userId, newScene, sceneAction.Overtime, sceneAction.TimerDurationMinutes);
                _logger.LogInformation("Overtime scheduled for userId={UserId}: {Minutes}min on scene={Scene}",
                    userId, sceneAction.TimerDurationMinutes, newScene);
            }
        }

        private static OverlaySettings CloneOverlaySettings(OverlaySettings source)
        {
            return new OverlaySettings
            {
                Enabled = source.Enabled,
                OfflinePreview = source.OfflinePreview,
                Position = source.Position,
                Scale = source.Scale,
                TimerEnabled = source.TimerEnabled,
                TimerDurationMinutes = source.TimerDurationMinutes,
                TimerTextColor = source.TimerTextColor,
                TimerManualRunning = source.TimerManualRunning,
                TimerManualStartUtc = source.TimerManualStartUtc,
                TimerHidden = source.TimerHidden,
                Counters = new OverlayCounters
                {
                    HideAll = source.Counters.HideAll,
                    Deaths = source.Counters.Deaths,
                    Swears = source.Counters.Swears,
                    Screams = source.Counters.Screams,
                    Bits = source.Counters.Bits
                },
                BitsGoal = source.BitsGoal,
                Theme = source.Theme,
                Animations = source.Animations
            };
        }

        private static void ApplyCounterVisibility(OverlayCounters counters, SceneAction action)
        {
            // Sentinel key "*" = "hide" means hide the entire counter block (standard + custom)
            if (action.CounterVisibility.TryGetValue("*", out var sentinel) && sentinel == "hide")
            {
                counters.HideAll = true;
                counters.Deaths = false;
                counters.Swears = false;
                counters.Screams = false;
                counters.Bits = false;
                return;
            }

            foreach (var kvp in action.CounterVisibility)
            {
                var visibility = kvp.Value?.ToLowerInvariant();
                if (visibility == "default") continue;

                var show = visibility == "show";
                switch (kvp.Key.ToLowerInvariant())
                {
                    case "deaths": counters.Deaths = show; break;
                    case "swears": counters.Swears = show; break;
                    case "screams": counters.Screams = show; break;
                    case "bits": counters.Bits = show; break;
                }
            }
        }
    }
}
