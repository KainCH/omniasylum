---
description: "Step-by-step protocol for adding a new Blazor Server page with an API controller in OmniForge"
tools:
  - fetch_webpage
  - codebase
  - editFiles
  - problems
  - findTestFiles
---

You are building a new Blazor Server page with an ASP.NET Core API controller in **OmniForge**. Follow this protocol exactly, in order.

---

## Step 1 — Fetch .NET Best Practices

Run the `/fetch-dotnet-bestpractices` prompt first to load async patterns, Blazor Server performance guidance, and security practices before writing any code.

---

## Step 2 — Understand the Existing Patterns

Search the codebase for:

- An existing controller (e.g. `CounterController`) — use as the template for JWT extraction and error patterns
- An existing Blazor page (e.g. `OmniForge.Web/Components/Pages/`) — use as the template for component lifecycle and feature-flag gating. Note: pages resolve the current user via `AuthenticationStateProvider` + `IUserRepository`, not via `IUserService` (which does not exist)
- `OmniForge.Web/Program.cs` — understand the middleware pipeline and auth setup
- `OmniForge.Web/Components/Layout/NavMenu.razor` — where nav links are added

**Core architecture rules:**

- Controllers live in `OmniForge.Web/Controllers/` — never put business logic here, delegate to services
- Blazor pages live in `OmniForge.Web/Components/Pages/`
- Always extract `userId` from JWT claims — never trust route parameters alone
- All data operations are scoped to `TwitchUserId` — never return cross-tenant data

---

## Step 3 — Create the API Controller

Create `OmniForge.DotNet/src/OmniForge.Web/Controllers/MyFeatureController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MyFeatureController : ControllerBase
{
    private readonly IMyFeatureService _service;
    private readonly IUserRepository _userRepo;
    private readonly ILogger<MyFeatureController> _logger;

    public MyFeatureController(
        IMyFeatureService service,
        IUserRepository userRepo,
        ILogger<MyFeatureController> logger)
    {
        _service = service;
        _userRepo = userRepo;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyFeatureData()
    {
        // ALWAYS extract userId from JWT — never from request parameters
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Load user and check feature flag
        var user = await _userRepo.GetUserAsync(userId);
        if (user == null) return Unauthorized();
        if (!user.Features.MyNewFeature)
            return StatusCode(403, new { error = "Feature not enabled for this account" });

        try
        {
            var data = await _service.GetDataAsync(userId);
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GetMyFeatureData failed for {UserId}", userId);
            return StatusCode(500, new { error = "Failed to retrieve data" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateMyFeatureData([FromBody] MyFeatureUpdateRequest request)
    {
        if (request == null) return BadRequest(new { error = "Request body required" });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _userRepo.GetUserAsync(userId);
        if (user == null) return Unauthorized();
        if (!user.Features.MyNewFeature)
            return StatusCode(403, new { error = "Feature not enabled for this account" });

        try
        {
            await _service.UpdateDataAsync(userId, request);
            _logger.LogInformation("✅ MyFeatureData updated for {UserId}", userId);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ UpdateMyFeatureData failed for {UserId}", userId);
            return StatusCode(500, new { error = "Failed to update data" });
        }
    }
}
```

---

## Step 4 — Create the Blazor Page Component

Create `OmniForge.DotNet/src/OmniForge.Web/Components/Pages/MyFeaturePage.razor`:

```razor
@page "/my-feature"
@using System.Security.Claims
@using OmniForge.Core.Interfaces
@inject IUserRepository UserRepository
@inject NavigationManager NavigationManager
@inject AuthenticationStateProvider AuthenticationStateProvider
@inject ILogger<MyFeaturePage> Logger

<PageTitle>My Feature — OmniForge</PageTitle>

@if (!_loaded)
{
    <p>Loading...</p>
}
else if (!_hasFeature)
{
    <p>This feature is not enabled for your account.</p>
}
else
{
    <!-- Page content here -->
    <h2>My Feature</h2>
}

@code {
    private bool _loaded = false;
    private bool _hasFeature = false;
    private MyFeatureData? _data;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? authState.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                NavigationManager.NavigateTo("/login", replace: true);
                return;
            }

            var user = await UserRepository.GetUserAsync(userId);
            if (user == null)
            {
                NavigationManager.NavigateTo("/login", replace: true);
                return;
            }

            if (!user.Features.MyNewFeature)
            {
                // Redirect away — do not render feature markup for unauthorized users
                NavigationManager.NavigateTo("/", replace: true);
                return;
            }

            _hasFeature = true;
            _data = await LoadDataAsync(userId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "❌ Failed loading MyFeaturePage");
        }
        finally
        {
            _loaded = true;
        }
    }

    private async Task<MyFeatureData?> LoadDataAsync(string userId)
    {
        // Call your service or HTTP client here
        return null;
    }
}
```

