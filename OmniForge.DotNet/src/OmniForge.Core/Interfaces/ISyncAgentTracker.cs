using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface ISyncAgentTracker
    {
        /// <summary>Fires whenever agent state changes (connect, disconnect, scene switch). Argument is the userId.</summary>
        event Action<string> OnAgentStateChanged;

        Task RegisterAgentAsync(string userId, string connectionId, string softwareType);
        Task UnregisterAgentAsync(string userId, string connectionId);
        Task UpdateCurrentSceneAsync(string userId, string sceneName);
        AgentState? GetAgentState(string userId);
        bool IsAgentConnected(string userId);
    }

    public class AgentState
    {
        public string UserId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string SoftwareType { get; set; } = string.Empty;
        public string? CurrentScene { get; set; }
        public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastHeartbeat { get; set; } = DateTimeOffset.UtcNow;
    }
}
