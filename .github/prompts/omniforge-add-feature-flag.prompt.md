---
description: "Step-by-step protocol for adding a new user feature flag end-to-end in OmniForge"
tools:
  - fetch_webpage
  - codebase
  - editFiles
  - problems
  - findTestFiles
---

You are adding a new user feature flag to **OmniForge**. This protocol covers the full end-to-end path from the `UserFeatures` entity through API authorization, Blazor UI gating, and admin toggle UI. Follow in order.

---

## Step 1 — Fetch .NET Best Practices

Run the `/fetch-dotnet-bestpractices` prompt first to load DI lifetime guidance and secure coding practices.

---

## Step 2 — Understand the Existing Feature Flag System

Search the codebase for:

- `UserFeatures` — the Core entity holding all feature flag properties
- `User.Features` — how the entity is attached to the User domain model
- `UserTableEntity` — how `features` JSON is deserialized from Azure Table Storage
- An existing feature flag check like `user.Features.DiscordNotifications` for reference

**Feature flag lifecycle:**

```
UserFeatures property (bool, default false)
  └─► Serialized as JSON in UserTableEntity.features column (Azure Table)
  └─► Deserialized into User.Features on every user load
  └─► Checked in: API controllers, Blazor pages, service methods
  └─► Toggled by: Admin API endpoint / Admin Blazor UI
```

---

## Step 3 — Add the Property to UserFeatures (Core)

Open `OmniForge.DotNet/src/OmniForge.Core/Entities/UserFeatures.cs` (or wherever `UserFeatures` is defined).

Add the new flag — default to `false` unless this feature should be on for all users immediately:

```csharp
/// <summary>
/// Enables [describe what the feature does].
/// Controlled by admin or user settings.
/// </summary>
public bool MyNewFeature { get; set; } = false;
```

`UserFeatures` is serialized/deserialized as a JSON blob in `UserTableEntity.features`. No migration needed — missing JSON properties deserialize to their `= false` defaults automatically.

---

## Step 4 — Gate in API Controllers

In every API controller endpoint that the feature protects, add the flag check **after** authenticating the user:

```csharp
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? User.FindFirst("sub")?.Value;
if (string.IsNullOrEmpty(userId)) return Unauthorized();

var user = await _userRepo.GetUserAsync(userId);
if (user == null) return Unauthorized();

if (!user.Features.MyNewFeature)
    return StatusCode(403, new { error = "Feature not enabled" });
```

Never trust client-side checks — the server-side gate must exist even if the UI hides the feature.

---

## Step 5 — Gate in Blazor Pages / Components

In the Blazor component's `OnInitializedAsync` (or `OnParametersSetAsync`):

```csharp
@inject NavigationManager NavigationManager

protected override async Task OnInitializedAsync()
{
    var user = await UserService.GetCurrentUserAsync();
    if (user == null || !user.Features.MyNewFeature)
    {
        NavigationManager.NavigateTo("/", replace: true);
        return;
    }
    // ... load page data
}
```

Also conditionally hide nav menu links:

```razor
@if (currentUser?.Features.MyNewFeature == true)
{
    <NavLink href="/my-feature-page">My Feature</NavLink>
}
```

---

## Step 6 — Add Admin Toggle UI

Find the admin settings Blazor component (likely `OmniForge.Web/Components/Pages/Settings/Admin.razor` or similar).

Add the toggle alongside existing feature flag toggles:

```razor
<div class="feature-toggle">
    <label>
        <InputCheckbox @bind-Value="selectedUser.Features.MyNewFeature" />
        My New Feature — [brief description of what it does]
    </label>
</div>
```

Wire the save button to call the admin API endpoint that serializes `UserFeatures` back to the database.

---

## Step 7 — Add Admin API Endpoint Support

Verify the admin controller's user-update endpoint includes `MyNewFeature` in its accepted payload (or uses the full `UserFeatures` object). If the admin endpoint serializes the entire `UserFeatures` object, no change is needed.

If the flag needs a dedicated admin toggle endpoint:

```csharp
[HttpPost("admin/users/{targetUserId}/features/mynewfeature")]
[Authorize(Roles = "admin")]
public async Task<IActionResult> ToggleMyNewFeature(string targetUserId, [FromBody] bool enabled)
{
    // targetUserId comes from route — validate it's not empty
    if (string.IsNullOrEmpty(targetUserId)) return BadRequest();

    var user = await _userRepo.GetUserAsync(targetUserId);
    if (user == null) return NotFound();

    user.Features.MyNewFeature = enabled;
    await _userRepo.SaveUserAsync(user);

    _logger.LogInformation("✅ Admin toggled MyNewFeature={Enabled} for {User}", enabled, user.Username);
    return Ok();
}
```

---

## Step 8 — Write Tests

Required test cases (minimum):

```csharp
// Controller tests
[Fact] Task MyFeatureEndpoint_FeatureDisabled_Returns403()
[Fact] Task MyFeatureEndpoint_FeatureEnabled_ReturnsOk()
[Fact] Task MyFeatureEndpoint_NoAuth_Returns401()

// Admin toggle tests
[Fact] Task ToggleMyNewFeature_AdminUser_SavesFlag()
[Fact] Task ToggleMyNewFeature_NotAdmin_Returns403()

// Serialization round-trip
[Fact] UserFeatures_NewPropertyDefault_IsFalse()
[Fact] UserFeatures_DeserializesFromJson_WithMissingProperty_DefaultsFalse()
```

**Coverage gate: ≥85% on all new production code.**

---

## Checklist Before Committing

- [ ] `UserFeatures` property added (default `false`)
- [ ] Existing users: confirmed missing JSON property deserializes to `false` (no data migration needed)
- [ ] Server-side gate in every relevant API controller (cannot be bypassed from client)
- [ ] Blazor page/component redirects away if feature disabled
- [ ] Nav menu link hidden when feature disabled
- [ ] Admin can toggle the flag via UI or API
- [ ] Tests cover: disabled, enabled, no-auth, admin-only toggle
