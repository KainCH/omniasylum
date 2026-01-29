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

            // Prevent log forging / injection by stripping control characters (CR/LF, tabs, nulls, etc.).
            // This intentionally does NOT redact values; it only removes characters that could alter log structure.
            var buffer = new char[input.Length];
            var length = 0;

            foreach (var ch in input)
            {
                if (!char.IsControl(ch))
                {
                    buffer[length++] = ch;
                }
            }

            return length == input.Length
                ? input
                : new string(buffer, 0, length);
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
