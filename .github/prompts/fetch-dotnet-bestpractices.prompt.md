---
description: "Fetch Microsoft .NET best practices documentation and apply them alongside OmniForge's project conventions"
tools:
  - fetch_webpage
  - codebase
---

You are helping develop production-quality C# code for OmniForge, a .NET 9 / Blazor Server application following Clean Architecture principles.

## Step 1 — Fetch live Microsoft best practices documentation

Fetch the following pages and incorporate their content into your response context:

1. **ASP.NET Core performance best practices** — caching, async patterns, memory management:
   `https://learn.microsoft.com/en-us/aspnet/core/performance/performance-best-practices`

2. **Clean Architecture with ASP.NET Core** — layer separation, dependency rules, project structure:
   `https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures`

3. **Async/Await best practices in .NET** — `ConfigureAwait`, `ValueTask`, deadlock prevention:
   `https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios`

4. **Dependency injection in ASP.NET Core** — lifetime guidelines (Singleton/Scoped/Transient), scope validation:
   `https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection`

5. **Logging in .NET** — structured logging, log levels, high-performance logging with LoggerMessage:
   `https://learn.microsoft.com/en-us/dotnet/core/extensions/logging`

6. **Secure coding guidelines for .NET** — input validation, secrets management, OWASP guidance:
   `https://learn.microsoft.com/en-us/dotnet/standard/security/secure-coding-guidelines`

## Step 2 — Load OmniForge project conventions

Search the codebase for the following to understand established project patterns:

- `DependencyInjection.cs` — how services are currently registered (Singleton vs Scoped vs Transient)
- `BaseEventSubHandler` — the scoped-within-singleton pattern used for handlers
- Any existing use of `ConfigureAwait(false)` in Infrastructure services
- The `[ExcludeFromCodeCoverage]` pattern usage

## Step 3 — Apply best practices to the current task

Synthesize the fetched Microsoft guidance with OmniForge's existing conventions, then:

1. **Service lifetime** — identify the correct DI lifetime for the new code:
   - _Singleton_: If the service maintains long-lived state (connections, caches)
   - _Scoped_: If the service is per-request and may touch a DbContext/repository
   - _Transient_: If the service is stateless and cheap to create

2. **Async patterns** — confirm all I/O paths are fully async, no `.Result`/`.Wait()`, `CancellationToken` propagated

3. **Memory & allocations** — flag any unnecessary allocations in hot paths (event handlers, per-message processing)

4. **Logging** — use structured logging with `{Properties}` not string concatenation; use `LoggerMessage` for high-frequency log calls

5. **Security** — no secrets in logs, validate all external inputs (Twitch payloads, Discord channel IDs), respect multi-tenancy boundaries

6. **Testability** — confirm new code is testable (no hidden static dependencies, I/O wrapped behind interfaces), and that **≥85% code coverage** is achievable

Then proceed with implementing or reviewing the described .NET feature following both the fetched Microsoft guidance and OmniForge's established patterns.
