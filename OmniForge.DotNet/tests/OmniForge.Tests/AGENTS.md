# OmniForge.Tests — Agent Context

This directory contains all xUnit tests. **All new production code must maintain ≥85% code coverage.**

## Test Commands

```bash
# Run all tests
dotnet test OmniForge.DotNet/OmniForge.sln

# Run a single test class
dotnet test OmniForge.DotNet/OmniForge.sln --filter "FullyQualifiedName~MyClassName"

# Run a single test method
dotnet test OmniForge.DotNet/OmniForge.sln --filter "FullyQualifiedName~MyClassName.MyMethodName"

# Run with coverage
dotnet test OmniForge.DotNet/OmniForge.sln --collect:"XPlat Code Coverage"
```

## Controller Test Pattern

Construct controllers manually — no `WebApplicationFactory`:

```csharp
public class MyControllerTests
{
    private readonly Mock<IMyService> _serviceMock = new();
    private readonly MyController _sut;

    public MyControllerTests()
    {
        _sut = new MyController(_serviceMock.Object, Mock.Of<ILogger<MyController>>());
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-123") }))
            }
        };
    }
}
```

Always test the feature-flag-disabled path (expect 403) and the unauthenticated path (expect 401).

## Service Test Pattern

Mock `IServiceScopeFactory` for services that use it internally:

```csharp
var scopeFactory = new Mock<IServiceScopeFactory>();
var scope = new Mock<IServiceScope>();
var serviceProvider = new Mock<IServiceProvider>();

scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
serviceProvider.Setup(p => p.GetService(typeof(IUserRepository)))
    .Returns(_userRepoMock.Object);

var sut = new MyBotService(Mock.Of<ILogger<MyBotService>>(), scopeFactory.Object);
```

## EventSub Handler Test Pattern

The handler class itself is `[ExcludeFromCodeCoverage]` — don't test the class directly. Test any processor/helper class extracted from it.

For handlers that have no extracted processor, test via the handler's `HandleAsync` method directly with mocked scope factory. Required cases for every handler:

```csharp
[Fact] Task HandleAsync_MissingBroadcasterId_ReturnsEarly()
[Fact] Task HandleAsync_UnknownBroadcaster_ReturnsEarly()
[Fact] Task HandleAsync_FeatureFlagDisabled_ReturnsEarly()
[Fact] Task HandleAsync_ValidEvent_PerformsExpectedAction()
[Fact] Task HandleAsync_ServiceThrows_LogsAndDoesNotCrash()
```

## Bot Service Test Pattern

Required cases for every bot service:

```csharp
[Fact] Task ActionAsync_UserNotFound_ReturnsEarly()
[Fact] Task ActionAsync_FeatureDisabled_ReturnsEarly()
[Fact] Task ActionAsync_ValidInput_PerformsExpectedBehavior()
[Fact] Task ActionAsync_ServiceThrows_DoesNotPropagate()
[Fact] void ResetSession_ClearsSessionState()
[Fact] void ResetSession_UnknownBroadcaster_DoesNotThrow()
```

## Blazor Component Test Pattern

Uses bunit:

```csharp
var cut = RenderComponent<MyComponent>(parameters => parameters
    .Add(p => p.UserId, "test-user-123"));
cut.Find("button").Click();
cut.WaitForAssertion(() => cut.Find(".result").TextContent.Should().Be("expected"));
```

## What Is Excluded From Coverage

Classes decorated with `[ExcludeFromCodeCoverage]` do **not** count against the 85% gate:
- Classes directly wrapping Azure SDK calls
- Classes directly wrapping WebSocket or HTTP I/O (e.g. `NativeEventSubService`)
- EventSub handler classes (not their extracted processor classes)

If you're tempted to add `[ExcludeFromCodeCoverage]` to business logic, factor the logic into a separate testable class instead.
