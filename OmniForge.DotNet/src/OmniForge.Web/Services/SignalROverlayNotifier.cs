using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Hubs;

namespace OmniForge.Web.Services
{
    public class SignalROverlayNotifier : IOverlayNotifier
    {
        private readonly IHubContext<OverlayHub> _hubContext;

        public SignalROverlayNotifier(IHubContext<OverlayHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task NotifyCounterUpdateAsync(string userId, Counter counter)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("counterUpdate", counter);
        }

        public async Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("milestoneReached", new
            {
                userId,
                counterType,
                milestone,
                newValue,
                previousMilestone,
                timestamp = System.DateTime.UtcNow
            });
        }

        public async Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("overlaySettingsUpdate", new
            {
                userId,
                overlaySettings = settings
            });
        }

        public async Task NotifyStreamStatusUpdateAsync(string userId, string status)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("streamStatusUpdate", new
            {
                userId,
                streamStatus = status
            });
        }

        public async Task NotifyStreamStartedAsync(string userId, Counter counter)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("streamStarted", counter);
        }

        public async Task NotifyStreamEndedAsync(string userId, Counter counter)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("streamEnded", counter);
        }

        public async Task NotifyFollowerAsync(string userId, string displayName)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("newFollower", new { displayName });
        }

        public async Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("newSubscriber", new { displayName, tier, isGift });
        }

        public async Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("resub", new { displayName, months, tier, message });
        }

        public async Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("giftSub", new { gifterName, recipientName, tier, totalGifts });
        }

        public async Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("bitsReceived", new { displayName, amount, message, totalBits });
        }

        public async Task NotifyRaidAsync(string userId, string raiderName, int viewers)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("raidReceived", new { raiderName, viewers });
        }

        public async Task NotifyCustomAlertAsync(string userId, string alertType, object data)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("customAlert", new { alertType, data });
        }

        public async Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("templateChanged", new { templateStyle, template });
        }

        public async Task NotifyPayPalDonationAsync(string userId, string donorName, decimal amount, string currency, string message, bool matchedTwitchUser)
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("paypalDonation", new
            {
                donorName,
                amount,
                currency,
                message,
                matchedTwitchUser,
                timestamp = System.DateTime.UtcNow
            });
        }
    }
}
