using System.Text.Json;
using System.Threading.Tasks;

namespace OmniForge.Infrastructure.Interfaces
{
    /// <summary>
    /// Interface for handling specific EventSub event types.
    /// Each implementation handles one type of Twitch EventSub notification.
    /// </summary>
    public interface IEventSubHandler
    {
        /// <summary>
        /// The EventSub subscription type this handler processes (e.g., "stream.online", "channel.follow").
        /// </summary>
        string SubscriptionType { get; }

        /// <summary>
        /// Handles the EventSub event data.
        /// </summary>
        /// <param name="eventData">The JSON event payload from Twitch.</param>
        /// <returns>A task representing the async operation.</returns>
        Task HandleAsync(JsonElement eventData);
    }
}
