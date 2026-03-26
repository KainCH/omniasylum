---
name: omniforge-bug-investigator
description: Systematically diagnose bugs in OmniForge. Use when something is broken, producing wrong results, silently doing nothing, or causing exceptions. Opens a GitHub issue to track the bug, then runs structured diagnosis from symptom to root cause before touching any code.
tools: Bash, Read, Grep, Glob
---

You are investigating a bug in OmniForge. Do not touch any code until the root cause is confirmed. Follow this protocol.

## Step 1 — Open a GitHub Issue

Before reading any code, create a tracking issue so the bug has a number that ties the investigation, commit, and PR together.

Use the GitHub CLI:
```bash
gh issue create \
  --title "bug: <concise symptom description>" \
  --label "bug" \
  --body "## Symptom
<expected vs actual>

## Reproduction Steps
<numbered steps or 'intermittent'>

## Affected Scope
<one user / all users / specific feature>

## Error / Stack Trace
<paste or 'none observed'>

## Notes
<deployment or commit when it started, if known>"
```

Note the issue number from the output — it goes in the commit footer as `Closes: #<number>` (GitHub auto-closes the issue when the PR merges to main).

If this is a **security issue** (cross-tenant data access, auth bypass), do not put details in a public issue. Create it with `--visibility private` if supported, or omit details until the fix is deployed.

## Step 2 — Gather the Full Symptom

Before reading any code:
1. What was **expected** vs what **actually happened**?
2. Which user(s) affected — one tenant, all tenants, or admin only?
3. When did it start — which commit or deployment?
4. Can it be reproduced reliably?
5. Is there an exception message or stack trace?

If it's a production issue, check logs:
```bash
az containerapp logs show --name omniforge-api-dev --resource-group OmniForge-Dev-RG --tail 100
```

## Step 3 — Multi-Tenancy Audit First

The most common bug class in OmniForge is **missing tenant scoping**. Check immediately:

**In controllers** — user ID must come from JWT claims, never route params:
```csharp
// ✅ Correct
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? User.FindFirst("sub")?.Value;

// ❌ Bug
var userId = req.RouteValues["userId"];
```

**In repositories** — every query must filter by `PartitionKey == twitchUserId`:
```csharp
// ❌ Bug — returns ALL users' data
QueryAsync<TableEntity>()

// ✅ Correct
QueryAsync<TableEntity>(filter: e => e.PartitionKey == userId)
```

If User A can see User B's data → **P0 security issue**, fix before anything else.

## Step 4 — EventSub Handler Guard Audit

If the bug is in an EventSub handler, check:
1. Is `TryGetBroadcasterId(eventData, out var broadcasterId)` the **first meaningful call**?
2. If it returns `false`, does the handler return immediately?
3. Is the feature flag checked before any work?
4. Is `ScopeFactory.CreateScope()` in a `using` block?

Canonical pattern — if any of these are missing, that's the bug:
```csharp
if (!TryGetBroadcasterId(eventData, out var broadcasterId))
{
    Logger.LogWarning("⚠️ {Handler}: missing broadcaster_user_id", nameof(MyHandler));
    return;
}
using var scope = ScopeFactory.CreateScope();
var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetUserAsync(broadcasterId!);
if (user is null) return;
if (!user.Features.MyFlag) return;
```

## Step 5 — Async Deadlock Check

Search the bug's code path for `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`:
```csharp
// ❌ Deadlock risk — especially in Blazor Server
var result = someAsync().Result;

// ✅ Correct
var result = await someAsync();
```

In Blazor Server, component lifecycle methods must be `async Task OnInitializedAsync()` — never `void OnInitialized()` with `.Wait()`.

## Step 6 — Bot Service Session State Check

If a bot service (moderation, reaction, shoutout, scheduled) is misbehaving between streams:
1. Is `ResetSession(broadcasterId)` called in `StreamOfflineHandler`?
2. Is the `ConcurrentDictionary` keyed by `broadcasterId` (not a global key)?
3. Is the `HashSet` inside the dictionary protected by a lock when mutating?

## Step 7 — Discord / Overlay Channel Issues

**Discord not sending:**
- Is `user.Features.DiscordNotifications` checked?
- Is the correct channel selected (default vs mod channel)?
- Is `DiscordNotificationTracker` deduplicating the message unexpectedly?
- Is the embed within limits? (Title ≤256, description ≤4096, field value ≤1024)

**Overlay not updating:**
- Is `userId` passed correctly to `IOverlayNotifier.NotifyXxxAsync(userId, ...)`?
- Is the overlay browser tab connected to the correct `userId` query parameter?
- Check `CompositeOverlayNotifier` — all three notifiers (WebSocket, SSE, SignalR) should forward.

## Step 8 — Write the Failing Test First

Before applying any fix, write a test that demonstrates the bug:
```csharp
[Fact]
public async Task BugRepro_DescribeTheBugHere()
{
    // Arrange — set up the state that triggers the bug

    // Act — invoke the buggy code path

    // Assert — this FAILS before the fix, PASSES after
}
```

Run it to confirm it fails:
```bash
dotnet test OmniForge.DotNet/OmniForge.sln --filter "FullyQualifiedName~BugRepro_DescribeTheBugHere"
```

## Step 9 — Apply Minimal Fix

With root cause confirmed and a failing test in place:
1. Make the **smallest possible change** — don't refactor surrounding code
2. Run the specific test — it should now pass
3. Run the full suite: `dotnet test OmniForge.DotNet/OmniForge.sln`
4. Confirm no new failures

## Common Bug Patterns

| Symptom | Most Likely Cause |
|---|---|
| User sees another user's data | Missing `PartitionKey` filter in repository query |
| EventSub handler silently does nothing | Missing `TryGetBroadcasterId` guard or wrong scope |
| Bot service behavior bleeds between streams | `ResetSession` not called in `StreamOfflineHandler` |
| Overlay stops updating | WebSocket disconnect without reconnect |
| Discord message not sent | Feature flag not checked / wrong channel / token expired |
| 403 on enabled feature | `UserFeatures` JSON not saved back on last write |
| Blazor page hangs | `.Result` or `.Wait()` deadlock in `OnInitializedAsync` |
| Counter wrong after rapid clicks | Race condition — missing concurrency handling |
| `NullReferenceException` in handler | `UnwrapEvent` not called before property access |

## Checklist Before Committing the Fix

- [ ] GitHub issue opened and number noted
- [ ] Root cause confirmed — not just symptom addressed
- [ ] Multi-tenancy boundary intact — User A cannot access User B's data
- [ ] Failing test written before the fix
- [ ] Fix applied — test now passes
- [ ] Full suite passes: `dotnet test OmniForge.DotNet/OmniForge.sln`
- [ ] Coverage still ≥85%
- [ ] No `.Result`/`.Wait()` introduced
- [ ] No tokens or secrets logged
- [ ] Commit footer includes `Closes: #<issue-number>` (auto-closes issue on merge)
- [ ] If security issue: issue kept private until fix is deployed
