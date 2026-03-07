---
description: "Step-by-step protocol for adding a new Azure Table Storage repository to OmniForge"
tools:
  - fetch_webpage
  - codebase
  - editFiles
  - problems
  - findTestFiles
---

You are adding a new Azure Table Storage repository to **OmniForge**. Follow this protocol exactly, in order.

---

## Step 1 — Fetch .NET Best Practices

Run the `/fetch-dotnet-bestpractices` prompt first for DI lifetime guidance (repositories are Scoped), async patterns, and Azure Table Storage conventions.

---

## Step 2 — Understand the Existing Repository Pattern

Search the codebase for:

- `CounterRepository` or `UserRepository` — use as an implementation reference
- `ICounterRepository` — use as an interface reference
- `DependencyInjection.cs` — see how repositories are registered
- `Program.cs` — see how `InitializeAsync()` is called at startup

**Key rules for all repositories:**

- PartitionKey = `TwitchUserId` — **always**. This is the multi-tenancy boundary.
- RowKey = entity ID (meaningful string, not a GUID unless required)
- Interface defined in `OmniForge.Core/Interfaces/` — **zero external NuGet deps in Core**
- Implementation in `OmniForge.Infrastructure/Repositories/`
- `InitializeAsync()` creates the table if it doesn't exist — called from `Program.cs` at startup
- Local dev uses Azurite (`UseDevelopmentStorage=true`) when `AzureStorage:AccountName` is not configured

---

## Step 3 — Define the Core Interface

Create `OmniForge.DotNet/src/OmniForge.Core/Interfaces/IMyEntityRepository.cs`:

```csharp
namespace OmniForge.Core.Interfaces;

public interface IMyEntityRepository
{
    /// <summary>Creates the underlying Azure Table if it does not already exist.</summary>
    Task InitializeAsync();

    /// <summary>Gets the entity for a specific user + entity ID. Returns null if not found.</summary>
    Task<MyEntity?> GetAsync(string twitchUserId, string entityId);

    /// <summary>Lists all entities for a specific user.</summary>
    Task<IReadOnlyList<MyEntity>> ListAsync(string twitchUserId);

    /// <summary>Creates or updates (upserts) an entity.</summary>
    Task UpsertAsync(MyEntity entity);

    /// <summary>Deletes a specific entity. No-op if it does not exist.</summary>
    Task DeleteAsync(string twitchUserId, string entityId);
}
```

`MyEntity` is the domain entity (defined in `OmniForge.Core/Entities/`). It must not reference any Azure SDK types — Core has zero external dependencies.

---

## Step 4 — Define the Core Domain Entity

Create `OmniForge.DotNet/src/OmniForge.Core/Entities/MyEntity.cs`:

```csharp
namespace OmniForge.Core.Entities;

public class MyEntity
{
    public string TwitchUserId { get; set; } = string.Empty;  // always present — partition key
    public string Id { get; set; } = string.Empty;            // row key
    public string Name { get; set; } = string.Empty;
    // ... domain properties — no Azure types, no serialization attributes
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

---

## Step 5 — Implement the Repository

Create `OmniForge.DotNet/src/OmniForge.Infrastructure/Repositories/MyEntityRepository.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Repositories;

[ExcludeFromCodeCoverage(Justification = "Wraps Azure Table Storage I/O — logic tested via interface mocks")]
public class MyEntityRepository : IMyEntityRepository
{
    private readonly TableClient _tableClient;
    private readonly ILogger<MyEntityRepository> _logger;

    private const string TableName = "myentities";  // lowercase, Azure Table naming rules

    public MyEntityRepository(TableServiceClient tableServiceClient, ILogger<MyEntityRepository> logger)
    {
        _tableClient = tableServiceClient.GetTableClient(TableName);
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await _tableClient.CreateIfNotExistsAsync();
        _logger.LogInformation("✅ MyEntityRepository table '{Table}' ready", TableName);
    }

