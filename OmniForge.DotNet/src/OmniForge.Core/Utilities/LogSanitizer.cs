using System;

namespace OmniForge.Core.Utilities
{
    public static class LogSanitizer
    {
        public static string Sanitize(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            // Remove newlines and other control characters that could be used for log injection
            return input.Replace(Environment.NewLine, "")
                        .Replace("\n", "")
                        .Replace("\r", "");
        }

        public static object? Sanitize(object? input)
        {
            if (input == null)
            {
                return null;
            }
            return Sanitize(input.ToString());
        }
    }
}
