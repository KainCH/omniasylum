using System;
using System.Threading.Tasks;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    public interface IDiscordInviteBroadcastScheduler
    {
        Task StartAsync(string broadcasterId);
        Task StopAsync(string broadcasterId);
    }
}
