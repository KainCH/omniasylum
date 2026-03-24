namespace OmniForge.Core.Interfaces
{
    public interface IScheduledMessageService
    {
        void StartForUser(string broadcasterId);
        void StopForUser(string broadcasterId);
    }
}
