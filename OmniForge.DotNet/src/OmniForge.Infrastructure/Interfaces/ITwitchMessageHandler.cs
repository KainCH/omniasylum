using System;
using System.Threading.Tasks;
using TwitchLib.Client.Models;

namespace OmniForge.Infrastructure.Interfaces
{
    public interface ITwitchMessageHandler
    {
        Task HandleMessageAsync(string userId, ChatMessage chatMessage, Func<string, string, Task> sendMessage);
    }
}
