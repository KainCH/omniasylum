using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class ChatCommandConfiguration
    {
        // Max allowed numeric increment in chat for action-based commands.
        // Default 1 preserves legacy behavior.
        public int MaxIncrementAmount { get; set; } = 1;

        public Dictionary<string, ChatCommandDefinition> Commands { get; set; } = new Dictionary<string, ChatCommandDefinition>();
    }

    public class ChatCommandDefinition
    {
        public string Response { get; set; } = string.Empty;
        public string Permission { get; set; } = "everyone"; // everyone, subscriber, moderator, broadcaster
        public int Cooldown { get; set; } = 0;
        public bool Enabled { get; set; } = true;
        public bool Custom { get; set; } = false;
        public string? Action { get; set; }
        public string? Counter { get; set; }
        public System.DateTimeOffset? CreatedAt { get; set; }
        public System.DateTimeOffset? UpdatedAt { get; set; }
    }
}
