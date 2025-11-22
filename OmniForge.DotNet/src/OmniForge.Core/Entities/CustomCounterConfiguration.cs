using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class CustomCounterConfiguration
    {
        public Dictionary<string, CustomCounterDefinition> Counters { get; set; } = new Dictionary<string, CustomCounterDefinition>();
    }

    public class CustomCounterDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int IncrementBy { get; set; } = 1;
        public List<int> Milestones { get; set; } = new List<int>();
    }
}
