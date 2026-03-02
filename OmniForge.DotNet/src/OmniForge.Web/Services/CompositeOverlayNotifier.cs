using System.Threading.Tasks;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Services
{
    /// <summary>
    /// Forwards every IOverlayNotifier call to both v1 (WebSocket) and v2 (SSE) notifiers,
    /// allowing both overlay versions to coexist during the v2 rollout.
    /// </summary>
    public class CompositeOverlayNotifier : IOverlayNotifier
    {
        private readonly IOverlayNotifier[] _notifiers;

        public CompositeOverlayNotifier(params IOverlayNotifier[] notifiers)
        {
            _notifiers = notifiers;
        }

        public async Task NotifyCounterUpdateAsync(string userId, Counter counter)
        {
            foreach (var n in _notifiers)
                await n.NotifyCounterUpdateAsync(userId, counter);
        }

        public async Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone)
        {
            foreach (var n in _notifiers)
                await n.NotifyMilestoneReachedAsync(userId, counterType, milestone, newValue, previousMilestone);
        }

        public async Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings)
        {
            foreach (var n in _notifiers)
                await n.NotifySettingsUpdateAsync(userId, settings);
        }

        public async Task NotifyStreamStatusUpdateAsync(string userId, string status)
        {
            foreach (var n in _notifiers)
                await n.NotifyStreamStatusUpdateAsync(userId, status);
        }

        public async Task NotifyStreamStartedAsync(string userId, Counter counter)
        {
            foreach (var n in _notifiers)
                await n.NotifyStreamStartedAsync(userId, counter);
        }

        public async Task NotifyStreamEndedAsync(string userId, Counter counter)
        {
            foreach (var n in _notifiers)
                await n.NotifyStreamEndedAsync(userId, counter);
        }

        public async Task NotifyFollowerAsync(string userId, string displayName)
        {
            foreach (var n in _notifiers)
                await n.NotifyFollowerAsync(userId, displayName);
        }

        public async Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
        {
            foreach (var n in _notifiers)
                await n.NotifySubscriberAsync(userId, displayName, tier, isGift);
        }

        public async Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
        {
            foreach (var n in _notifiers)
                await n.NotifyResubAsync(userId, displayName, months, tier, message);
        }

        public async Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
        {
            foreach (var n in _notifiers)
                await n.NotifyGiftSubAsync(userId, gifterName, recipientName, tier, totalGifts);
        }

        public async Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
        {
            foreach (var n in _notifiers)
                await n.NotifyBitsAsync(userId, displayName, amount, message, totalBits);
        }

        public async Task NotifyRaidAsync(string userId, string raiderName, int viewers)
        {
            foreach (var n in _notifiers)
                await n.NotifyRaidAsync(userId, raiderName, viewers);
        }

        public async Task NotifyCustomAlertAsync(string userId, string alertType, object data)
        {
            foreach (var n in _notifiers)
                await n.NotifyCustomAlertAsync(userId, alertType, data);
        }

        public async Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template)
        {
            foreach (var n in _notifiers)
                await n.NotifyTemplateChangedAsync(userId, templateStyle, template);
        }
    }
}
