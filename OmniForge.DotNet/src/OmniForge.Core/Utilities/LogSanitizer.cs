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

        /// <summary>
        /// Sanitizes an email address for logging by masking the local part.
        /// Example: "john.doe@example.com" becomes "j***e@example.com"
        /// </summary>
        public static string SanitizeEmail(string? email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return string.Empty;
            }

            var sanitized = Sanitize(email);
            var atIndex = sanitized.IndexOf('@');

            if (atIndex <= 0)
            {
                // Not a valid email format, just mask most of it
                return sanitized.Length <= 2 ? "***" : $"{sanitized[0]}***";
            }

            var localPart = sanitized.Substring(0, atIndex);
            var domain = sanitized.Substring(atIndex);

            // Mask the local part, keeping first and last character
            if (localPart.Length <= 2)
            {
                return $"***{domain}";
            }

            return $"{localPart[0]}***{localPart[localPart.Length - 1]}{domain}";
        }
    }
}
