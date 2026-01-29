using System.Collections.Generic;

namespace OmniForge.Core.Utilities
{
    public interface ILogValueSanitizer
    {
        string Safe(string? value);
        string Safe(object? value);
        string JoinSafe(IEnumerable<string?>? values, string separator = ", ");
    }
}
