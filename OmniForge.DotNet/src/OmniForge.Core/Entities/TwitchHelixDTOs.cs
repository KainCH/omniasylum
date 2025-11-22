using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class HelixCustomReward
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Cost { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string BackgroundColor { get; set; } = string.Empty;
        public bool IsUserInputRequired { get; set; }
        public HelixMaxPerStreamSetting MaxPerStreamSetting { get; set; } = new();
        public HelixMaxPerUserPerStreamSetting MaxPerUserPerStreamSetting { get; set; } = new();
        public HelixGlobalCooldownSetting GlobalCooldownSetting { get; set; } = new();
    }

    public class HelixMaxPerStreamSetting
    {
        public bool IsEnabled { get; set; }
        public int MaxPerStream { get; set; }
    }

    public class HelixMaxPerUserPerStreamSetting
    {
        public bool IsEnabled { get; set; }
        public int MaxPerUserPerStream { get; set; }
    }

    public class HelixGlobalCooldownSetting
    {
        public bool IsEnabled { get; set; }
        public int GlobalCooldownSeconds { get; set; }
    }
}
