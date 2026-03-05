using System.Collections.Concurrent;

namespace OmniForge.Web.Services
{
    public class AgentPairingService
    {
        private readonly ConcurrentDictionary<string, PairingEntry> _entries = new();

        public bool TryRegisterCode(string code, DateTimeOffset expiresAt)
        {
            PurgeLazy();
            var entry = new PairingEntry { Code = code, ExpiresAt = expiresAt };
            return _entries.TryAdd(code, entry);
        }

        public bool TryApprove(string code, string userId, string token)
        {
            if (!_entries.TryGetValue(code, out var entry))
                return false;

            if (entry.ExpiresAt < DateTimeOffset.UtcNow)
            {
                _entries.TryRemove(code, out _);
                return false;
            }

            entry.UserId = userId;
            entry.Token = token;
            entry.IsApproved = true;
            return true;
        }

        public PairingEntry? TryPoll(string code)
        {
            // Purge expired entries except the one being polled
            PurgeLazy(excludeCode: code);

            if (!_entries.TryGetValue(code, out var entry))
                return null;

            if (entry.ExpiresAt < DateTimeOffset.UtcNow)
            {
                _entries.TryRemove(code, out _);
                return new PairingEntry { Code = code, IsExpired = true };
            }

            if (entry.IsApproved)
            {
                _entries.TryRemove(code, out _);
            }

            return entry;
        }

        private void PurgeLazy(string? excludeCode = null)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _entries)
            {
                if (kvp.Key == excludeCode) continue;
                if (kvp.Value.ExpiresAt < now)
                {
                    _entries.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    public class PairingEntry
    {
        public string Code { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public string? UserId { get; set; }
        public string? Token { get; set; }
        public bool IsApproved { get; set; }
        public bool IsExpired { get; set; }
    }
}
