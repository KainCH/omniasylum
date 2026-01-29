using System.Collections.Generic;

namespace OmniForge.Core.Utilities
{
    public sealed class LogValueSanitizer : ILogValueSanitizer
    {
        public string Safe(string? value) => LogValue.Safe(value);

        public string Safe(object? value) => LogValue.Safe(value);

        public string JoinSafe(IEnumerable<string?>? values, string separator = ", ")
            => LogValue.JoinSafe(values, separator);
    }
}
