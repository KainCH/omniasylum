using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniForge.Core.Utilities
{
    public static class LogValue
    {
        public static string Safe(string? value)
        {
            return (value ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public static string Safe(object? value)
        {
            return Safe(value?.ToString());
        }

        public static string JoinSafe(IEnumerable<string?>? values, string separator = ", ")
        {
            if (values == null)
            {
                return string.Empty;
            }

            return string.Join(separator, values.Select(Safe));
        }
    }
}
