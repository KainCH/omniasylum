namespace OmniForge.Core.DTOs
{
    /// <summary>
    /// Payload sent by the SceneSync desktop app when OBS or Streamlabs reports a scene change.
    /// </summary>
    public class SceneChangeRequest
    {
        /// <summary>
        /// The name of the scene that is now active.
        /// </summary>
        public string SceneName { get; set; } = string.Empty;

        /// <summary>
        /// The name of the scene that was active before the switch (may be null on initial report).
        /// </summary>
        public string? PreviousScene { get; set; }

        /// <summary>
        /// Which streaming software reported the change ("OBS" or "Streamlabs").
        /// </summary>
        public string Source { get; set; } = "OBS";
    }
}
