namespace OmniForge.Infrastructure.Configuration
{
    public class RedisSettings
    {
        public string HostName { get; set; } = string.Empty;

        // Namespaces cache keys when multiple environments share the same Redis instance.
        // Example: "dev" or "prod".
        public string KeyNamespace { get; set; } = string.Empty;
    }
}
