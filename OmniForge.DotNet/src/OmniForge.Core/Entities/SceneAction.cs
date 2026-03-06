using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class SceneAction
    {
        public string UserId { get; set; } = string.Empty;
        public string SceneName { get; set; } = string.Empty;

        /// <summary>
        /// Per-counter visibility overrides. Key = counter name (e.g. "Deaths", "Swears"),
        /// Value = "default" | "show" | "hide".
        /// </summary>
        public Dictionary<string, string> CounterVisibility { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Whether the timer is enabled for this scene. When false, any running timer is stopped on scene switch.
        /// </summary>
        public bool TimerEnabled { get; set; }

        /// <summary>
        /// Timer duration in minutes to auto-start when this scene activates. 0 = no timer.
        /// </summary>
        public int TimerDurationMinutes { get; set; }

        /// <summary>
        /// Whether to auto-start the timer when switching to this scene.
        /// </summary>
        public bool AutoStartTimer { get; set; }

        /// <summary>
        /// Overtime configuration for when the timer expires without a scene change.
        /// </summary>
        public OvertimeConfig Overtime { get; set; } = new OvertimeConfig();

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class OvertimeConfig
    {
        public bool Enabled { get; set; }
        public string Text { get; set; } = "OVERTIME!";
        public string TextColor { get; set; } = "#ff0000";
        public string BackgroundColor { get; set; } = "rgba(0, 0, 0, 0.8)";
        public int FlashIntervalSeconds { get; set; } = 2;
    }
}
