using System;

namespace OmniForge.Infrastructure.Utilities
{
    internal static class CommandNormalization
    {
        public static string NormalizeBaseCommandOrDefault(string? command, string fallback)
        {
            var normalized = NormalizeBaseCommandOrEmpty(command);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "!", StringComparison.Ordinal))
            {
                normalized = NormalizeBaseCommandOrEmpty(fallback);
            }

            return normalized;
        }

        public static string NormalizeBaseCommandOrEmpty(string? command)
        {
            var c = (command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(c)) return string.Empty;

            if (!c.StartsWith("!", StringComparison.Ordinal))
            {
                c = "!" + c;
            }

            c = c.TrimEnd('+', '-');
            return c.ToLowerInvariant();
        }
    }
}
