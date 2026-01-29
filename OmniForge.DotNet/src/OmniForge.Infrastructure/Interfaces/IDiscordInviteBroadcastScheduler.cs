using System;
using System.Threading.Tasks;

namespace OmniForge.Infrastructure.Interfaces
{
    public interface IDiscordInviteBroadcastScheduler
    {
        Task StartAsync(string broadcasterId);
        Task StopAsync(string broadcasterId);
    }
}
