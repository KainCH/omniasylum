using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class SyncAgentTrackerService : ISyncAgentTracker
    {
        private readonly ConcurrentDictionary<string, AgentState> _agents = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SyncAgentTrackerService> _logger;

        public event Action<string>? OnAgentStateChanged;

        public SyncAgentTrackerService(IServiceScopeFactory scopeFactory, ILogger<SyncAgentTrackerService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task RegisterAgentAsync(string userId, string connectionId, string softwareType)
        {
            var state = new AgentState
            {
                UserId = userId,
                ConnectionId = connectionId,
                SoftwareType = softwareType,
                ConnectedAt = DateTimeOffset.UtcNow,
                LastHeartbeat = DateTimeOffset.UtcNow
            };

            _agents.AddOrUpdate(userId, state, (_, _) => state);
            _logger.LogInformation("Sync agent registered: userId={UserId}, software={Software}, connectionId={ConnectionId}",
                userId, softwareType, connectionId);

            OnAgentStateChanged?.Invoke(userId);
            return Task.CompletedTask;
        }

        public Task UnregisterAgentAsync(string userId, string connectionId)
        {
            if (_agents.TryGetValue(userId, out var state) &&
                string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                _agents.TryRemove(userId, out _);
                _logger.LogInformation("Sync agent unregistered: userId={UserId}, connectionId={ConnectionId}",
                    userId, connectionId);
                OnAgentStateChanged?.Invoke(userId);
            }

            return Task.CompletedTask;
        }

        public Task UpdateCurrentSceneAsync(string userId, string sceneName)
        {
            if (_agents.TryGetValue(userId, out var state))
            {
                state.CurrentScene = sceneName;
                state.LastHeartbeat = DateTimeOffset.UtcNow;
                OnAgentStateChanged?.Invoke(userId);
            }

            return Task.CompletedTask;
        }

        public AgentState? GetAgentState(string userId)
        {
            _agents.TryGetValue(userId, out var state);
            return state;
        }

        public bool IsAgentConnected(string userId)
        {
            return _agents.ContainsKey(userId);
        }
    }
}
