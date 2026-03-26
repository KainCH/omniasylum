---
description: "Systematic bug investigation protocol for OmniForge — from symptom to fix to verified test"
tools:
  - codebase
  - fetch_webpage
  - editFiles
  - problems
  - runCommands
  - findTestFiles
  - usages
---

You are investigating a bug in **OmniForge**. Follow this systematic protocol to diagnose, isolate, fix, and verify. Do not apply fixes until the root cause is confirmed.

---

## Step 1 — Open a GitHub Issue

Before touching any code, open a tracking issue using the GitHub MCP tools (`mcp_github_create_issue`). This gives the bug a number that ties the investigation, commit, and PR together.

**Issue title format:** `bug: <concise description of the symptom>` (e.g., `bug: bot shoutouts bleeding across streams for same broadcaster`)

**Issue body template:**

```markdown
## Symptom
<!-- What was expected vs what actually happened -->

## Reproduction Steps
<!-- Numbered steps to trigger the bug reliably, or "intermittent — cannot reproduce reliably" -->

## Affected Scope
<!-- One user / all users / admin only / specific feature -->

## Error / Stack Trace
<!-- Paste exact error text or "none observed" -->

## Notes
<!-- Deployment or commit when it started, if known -->
```

Apply the label `bug` if it exists in the repo. Note the issue number — it goes in the commit footer as `Closes: #<number>` (GitHub auto-closes the issue when the PR merges to main).

If the bug is a **security issue** (cross-tenant data access, auth bypass), mark the issue as confidential / private and do not describe the vulnerability publicly until patched.

---

## Step 2 — Gather the Symptom

Before touching any code, collect:

1. **What was expected** vs **what actually happened**
2. **Which user(s) or tenant(s)** are affected (one user? all users? admin only?)
3. **When** it started — after which deployment or code change
4. **Reproduction steps** — can it be triggered reliably?
5. **Error messages** — exact text, stack trace if available

If this is a **production issue**:

```powershell
# View recent Azure Container App logs
az containerapp logs show \
  --name omniforge-api-dev \
  --resource-group OmniForge-Dev-RG \
  --tail 100
```

Search the logs for the error message, the affected `UserId`, and the timeframe.

---

## Step 3 — Multi-Tenancy Audit

The most common class of bugs in OmniForge is **cross-tenant data leakage or missing tenant scoping**. Check immediately:

- Does the buggy code path extract `userId` from JWT claims correctly?
  ```csharp
  // Correct
  var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
  ```
- Does any repository call **lack** `TwitchUserId` as the first argument?
- Search for any query that scans the full table without a PartitionKey filter:

  ```csharp
  // ❌ Bug — returns ALL users' data
  QueryAsync<TableEntity>()

  // ✅ Correct — scoped to one tenant
  QueryAsync<TableEntity>(filter: e => e.PartitionKey == twitchUserId)
  ```

- Can User A trigger the bug to see User B's data? If yes, this is a **security vulnerability** — treat as P0, fix before anything else.

---

## Step 4 — Check EventSub Handler Guard

If the bug is in an EventSub handler:

1. Is `TryGetBroadcasterId(eventData, out var broadcasterId)` the **first meaningful call**?
2. If `TryGetBroadcasterId` returns `false`, does the handler **return immediately**?
3. Does the handler **check the feature flag** (`user.Features.XxxFeature`) before doing work?
4. Is `ScopeFactory.CreateScope()` used correctly — scope disposed with `using`?

```csharp
// Canonical guard pattern — must be at the top of every HandleAsync
if (!TryGetBroadcasterId(eventData, out var broadcasterId))
{
    Logger.LogWarning("⚠️ {Handler}: missing broadcaster_user_id", nameof(MyHandler));
    return;
}

using var scope = ScopeFactory.CreateScope();
var user = await scope.ServiceProvider
    .GetRequiredService<IUserRepository>()
    .GetUserAsync(broadcasterId!);
if (user is null) return;
if (!user.Features.MyFlag) return;
```

---

## Step 5 — Check for Async Deadlocks

Search near the bug for `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`:

```csharp
// ❌ Deadlock risk in async context
var result = someAsyncMethod().Result;
someAsyncMethod().Wait();

// ✅ Correct
var result = await someAsyncMethod();
```

In Blazor Server, **all component lifecycle methods must be async** — `async Task OnInitializedAsync()`, never `void OnInitialized()` with `.Wait()`.

---

## Step 6 — Check Bot Service Session State

If a bot service (moderation, reaction, shoutout, scheduled messages) is misbehaving **between streams** — wrong users getting shoutouts, moderation strikes carrying over, duplicate reactions — the likely cause is missing or incorrect session reset.

Check:

1. Is `ResetSession(broadcasterId)` called in `StreamOfflineHandler` for this service?
2. Is all session state stored in `ConcurrentDictionary` fields keyed by `broadcasterId` — not a global variable or a key that ignores the broadcaster?
3. Is any `HashSet` inside the dictionary mutated without a lock?

```csharp
// ✅ Correct — keyed by broadcasterId, cleared on stream end
private readonly ConcurrentDictionary<string, HashSet<string>> _state = new();

public void ResetSession(string broadcasterId)
{
    _state.TryRemove(broadcasterId, out _);
}
```

```csharp
// ❌ Bug — global state bleeds across all broadcasters
private readonly HashSet<string> _state = new();
```

