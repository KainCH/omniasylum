using OmniForge.Core.Entities;

namespace OmniForge.Tests
{
    internal static class CounterTestHelpers
    {
        public static bool HasCustomCounterValue(Counter c, string key, int expected)
        {
            return c.CustomCounters != null
                && c.CustomCounters.TryGetValue(key, out var value)
                && value == expected;
        }
    }
}
