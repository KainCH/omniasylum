using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OmniForge.Core.Entities;

namespace OmniForge.Web.Hubs
{
    public class OverlayHub : Hub
    {
        public async Task JoinGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        }

        public async Task LeaveGroup(string userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");
        }
    }
}
