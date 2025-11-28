using System.Collections.Generic;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Interface for looking up event handlers by subscription type.
    /// </summary>
    public interface IEventSubHandlerRegistry
    {
        /// <summary>
        /// Gets a handler for the specified subscription type.
        /// </summary>
        /// <param name="subscriptionType">The EventSub subscription type (e.g., "stream.online").</param>
        /// <returns>The handler if found, otherwise null.</returns>
        IEventSubHandler? GetHandler(string subscriptionType);

        /// <summary>
        /// Gets all registered handlers.
        /// </summary>
        IEnumerable<IEventSubHandler> GetAllHandlers();
    }
}
