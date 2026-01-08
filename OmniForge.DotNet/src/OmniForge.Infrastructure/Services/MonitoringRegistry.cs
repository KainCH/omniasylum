using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class MonitoringRegistry : IMonitoringRegistry
    {
        private readonly ConcurrentDictionary<string, MonitoringState> _states = new(StringComparer.OrdinalIgnoreCase);

        public void SetState(string broadcasterUserId, MonitoringState state)
        {
            if (string.IsNullOrWhiteSpace(broadcasterUserId))
            {
                return;
            }

            _states.AddOrUpdate(broadcasterUserId, state, (_, __) => state);
        }

        public bool TryGetState(string broadcasterUserId, out MonitoringState state)
            => _states.TryGetValue(broadcasterUserId, out state!);

        public void Remove(string broadcasterUserId)
        {
            if (string.IsNullOrWhiteSpace(broadcasterUserId))
            {
                return;
            }

            _states.TryRemove(broadcasterUserId, out _);
        }

        public IReadOnlyDictionary<string, MonitoringState> GetAllStates()
            => _states;

        public IEnumerable<string> GetBroadcastersUsingBot()
            => _states.Where(kvp => kvp.Value.UseBot).Select(kvp => kvp.Key);
    }
}
