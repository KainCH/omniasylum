using System;
using System.Collections.Generic;
using System.Linq;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Registry that maps subscription types to their handlers.
    /// </summary>
    public class EventSubHandlerRegistry : IEventSubHandlerRegistry
    {
        private readonly Dictionary<string, IEventSubHandler> _handlers;

        public EventSubHandlerRegistry(IEnumerable<IEventSubHandler> handlers)
        {
            _handlers = handlers.ToDictionary(h => h.SubscriptionType, StringComparer.OrdinalIgnoreCase);
        }

        public IEventSubHandler? GetHandler(string subscriptionType)
        {
            return _handlers.TryGetValue(subscriptionType, out var handler) ? handler : null;
        }

        public IEnumerable<IEventSubHandler> GetAllHandlers()
        {
            return _handlers.Values;
        }
    }
}
