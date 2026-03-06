using System.Text.Json;

namespace OmniForge.SyncAgent
{
    public class AgentConfigStore
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "omni-forge");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "agent-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public AgentConfig Config { get; private set; } = new();

        public string ServerUrl => Config.ServerUrl;

        public void Load()
        {
            if (!File.Exists(ConfigPath))
            {
                Config = new AgentConfig { ServerUrl = GetDefaultServerUrl() };
                return;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions) ?? new AgentConfig();
                if (string.IsNullOrEmpty(Config.ServerUrl))
                    Config.ServerUrl = GetDefaultServerUrl();
            }
            catch
            {
                Config = new AgentConfig { ServerUrl = GetDefaultServerUrl() };
            }
        }

        public void Save(AgentConfig config)
        {
            Config = config;
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }

        public void SaveToken(string token, DateTimeOffset expiresAt)
        {
            Config.Token = token;
            Config.TokenExpiresAt = expiresAt;
            Save(Config);
        }

        public void ClearToken()
        {
            Config.Token = null;
            Config.TokenExpiresAt = null;
            Save(Config);
        }

        public bool HasToken() => !string.IsNullOrEmpty(Config.Token);

        public void SaveObsPassword(string? password)
        {
            Config.ObsPassword = password;
            Save(Config);
        }

        private static string GetDefaultServerUrl()
        {
#if PROD_BUILD
            return "https://stream-tool.cerillia.com";
#else
            return "https://localhost:5001";
#endif
        }
    }

    public class AgentConfig
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string? Token { get; set; }
        public DateTimeOffset? TokenExpiresAt { get; set; }
        public bool StartWithWindows { get; set; } = true;
        public string? ObsPassword { get; set; }
    }
}
