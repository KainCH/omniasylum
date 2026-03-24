using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Services
{
    /// <summary>
    /// Forwards every IOverlayNotifier call to both v1 (WebSocket) and v2 (SSE) notifiers,
    /// allowing both overlay versions to coexist during the v2 rollout.
    /// Each notifier is isolated so a failure in one does not block the others.
    /// </summary>
    public class CompositeOverlayNotifier : IOverlayNotifier
    {
        private readonly IOverlayNotifier[] _notifiers;
        private readonly ILogger<CompositeOverlayNotifier> _logger;

        public CompositeOverlayNotifier(ILogger<CompositeOverlayNotifier> logger, params IOverlayNotifier[] notifiers)
        {
            _logger = logger;
            _notifiers = notifiers;
        }

        private async Task ForEachNotifierAsync(Func<IOverlayNotifier, Task> action)
        {
            foreach (var n in _notifiers)
            {
                try
                {
                    await action(n);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Overlay notifier {Notifier} failed; continuing with remaining notifiers", n.GetType().Name);
                }
            }
        }

        public Task NotifyCounterUpdateAsync(string userId, Counter counter)
            => ForEachNotifierAsync(n => n.NotifyCounterUpdateAsync(userId, counter));

        public Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone)
            => ForEachNotifierAsync(n => n.NotifyMilestoneReachedAsync(userId, counterType, milestone, newValue, previousMilestone));

        public Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings)
            => ForEachNotifierAsync(n => n.NotifySettingsUpdateAsync(userId, settings));

        public Task NotifyStreamStatusUpdateAsync(string userId, string status)
            => ForEachNotifierAsync(n => n.NotifyStreamStatusUpdateAsync(userId, status));

        public Task NotifyStreamStartedAsync(string userId, Counter counter)
            => ForEachNotifierAsync(n => n.NotifyStreamStartedAsync(userId, counter));

        public Task NotifyStreamEndedAsync(string userId, Counter counter)
            => ForEachNotifierAsync(n => n.NotifyStreamEndedAsync(userId, counter));

        public Task NotifyFollowerAsync(string userId, string displayName)
            => ForEachNotifierAsync(n => n.NotifyFollowerAsync(userId, displayName));

        public Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
            => ForEachNotifierAsync(n => n.NotifySubscriberAsync(userId, displayName, tier, isGift));

        public Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
            => ForEachNotifierAsync(n => n.NotifyResubAsync(userId, displayName, months, tier, message));

        public Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
            => ForEachNotifierAsync(n => n.NotifyGiftSubAsync(userId, gifterName, recipientName, tier, totalGifts));

        public Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
            => ForEachNotifierAsync(n => n.NotifyBitsAsync(userId, displayName, amount, message, totalBits));

        public Task NotifyRaidAsync(string userId, string raiderName, int viewers)
            => ForEachNotifierAsync(n => n.NotifyRaidAsync(userId, raiderName, viewers));

        public Task NotifyCustomAlertAsync(string userId, string alertType, object data)
            => ForEachNotifierAsync(n => n.NotifyCustomAlertAsync(userId, alertType, data));

        public Task NotifyOvertimeAsync(string userId, OvertimeConfig config, string sceneName)
            => ForEachNotifierAsync(n => n.NotifyOvertimeAsync(userId, config, sceneName));

        public Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template)
            => ForEachNotifierAsync(n => n.NotifyTemplateChangedAsync(userId, templateStyle, template));

        public Task NotifySceneChangedAsync(string userId, string sceneName)
            => ForEachNotifierAsync(n => n.NotifySceneChangedAsync(userId, sceneName));
    }
}
