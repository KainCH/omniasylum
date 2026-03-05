using System;
using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class BroadcastProfile
    {
        public string UserId { get; set; } = string.Empty;
        public string ProfileId { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public List<SceneAction> SceneActions { get; set; } = new List<SceneAction>();
        public List<ChecklistItem> ChecklistItems { get; set; } = new List<ChecklistItem>();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class ChecklistItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// "manual" = streamer ticks off, "auto" = server auto-verifies (e.g. "agent_connected", "scenes_exist").
        /// </summary>
        public string CheckType { get; set; } = "manual";

        /// <summary>
        /// For auto-checks, the condition key (e.g. "agent_connected", "streaming_software_detected").
        /// </summary>
        public string? AutoCheckKey { get; set; }

        public int SortOrder { get; set; }
    }
}
