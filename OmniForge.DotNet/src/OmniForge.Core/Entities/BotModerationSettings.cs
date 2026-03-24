using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class BotModerationSettings
    {
        // Spam-bot detection: caps + symbols → immediate delete + ban
        public bool AntiCapsEnabled { get; set; } = false;
        public int CapsPercentThreshold { get; set; } = 70;
        public int CapsMinMessageLength { get; set; } = 10;
        public bool AntiSymbolSpamEnabled { get; set; } = false;
        public int SymbolPercentThreshold { get; set; } = 60;

        // Link guard: delete → mark suspicious → ban on 2nd session violation
        public bool LinkGuardEnabled { get; set; } = false;
        public List<string> AllowedDomains { get; set; } = new();
    }
}
