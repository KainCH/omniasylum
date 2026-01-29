using System;
using System.Threading.Tasks;
using OmniForge.Infrastructure.Services;

namespace OmniForge.Infrastructure.Interfaces
{
    public interface IChatCommandProcessor
    {
        Task ProcessAsync(ChatCommandContext context, Func<string, string, Task>? sendMessage = null);
    }
}
