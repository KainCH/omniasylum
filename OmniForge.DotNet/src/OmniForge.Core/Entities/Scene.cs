using System;

namespace OmniForge.Core.Entities
{
    public class Scene
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Which streaming software discovered this scene ("obs" or "streamlabs").
        /// </summary>
        public string Source { get; set; } = string.Empty;

        public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    }
}
