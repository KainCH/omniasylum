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
    }
}