**Note:** There is no `IUserService` in OmniForge. Blazor pages resolve the current user by getting claims from `AuthenticationStateProvider` and calling `IUserRepository.GetUserAsync(userId)` directly.

---

## Step 5 — Add Nav Menu Link

Open `OmniForge.DotNet/src/OmniForge.Web/Components/Layout/NavMenu.razor`.

Add the nav link conditionally (only show when the user has the feature):

```razor
@if (CurrentUser?.Features.MyNewFeature == true)
{
    <div class="nav-item px-3">
        <NavLink class="nav-link" href="my-feature">
            <span class="bi bi-icon-name" aria-hidden="true"></span> My Feature
        </NavLink>
    </div>
}
```

---

## Step 6 — Add Real-Time Updates (if needed)

If the page needs real-time data from the server (e.g. counter changes, stream status):

**Option A — Poll via `IOverlayNotifier` (for overlay-style events):**
Use the existing WebSocket/SSE overlay connection — see `WebSocketOverlayManager` and `IOverlayNotifier`.

**Option B — Blazor component state refresh:**

```csharp
// In @code block, subscribe to overlay hub events
protected override async Task OnInitializedAsync()
{
    // ... existing logic ...
    // Subscribe to relevant events if needed
}
```

**Option C — SignalR (for admin/multi-user scenarios):**
See `SignalROverlayNotifier` for the existing SignalR hub pattern.

---

## Step 7 — Write Tests

**Controller tests** — construct manually, no `WebApplicationFactory`:

```csharp
public class MyFeatureControllerTests
{
    private readonly Mock<IMyFeatureService> _serviceMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly MyFeatureController _sut;

    public MyFeatureControllerTests()
    {
        _sut = new MyFeatureController(
            _serviceMock.Object,
            _userRepoMock.Object,
            NullLogger<MyFeatureController>.Instance);

        // Set up authenticated user context
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "test-user-123")],
                    "TestAuth"))
            }
        };
    }

    [Fact] public async Task GetMyFeatureData_NoAuth_Returns401()
    [Fact] public async Task GetMyFeatureData_FeatureDisabled_Returns403()
    [Fact] public async Task GetMyFeatureData_FeatureEnabled_ReturnsOkWithData()
    [Fact] public async Task GetMyFeatureData_ServiceThrows_Returns500()
    [Fact] public async Task UpdateMyFeatureData_NullBody_Returns400()
    [Fact] public async Task UpdateMyFeatureData_ValidRequest_ReturnsOk()
}
```

**Blazor component tests** — use bunit:

```csharp
var cut = RenderComponent<MyFeaturePage>();
cut.WaitForAssertion(() => cut.Find(".my-feature-content").Should().NotBeNull());
```

**Coverage gate: ≥85% on all new production code.**

---

## Checklist Before Committing

- [ ] Controller: `userId` extracted from JWT claims only
- [ ] Controller: feature flag checked (server-side gate, not just UI)
- [ ] Controller: no business logic — delegates to service
- [ ] Blazor page: redirects immediately if feature disabled or user not authenticated
- [ ] Blazor page: `NavigationManager.NavigateTo("/", replace: true)` used (not `href` redirect)
- [ ] Nav menu: link hidden when feature disabled
- [ ] Error handling: all async calls wrapped in try/catch with structured logging
- [ ] Tests cover: no-auth, feature-disabled, success, service-error
- [ ] Multi-tenancy: controller never uses one user's userId to fetch another user's data
