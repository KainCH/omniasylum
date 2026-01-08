using System;
using System.Collections.Generic;

namespace OmniForge.Core.Interfaces
{
    public record MonitoringState(bool UseBot, string? BotUserId, DateTimeOffset UpdatedAtUtc);

    /// <summary>
    /// Tracks which broadcasters are currently being monitored and whether the Forge bot is doing the work.
    /// This is an in-memory, per-instance registry (non-persistent).
    /// </summary>
    public interface IMonitoringRegistry
    {
        void SetState(string broadcasterUserId, MonitoringState state);
        bool TryGetState(string broadcasterUserId, out MonitoringState state);
        void Remove(string broadcasterUserId);

        IReadOnlyDictionary<string, MonitoringState> GetAllStates();
        IEnumerable<string> GetBroadcastersUsingBot();
    }
}
