namespace OmniForge.Infrastructure.Interfaces
{
    /// <summary>
    /// Interface for processing EventSub WebSocket messages.
    /// </summary>
    public interface IEventSubMessageProcessor
    {
        OmniForge.Infrastructure.Services.EventSubProcessResult Process(string json);
    }
}
