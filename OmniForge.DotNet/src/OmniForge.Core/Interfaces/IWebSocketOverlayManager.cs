using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OmniForge.Core.Interfaces
{
    public interface IWebSocketOverlayManager
    {
        Task HandleConnectionAsync(string userId, WebSocket webSocket);
        Task SendToUserAsync(string userId, string method, object data);
    }
}