    public async Task<MyEntity?> GetAsync(string twitchUserId, string entityId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(twitchUserId, entityId);
            return MapToEntity(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<MyEntity>> ListAsync(string twitchUserId)
    {
        var filter = TableClient.CreateQueryFilter<TableEntity>(e => e.PartitionKey == twitchUserId);
        var results = new List<MyEntity>();
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter))
        {
            results.Add(MapToEntity(entity));
        }
        return results;
    }

    public async Task UpsertAsync(MyEntity entity)
    {
        var tableEntity = new TableEntity(entity.TwitchUserId, entity.Id)
        {
            ["Name"] = entity.Name,
            // ... map all properties
            ["CreatedAt"] = entity.CreatedAt,
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        };
        await _tableClient.UpsertEntityAsync(tableEntity, TableUpdateMode.Replace);
    }

    public async Task DeleteAsync(string twitchUserId, string entityId)
    {
        try
        {
            await _tableClient.DeleteEntityAsync(twitchUserId, entityId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No-op — already deleted
        }
    }

    private static MyEntity MapToEntity(TableEntity entity) => new()
    {
        TwitchUserId = entity.PartitionKey,
        Id = entity.RowKey,
        Name = entity.GetString("Name") ?? string.Empty,
        // ... map all properties
        CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.UtcNow,
        UpdatedAt = entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.UtcNow,
    };
}
```

**Azure Table naming rules:**

- Table names: 3–63 characters, alphanumeric only, start with letter
- PartitionKey + RowKey: no `/`, `\`, `#`, `?`, or control characters; max 1KB each
- Max 252 properties per entity; property values max 64KB (strings); total entity max 1MB

---

## Step 6 — Register in DependencyInjection.cs

Open `OmniForge.DotNet/src/OmniForge.Infrastructure/DependencyInjection.cs`.

Add alongside existing repository registrations:

```csharp
services.AddScoped<IMyEntityRepository, MyEntityRepository>();
```

Repositories are **Scoped** — one per HTTP request / service scope. Never Singleton.

---

## Step 7 — Call InitializeAsync in Program.cs

Open `OmniForge.DotNet/src/OmniForge.Web/Program.cs`.

Find the block where existing repositories have `InitializeAsync()` called and add:

```csharp
await app.Services.GetRequiredService<IMyEntityRepository>().InitializeAsync();
```

This runs once at startup to ensure the Azure Table exists before any request is served.

---

## Step 8 — Write Tests

Since the repository itself is `[ExcludeFromCodeCoverage]` (wraps I/O), tests focus on **consumers** of the interface. But if there's mapping logic worth unit testing, extract it to a static helper and test that helper:

```csharp
// Test the service that uses the repository
public class MyServiceTests
{
    private readonly Mock<IMyEntityRepository> _repoMock = new();
    private readonly MyService _sut;

    public MyServiceTests() => _sut = new MyService(_repoMock.Object);

    [Fact]
    public async Task GetItem_ReturnsNull_WhenNotFound()
    {
        _repoMock.Setup(r => r.GetAsync("user1", "item1")).ReturnsAsync((MyEntity?)null);
        var result = await _sut.GetItemAsync("user1", "item1");
        result.Should().BeNull();
    }
}
```

**Coverage gate: ≥85% on all services that use the new repository.**

---

## Checklist Before Committing

- [ ] Interface in `OmniForge.Core/Interfaces/` — zero external NuGet dependencies
- [ ] Domain entity in `OmniForge.Core/Entities/` — no Azure SDK types
- [ ] Repository in `OmniForge.Infrastructure/Repositories/` marked `[ExcludeFromCodeCoverage]`
- [ ] PartitionKey = `TwitchUserId` on every entity — multi-tenancy boundary enforced
- [ ] `InitializeAsync()` implemented (calls `CreateIfNotExistsAsync`)
- [ ] Registered as `Scoped` in `DependencyInjection.cs`
- [ ] `InitializeAsync()` called in `Program.cs` startup sequence
- [ ] `RequestFailedException` with status 404 handled gracefully in `GetAsync` and `DeleteAsync`
- [ ] Service-layer tests cover all code that uses the repository through the mocked interface
