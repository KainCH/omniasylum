using System;

namespace OmniForge.Core.Entities
{
    public sealed record GameCoreCountersConfig(
        string UserId,
        string GameId,
        bool DeathsEnabled,
        bool SwearsEnabled,
        bool ScreamsEnabled,
        bool BitsEnabled,
        DateTimeOffset UpdatedAt
    );
}
