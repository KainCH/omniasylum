---
applyTo: "OmniForge.DotNet/**/*.cs"
---

# OmniForge — C# / .NET Development Instructions

> These instructions activate automatically when editing any C# file in OmniForge.DotNet.
> For live best practices documentation, run the `/fetch-dotnet-bestpractices` prompt.

## Solution Structure & Clean Architecture

```
OmniForge.sln
├── OmniForge.Core           ← Pure domain layer — ZERO external NuGet dependencies
│   ├── Entities/            ← Domain models (User, Counter, Alert, etc.)
│   ├── Interfaces/          ← All service + repository contracts
│   └── Constants/           ← AlertTemplates, CounterTypes, etc.
│
├── OmniForge.Infrastructure ← Implements Core interfaces — ALL external I/O lives here
│   ├── Services/            ← Service implementations (Twitch, Discord, EventSub, etc.)
│   ├── Repositories/        ← Azure Table Storage repository implementations
│   ├── Configuration/       ← Strongly-typed settings classes
│   ├── Models/              ← Infrastructure-only DTOs and models
│   └── DependencyInjection.cs  ← All service registrations
│
├── OmniForge.Web            ← Blazor Server + ASP.NET Core API
│   ├── Controllers/         ← REST API controllers
│   ├── Components/          ← Blazor components (.razor)
│   └── Program.cs           ← Startup, calls services.AddInfrastructure(config)
│
└── OmniForge.Tests          ← xUnit test project
    ├── Controllers/         ← Controller unit tests (no WebApplicationFactory)
    ├── Services/            ← Service unit tests
    └── Components/          ← bunit Blazor component tests
```

**Core layer rule:** If you're adding a reference to an external NuGet package inside `OmniForge.Core`, stop — it belongs in Infrastructure or Web instead.

## Code Coverage Requirement

**All new production code must maintain ≥85% code coverage.**

- Write tests alongside new code, not after
- Measure with `dotnet test --collect:"XPlat Code Coverage"`
- Infrastructure I/O wrappers are excluded (see below) — testable logic must be factored out
- Run a specific test: `dotnet test --filter "FullyQualifiedName~MyClassName.MyMethodName"`

## Dependency Injection

All services must use constructor injection. Never use `ServiceLocator` or `IServiceProvider`
from a constructor — use `IServiceScopeFactory` only for handler classes that need scoped services:

```csharp
// ✅ Correct — constructor injection
public class MyService : IMyService
{
    private readonly IUserRepository _userRepo;
    private readonly ILogger<MyService> _logger;

    public MyService(IUserRepository userRepo, ILogger<MyService> logger)
    {
        _userRepo = userRepo;
        _logger = logger;
    }
}

// ✅ Correct — scoped resolution inside a singleton (e.g. event handlers)
public override async Task HandleAsync(JsonElement eventData)
{
    using var scope = ScopeFactory.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
    // ...
}

// ❌ Wrong — don't resolve from DI container in constructor
public MyService(IServiceProvider provider)
{
    _repo = provider.GetService<IRepo>(); // Don't do this
}
```

Register services in `OmniForge.Infrastructure/DependencyInjection.cs`:
```csharp
// Singleton: services that maintain state or expensive connections
services.AddSingleton<IMyService, MyService>();

// Scoped: per-request services (repositories, unit-of-work)
services.AddScoped<IMyRepository, MyRepository>();

// Transient: lightweight, stateless services
services.AddTransient<IMyUtility, MyUtility>();
```

## Async/Await Rules

- **All I/O-bound operations must be async** — never call `.Result` or `.Wait()`
- Use `ConfigureAwait(false)` in library/infrastructure code (not in Blazor components)
- Prefer `Task` over `void` for async methods; use `async void` only for event handlers
- Use `CancellationToken` parameters for long-running operations

```csharp
// ✅ Correct
public async Task<Counter> GetCounterAsync(string userId, CancellationToken ct = default)
{
    return await _repository.GetAsync(userId, ct).ConfigureAwait(false);
}

// ❌ Wrong
public Counter GetCounter(string userId)
{
    return _repository.GetAsync(userId).Result; // Deadlock risk
}
```

## Excluding Infrastructure I/O From Coverage

Classes that directly wrap external I/O (Azure SDK calls, WebSocket connections, HTTP clients)
should have testable logic factored out into a separate processor/helper class, and the I/O
wrapper marked with:

```csharp
[ExcludeFromCodeCoverage(Justification = "Wraps external I/O — logic tested in XxxProcessor")]
public class NativeEventSubService : INativeEventSubService
{
    // Raw WebSocket I/O only — parsing logic is in EventSubMessageProcessor
}
```

