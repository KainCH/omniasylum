using System;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public record DashboardChatMessage(
        string UserId,
        string Username,
        string DisplayName,
        string Message,
        bool IsMod,
        bool IsBroadcaster,
        bool IsSubscriber,
        string? ColorHex,
        DateTimeOffset Timestamp);

    public record DashboardEvent(
        string EventType,
        string Description,
        DateTimeOffset Timestamp,
        string? Extra = null);

    public interface IDashboardFeedService
    {
        void PushChatMessage(string userId, DashboardChatMessage msg);
        void PushEvent(string userId, DashboardEvent evt);
        void SetLiveStatus(string userId, bool isLive);
        bool GetLiveStatus(string userId);
        IDisposable Subscribe(string userId, Action<DashboardChatMessage>? onChat, Action<DashboardEvent>? onEvent, Action<bool>? onLiveChange = null);
    }
}
