---
description: "Step-by-step protocol for adding or modifying counter types and counter-related features in OmniForge"
tools:
  - fetch_webpage
  - codebase
  - editFiles
  - problems
  - findTestFiles
---

You are adding or modifying a counter feature in **OmniForge**. Follow this protocol exactly, in order.

---

## Step 1 — Fetch .NET Best Practices

Run the `/fetch-dotnet-bestpractices` prompt first to load async patterns, DI lifetime guidance, and security practices into context before writing any code.

---

## Step 2 — Understand the Counter Data Flow

Search the codebase for the existing counter pipeline:

```
Chat command / EventSub event
  └─► CounterController (REST) or EventSub handler
        └─► ICounterRepository.SaveCountersAsync(counter)
              └─► INotificationService.CheckAndSendMilestoneNotificationsAsync(user, type, prev, new)
                    ├─► IDiscordService.SendNotificationAsync(...)  [Discord milestone]
                    ├─► ITwitchClientManager.SendMessageAsync(...)  [Twitch chat]
                    └─► IOverlayNotifier.NotifyMilestoneReachedAsync(...)  [Browser overlay]
              └─► IOverlayNotifier.NotifyCounterUpdateAsync(userId, counter)  [Real-time UI update]
```

Key files to read before changing anything:

- `OmniForge.Core/Entities/Counter.cs` — the domain model
- `OmniForge.Infrastructure/Repositories/CounterRepository.cs` — Azure Table mapping
- `OmniForge.Core/Interfaces/INotificationService.cs` — milestone fanout contract
- `OmniForge.Core/Interfaces/IOverlayNotifier.cs` — overlay event contract

---

## Step 3 — Update the Counter Entity (if adding a new counter field)

Open `OmniForge.DotNet/src/OmniForge.Core/Entities/Counter.cs`.

Add the new property — use `int` for a standard counter:

```csharp
/// <summary>My new counter type description.</summary>
public int MyCounter { get; set; }
```

For a **custom counter**, do NOT add a fixed property — custom counters go in `CustomCounters` (`Dictionary<string, int>`).

---

## Step 4 — Update CounterRepository (Azure Table mapping)

Open `OmniForge.DotNet/src/OmniForge.Infrastructure/Repositories/CounterRepository.cs`.

**Read side** — add to `GetCountersAsync` entity mapping:

```csharp
MyCounter = GetInt32SafeCaseInsensitive(entity, "MyCounter"),
```

**Write side** — add to `SaveCountersAsync` entity construction:

```csharp
["MyCounter"] = counter.MyCounter,
```

No schema migrations needed — Azure Table Storage is schema-less. New columns appear automatically on first write.

---

## Step 5 — Update API Controller Endpoint

Open `OmniForge.DotNet/src/OmniForge.Web/Controllers/CounterController.cs`.

Extract `userId` from JWT — **never use `req.Params` directly**:

```csharp
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? User.FindFirst("sub")?.Value;
if (string.IsNullOrEmpty(userId)) return Unauthorized();
```

Add or update the increment/decrement action:

```csharp
[HttpPost("mycounter/increment")]
public async Task<IActionResult> IncrementMyCounter()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(userId)) return Unauthorized();

    var counter = await _counterRepo.GetCountersAsync(userId) ?? new Counter { TwitchUserId = userId };
    var previousValue = counter.MyCounter;
    counter.MyCounter++;
    counter.LastUpdated = DateTimeOffset.UtcNow;

    await _counterRepo.SaveCountersAsync(counter);

    // Real-time overlay update
    await _overlayNotifier.NotifyCounterUpdateAsync(userId, counter);

    // Milestone check
    var user = await _userRepo.GetUserAsync(userId);
    if (user != null)
        await _notificationService.CheckAndSendMilestoneNotificationsAsync(user, "mycounter", previousValue, counter.MyCounter);

    return Ok(counter);
}
```

---

## Step 6 — Add Overlay Notification Method (if the overlay needs to react)

Open `OmniForge.DotNet/src/OmniForge.Core/Interfaces/IOverlayNotifier.cs`.

If the counter needs a dedicated overlay event (beyond the generic `NotifyCounterUpdateAsync`):

```csharp
Task NotifyMyCounterSpecialEventAsync(string userId, int newValue, string contextData);
```

Implement in all three notifiers:

- `OmniForge.Web/Services/WebSocketOverlayNotifier.cs`
- `OmniForge.Web/Services/SseOverlayNotifier.cs`
- `OmniForge.Web/Services/SignalROverlayNotifier.cs`
- `OmniForge.Web/Services/CompositeOverlayNotifier.cs` (delegates to all three)

Update the overlay JavaScript (`wwwroot/overlay.html` or `wwwroot/v2/js/overlay.js`) to handle the new event message type.

---

## Step 7 — Add Milestone Support (if this counter should have milestones)

Open `OmniForge.DotNet/src/OmniForge.Infrastructure/Services/NotificationService.cs`.

Add a case in `CheckAndSendMilestoneNotificationsAsync`:

```csharp
case "mycounter":
    eventType = "mycounter_milestone";
    thresholds = settings.MilestoneThresholds.MyCounter;  // add to MilestoneThresholds entity
    discordEnabledForType = settings.EnabledNotifications.MyCounterMilestone;
    break;
```

Add `MyCounter` to `MilestoneThresholds` and `MyCounterMilestone` to `DiscordEnabledNotifications` in Core entities.

---

## Step 8 — Update Blazor UI

Find the relevant Blazor component (likely under `OmniForge.Web/Components/Pages/`) and add:

- Display binding for the new counter value
- Increment/decrement button wired to the new controller endpoint
- Real-time update handler for the WebSocket/SSE event

---

## Step 9 — Write Tests

Required test cases (minimum):

```csharp
// CounterRepository tests
[Fact] Task GetCountersAsync_MapsMyCounterField_Correctly()
[Fact] Task SaveCountersAsync_WritesMyCounterField_ToTable()

// Controller tests (no WebApplicationFactory — construct manually with mocked deps)
[Fact] Task IncrementMyCounter_NoAuth_Returns401()
[Fact] Task IncrementMyCounter_ValidUser_IncrementsAndNotifies()
[Fact] Task IncrementMyCounter_SaveThrows_Returns500()

// NotificationService tests (if milestone added)
[Fact] Task CheckMilestone_MyCounter_FiresAtConfiguredThreshold()
[Fact] Task CheckMilestone_MyCounter_NoThresholdCrossed_DoesNotNotify()
```

**Coverage gate: ≥85% on all new production code.**

---

## Checklist Before Committing

- [ ] `Counter` entity updated (Core — no external deps)
- [ ] `CounterRepository` read and write sides updated
- [ ] Controller extracts `userId` from JWT claims (never from route params directly)
- [ ] `NotifyCounterUpdateAsync` called after every save for real-time UI
- [ ] Milestone support added (or explicitly left out with a comment explaining why)
- [ ] Overlay JS handler added/updated if a new overlay event type was added
- [ ] Multi-tenancy: all data scoped to `TwitchUserId` partition key — no cross-tenant queries
- [ ] Tests cover: auth, mapping, increment, error, milestone
