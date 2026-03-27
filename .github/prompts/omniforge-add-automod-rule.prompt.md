---
description: "Step-by-step protocol for adding a new auto-moderation rule to BotModerationService in OmniForge"
tools:
  - codebase
  - editFiles
  - problems
  - findTestFiles
---

You are adding a new automatic moderation rule to **OmniForge's** `BotModerationService`. Follow this protocol exactly.

---

## Step 1 — Understand the Existing Moderation Architecture

Read these files before writing anything:

- `OmniForge.Infrastructure/Services/BotModerationService.cs` — the main service; read `CheckAndEnforceAsync` in full
- `OmniForge.Core/Interfaces/IBotModerationService.cs` — the interface
- `OmniForge.Core/Entities/BotModerationSettings.cs` — where rule config lives
- `OmniForge.Infrastructure/Services/EventHandlers/ChatMessageHandler.cs` — how `CheckAndEnforceAsync` is called

Existing rules to understand before adding yours:
- **AntiCaps** — percentage of uppercase characters above a configurable threshold
- **AntiSymbolSpam** — percentage of non-alphanumeric characters above a threshold
- **LinkGuard** — blocks unapproved domains with a 2-strike-then-ban mechanism using `_linkViolations` `ConcurrentDictionary`

Note the enforcement pattern — each rule independently evaluates its condition, then calls `TwitchApiService` to delete the message or ban the user.

---

## Step 2 — Add Configuration Properties to BotModerationSettings (Core)

Open `OmniForge.Core/Entities/BotModerationSettings.cs` and add the new rule's settings:

```csharp
/// <summary>Enables the new rule.</summary>
public bool MyRuleEnabled { get; set; } = false;

/// <summary>Configurable threshold for the rule (0–100).</summary>
public int MyRuleThreshold { get; set; } = 50;
```

Settings serialize to JSON in Azure Table Storage — new properties default correctly for existing users without migration.

---

## Step 3 — Add Feature Flag (if this is a premium/admin-gated feature)

If the rule should be admin-enabled per user, add a flag to `OmniForge.Core/Entities/UserFeatures.cs`:

```csharp
/// <summary>Enables [rule name] auto-moderation.</summary>
public bool MyAutomodRule { get; set; } = false;
```

If the rule is controlled entirely by `BotModerationSettings.MyRuleEnabled` (user self-service), no feature flag is needed.

---

## Step 4 — Implement the Rule in BotModerationService

Open `OmniForge.Infrastructure/Services/BotModerationService.cs`.

Add the rule inside `CheckAndEnforceAsync`, following the existing pattern:

```csharp
// My new rule
if (user.BotModeration.MyRuleEnabled)
{
    var triggered = EvaluateMyRule(message, user.BotModeration.MyRuleThreshold);
    if (triggered)
    {
        await _twitchApiService.DeleteChatMessageAsync(broadcasterId, messageId, accessToken)
            .ConfigureAwait(false);
        await _twitchClientManager.SendMessageAsync(broadcasterId,
            $"@{chatterLogin} {MyRuleViolationMessage}")
            .ConfigureAwait(false);
        _logger.LogInformation("⚠️ MyRule violation by {Chatter} in {Broadcaster}",
            chatterLogin, broadcasterId);
        return; // Don't evaluate further rules after enforcement
    }
}
```

Extract the detection logic into a **private static method** so it can be unit tested without needing service dependencies:

```csharp
private static bool EvaluateMyRule(string message, int threshold)
{
    if (string.IsNullOrWhiteSpace(message)) return false;
    // ... pure logic, no I/O
    return result > threshold;
}
```

**For rules with session state (e.g. strike counting):**

```csharp
// Field on the class
private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _strikes = new();

// In ResetSession — clear strikes for broadcaster
public void ResetSession(string broadcasterId)
{
    _strikes.TryRemove(broadcasterId, out _);
    // ... existing reset logic
}
```

---

## Step 5 — Update the Interface (if ResetSession changes)

If your rule adds new session state that needs clearing, `IBotModerationService.ResetSession` already exists — no interface change needed. Only update the interface if you're adding a new public method.

---

## Step 6 — Update the Admin / Settings UI

Find the `AutomodSettings.razor` Blazor page and add the new rule's toggle and threshold inputs alongside existing ones.

Connect the save flow to persist the updated `BotModerationSettings` back to the user entity via the settings API.

---

## Step 7 — Write Tests

Create or extend `OmniForge.Tests/Services/BotModerationServiceTests.cs`.

**Required test cases for the detection logic (target the private static method via the service):**

```csharp
[Fact] Task CheckAndEnforceAsync_MyRule_Disabled_DoesNotEnforce()
[Fact] Task CheckAndEnforceAsync_MyRule_BelowThreshold_DoesNotEnforce()
[Fact] Task CheckAndEnforceAsync_MyRule_AboveThreshold_DeletesMessage()
[Fact] Task CheckAndEnforceAsync_MyRule_ModUser_IsExempt()
[Fact] Task CheckAndEnforceAsync_MyRule_BroadcasterUser_IsExempt()
```

**If strike-based:**
```csharp
[Fact] Task CheckAndEnforceAsync_MyRule_FirstViolation_WarnsUser()
[Fact] Task CheckAndEnforceAsync_MyRule_SecondViolation_BansUser()
[Fact] void ResetSession_ClearsStrikesForBroadcaster()
```

**Coverage gate: ≥85% on all new production code.**

---

## Checklist Before Committing

- [ ] `BotModerationSettings` updated with new rule's config properties
- [ ] Feature flag added to `UserFeatures` if admin-gated (default `false`)
- [ ] Detection logic in a private static method (pure — no I/O, no dependencies)
- [ ] Rule evaluated in `CheckAndEnforceAsync` — mods and broadcasters exempt
- [ ] Session state in `ConcurrentDictionary` (if strike-based), cleared in `ResetSession`
- [ ] Admin UI (`AutomodSettings.razor`) updated with toggle and config inputs
- [ ] Tests: disabled, below threshold, above threshold, mod exempt, broadcaster exempt
- [ ] Coverage ≥85%
