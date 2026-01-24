using System;
using System.Threading.Tasks;
using OmniForge.Infrastructure.Models.EventSub;

namespace OmniForge.Infrastructure.Services
{
    public interface INativeEventSubService
    {
        string? SessionId { get; }
        bool IsConnected { get; }
        DateTime LastKeepaliveTime { get; }
        int? KeepaliveTimeoutSeconds { get; }

        event Func<EventSubMessage, Task>? OnNotification;
        event Func<string, Task>? OnSessionWelcome;
        event Func<Task>? OnDisconnected;

        Task ConnectAsync();
        Task DisconnectAsync();
    }
}