**Pattern:** `NativeEventSubService` (excluded) + `EventSubMessageProcessor` (tested separately)

## Multi-Tenancy — CRITICAL

All data operations **must be scoped to a single user's `TwitchUserId`**.

```csharp
// ✅ In controllers — extract from JWT claims
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? User.FindFirst("sub")?.Value;
if (string.IsNullOrEmpty(userId))
    return Unauthorized();

var counters = await _counterRepo.GetCountersAsync(userId);

// ✅ In repositories — always include userId as partition key
public async Task<IEnumerable<Counter>> GetCountersAsync(string userId)
{
    var filter = TableClient.CreateQueryFilter<CounterEntity>(e => e.PartitionKey == userId);
    // ...
}

// ❌ Never do this — returns all users' data
public async Task<IEnumerable<Counter>> GetAllCounters()
{
    return await _table.QueryAsync<CounterEntity>().ToListAsync();
}
```

## Testing Patterns

**Controller tests** — construct manually with mocked dependencies (no WebApplicationFactory):
```csharp
public class MyControllerTests
{
    private readonly Mock<IMyService> _serviceMock = new();
    private readonly MyController _sut;

    public MyControllerTests()
    {
        _sut = new MyController(_serviceMock.Object);
        // Set up authenticated user context
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-123") }))
            }
        };
    }

    [Fact]
    public async Task GetCounters_ReturnsOk_WhenUserExists()
    {
        _serviceMock.Setup(s => s.GetCountersAsync("test-user-123"))
            .ReturnsAsync(new List<Counter>());

        var result = await _sut.GetCounters();

        result.Should().BeOfType<OkObjectResult>();
    }
}
```

**Service tests** — test logic with mocked repository dependencies:
```csharp
_repoMock.Setup(r => r.GetByTwitchUserIdAsync("user123"))
    .ReturnsAsync(new User { TwitchUserId = "user123", Username = "testuser" });
```

**Blazor component tests** — use bunit:
```csharp
var cut = RenderComponent<MyComponent>(parameters => parameters
    .Add(p => p.UserId, "user123"));
cut.Find("button").Click();
cut.WaitForAssertion(() => cut.Find(".success").Should().NotBeNull());
```

## Repository Pattern

All repositories:
1. Implement an interface from `OmniForge.Core/Interfaces/`
2. Inherit Azure Table Storage base logic
3. Call `InitializeAsync()` at startup (called from `Program.cs`)
4. Use `UseDevelopmentStorage=true` (Azurite) locally when no `AzureStorage:AccountName` configured

```csharp
public interface IMyRepository  // Lives in Core/Interfaces/
{
    Task<MyEntity?> GetAsync(string userId, string id);
    Task UpsertAsync(MyEntity entity);
    Task DeleteAsync(string userId, string id);
    Task InitializeAsync();
}
```

## Logging Conventions

```csharp
_logger.LogInformation("✅ {Event} processed for {User}", eventType, username);
_logger.LogWarning("⚠️ {Event} skipped — {Reason}", eventType, reason);
_logger.LogError(ex, "❌ Failed to process {Event} for {User}", eventType, username);
```

## Feature Flag Checks

Always check feature flags before executing optional functionality:
```csharp
if (!user.Features.StreamOverlay)
{
    _logger.LogDebug("⚠️ Overlay feature disabled for {User}", user.Username);
    return;
}
```

Feature flags live on `User.Features` (of type `UserFeatures`). New features should be
added as a property there and defaulted to `false` unless enabled by admin.

## Key Vault & Secrets

- Production secrets come from Azure Key Vault via Managed Identity — never hardcode them
- Local dev: use `appsettings.Development.json` (gitignored)
- Key Vault name configured via `KeyVaultName` config key in `appsettings.json`

## Reference Documentation

> Run `/fetch-dotnet-bestpractices` to pull official Microsoft .NET documentation into your session.

- ASP.NET Core dependency injection: https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection
- Clean Architecture with .NET: https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures
- ASP.NET Core best practices: https://learn.microsoft.com/aspnet/core/fundamentals/best-practices
- xUnit documentation: https://xunit.net/docs/getting-started/net/visual-studio
- Moq quickstart: https://github.com/devlooped/moq/wiki/Quickstart
- Azure Table Storage (.NET): https://learn.microsoft.com/azure/cosmos-db/table/quickstart-dotnet
- Blazor Server best practices: https://learn.microsoft.com/aspnet/core/blazor/performance
