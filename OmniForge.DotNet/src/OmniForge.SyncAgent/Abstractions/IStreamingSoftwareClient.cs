namespace OmniForge.SyncAgent.Abstractions
{
    public interface IStreamingSoftwareClient
    {
        Task ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync();
        Task<string[]> GetScenesAsync();
        Task<string?> GetActiveSceneAsync();
        bool IsConnected { get; }
        string SoftwareType { get; }
        event Action<string>? SceneChanged;
        event Action<string[]>? SceneListUpdated;
        event Action? Connected;
        event Action<string>? Disconnected;
    }
}