If `ResetSession` exists but isn't being called, open `StreamOfflineHandler.cs` and confirm the service is injected and the call is present.

---

## Step 7 — Check Feature Flag Gating

If the bug is "feature is enabled but not working" or "feature is disabled but still accessible":

1. Is the feature flag checked **server-side** in the controller (not just in the Blazor UI)?
2. Does the Blazor page redirect away if the flag is `false`?
3. Was the `UserFeatures` JSON in Azure Table Storage updated? Check for deserialization issues:
   ```csharp
   // Missing JSON property → should default to false, not throw
   var features = JsonSerializer.Deserialize<UserFeatures>(json, _jsonOptions) ?? new UserFeatures();
   ```

---

## Step 8 — Check Discord Notification Issues

If Discord messages are not sending, not routing correctly, or send duplicate messages:

1. Is `user.Features.DiscordNotifications` checked before sending?
2. Is the correct channel selected (default `DiscordChannelId` vs mod `DiscordModChannelId`)?
3. Is `DiscordNotificationTracker` recording failures? Check `GetLastNotification(userId)` — it tracks success/failure of the last send per user, useful for diagnosing repeated failures
4. Is the embed within Discord limits? (Title ≤256, description ≤4096, field value ≤1024)
5. Is the bot token valid — check Key Vault access?

---

## Step 9 — Check Overlay/WebSocket Issues

If overlay events are not reaching the browser, or are reaching the wrong user's overlay:

1. Is `userId` passed correctly to `IOverlayNotifier.NotifyXxxAsync(userId, ...)`?
2. Is `WebSocketOverlayManager.SendToUserAsync(userId, ...)` routing to the right connection?
3. In the browser JS, is the overlay connected to the correct `userId` query parameter?
4. Check `CompositeOverlayNotifier` — all three notifiers (WebSocket, SSE, SignalR) should forward the event.

---

## Step 10 — Write a Failing Test First

Before applying any fix, write a test that demonstrates the bug:

```csharp
[Fact]
public async Task BugRepro_DescribeTheBug()
{
    // Arrange — set up the state that triggers the bug
    _repoMock.Setup(...);

    // Act — invoke the buggy code path
    var result = await _sut.BuggyMethod(...);

    // Assert — this should FAIL before the fix, PASS after
    result.Should().Be(expectedValue);
}
```

This test becomes a permanent regression guard.

---

## Step 11 — Apply the Fix

With the root cause confirmed and a failing test written:

1. Make the minimal change needed — don't refactor surrounding code unless it caused the bug
2. Apply the fix
3. Run `dotnet test --filter "FullyQualifiedName~BugReproDescription"` — the test should now pass
4. Run the full test suite: `dotnet test OmniForge.DotNet/OmniForge.sln`
5. Check no new failures were introduced

---

## Step 12 — Verify Coverage Still ≥85%

```powershell
dotnet test OmniForge.DotNet/OmniForge.sln --collect:"XPlat Code Coverage"
```

If coverage dropped below 85%, add tests until it is restored before committing.

---

## Step 13 — Deployment Verification (for production fixes)

After deploying:

1. Confirm `provisioningState: Succeeded` and `runningStatus: Running`
2. Check health endpoint responds: `GET /api/health`
3. Check Azure Container App logs for the specific error — confirm it no longer appears
4. Have the affected user reproduce the original steps — confirm it is resolved

---

## Common Bug Patterns in OmniForge

| Symptom                                      | Likely Cause                                                              |
| -------------------------------------------- | ------------------------------------------------------------------------- |
| User sees another user's data                | Missing PartitionKey filter in repository query                           |
| EventSub handler silently does nothing       | Missing `TryGetBroadcasterId` guard or scope not created                  |
| Overlay stops updating after some time       | WebSocket disconnect not triggering reconnect                             |
| Discord message not sent                     | Feature flag not checked / wrong channel / token expired                  |
| 403 on feature that should be accessible     | `UserFeatures` JSON not serialized back to table on last save             |
| Blazor page hangs on load                    | `.Result` or `.Wait()` deadlock in `OnInitializedAsync`                   |
| Counter value wrong after rapid clicks       | Race condition — missing optimistic concurrency or upsert ordering        |
| `NullReferenceException` in EventSub handler | `UnwrapEvent` not called before accessing `eventData.broadcaster_user_id` |
| Bot behavior bleeding across streams         | `ResetSession` not wired into `StreamOfflineHandler` for this service     |
| Autoban firing on low-confidence detections  | `SuspiciousUserMessageHandler` not checking `ban_evasion_evaluation == "likely"` |

---

## Checklist Before Committing the Fix

- [ ] GitHub issue opened and issue number noted
- [ ] Root cause confirmed — not just symptom addressed
- [ ] Multi-tenancy boundary intact — no cross-tenant data accessible
- [ ] Failing test written that demonstrates the bug
- [ ] Fix applied — test now passes
- [ ] Full test suite passes: `dotnet test OmniForge.DotNet/OmniForge.sln`
- [ ] Coverage still ≥85%
- [ ] No `.Result`/`.Wait()` introduced
- [ ] No secrets logged
- [ ] Commit footer includes `Closes: #<issue-number>` (auto-closes issue on merge)
- [ ] If security issue: issue marked private; documented as security fix in PR description
