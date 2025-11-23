using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Services
{
    public class WebSocketOverlayNotifier : IOverlayNotifier
    {
        private readonly IWebSocketOverlayManager _webSocketManager;

        public WebSocketOverlayNotifier(IWebSocketOverlayManager webSocketManager)
        {
            _webSocketManager = webSocketManager;
        }

        public async Task NotifyCounterUpdateAsync(string userId, Counter counter)
        {
            await _webSocketManager.SendToUserAsync(userId, "counterUpdate", counter);
        }

        public async Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone)
        {
            await _webSocketManager.SendToUserAsync(userId, "milestoneReached", new { counterType, milestone, newValue, previousMilestone });
        }

        public async Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings)
        {
            await _webSocketManager.SendToUserAsync(userId, "settingsUpdate", settings);
        }

        public async Task NotifyStreamStatusUpdateAsync(string userId, string status)
        {
            await _webSocketManager.SendToUserAsync(userId, "streamStatusUpdate", new { streamStatus = status });
        }

        public async Task NotifyStreamStartedAsync(string userId, Counter counter)
        {
            await _webSocketManager.SendToUserAsync(userId, "streamStarted", counter);
        }

        public async Task NotifyStreamEndedAsync(string userId, Counter counter)
        {
            await _webSocketManager.SendToUserAsync(userId, "streamEnded", counter);
        }

        public async Task NotifyFollowerAsync(string userId, string displayName)
        {
            await _webSocketManager.SendToUserAsync(userId, "newFollower", new { displayName });
        }

        public async Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
        {
            await _webSocketManager.SendToUserAsync(userId, "newSubscriber", new { displayName, tier, isGift });
        }

        public async Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
        {
            await _webSocketManager.SendToUserAsync(userId, "resub", new { displayName, months, tier, message });
        }

        public async Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
        {
            await _webSocketManager.SendToUserAsync(userId, "giftSub", new { gifterName, recipientName, tier, totalGifts });
        }

        public async Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
        {
            await _webSocketManager.SendToUserAsync(userId, "bitsReceived", new { displayName, amount, message, totalBits });
        }

        public async Task NotifyRaidAsync(string userId, string raiderName, int viewers)
        {
            await _webSocketManager.SendToUserAsync(userId, "raidReceived", new { raiderName, viewers });
        }

        public async Task NotifyCustomAlertAsync(string userId, string alertType, object data)
        {
            await _webSocketManager.SendToUserAsync(userId, "customAlert", new { alertType, data });
        }
    }
}
