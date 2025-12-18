namespace OmniForge.Web;

/// <summary>
/// Provides server instance identification for detecting server restarts.
/// Overlays use this to detect server restarts and refresh silently.
/// </summary>
public static class ServerInstance
{
    /// <summary>
    /// A unique 8-character identifier that changes on each server restart.
    /// </summary>
    public static readonly string Id = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// The UTC timestamp when the server started.
    /// </summary>
    public static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;
}
