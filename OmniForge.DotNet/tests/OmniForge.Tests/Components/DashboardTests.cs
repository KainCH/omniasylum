/*
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages;
using System.Security.Claims;
using Xunit;
using CoreCounter = OmniForge.Core.Entities.Counter;

namespace OmniForge.Tests.Components;

#pragma warning disable CS0618 // Type or member is obsolete
public class DashboardTests : TestContext
#pragma warning restore CS0618 // Type or member is obsolete
{
    private readonly Mock<ICounterRepository> _mockCounterRepo;
    private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;

    public DashboardTests()
    {
        _mockCounterRepo = new Mock<ICounterRepository>();
        _mockOverlayNotifier = new Mock<IOverlayNotifier>();

        Services.AddSingleton(_mockCounterRepo.Object);
        Services.AddSingleton(_mockOverlayNotifier.Object);
    }

    [Fact]
    public void Dashboard_Renders_Loading_When_Counter_Is_Null()
    {
        // Arrange
        var authContext = new TestAuthorizationContext();
        authContext.SetAuthorized("TestUser");
        Services.AddFallbackServiceProvider(new FallbackServiceProvider(Services));
        Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(authContext);

        // Act
        var cut = Render<Dashboard>();

        // Assert
        cut.Find("p em").MarkupMatches("<em>Loading counters...</em>");
    }

    [Fact]
    public void Dashboard_Renders_Counters_When_Loaded()
    {
        // Arrange
        var userId = "12345";
        var authContext = new TestAuthorizationContext();
        authContext.SetAuthorized("TestUser");
        authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, userId));
        Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(authContext);

        var counter = new CoreCounter { TwitchUserId = userId, Deaths = 10, Swears = 5 };
        _mockCounterRepo.Setup(r => r.GetCountersAsync(userId)).ReturnsAsync(counter);

        // Act
        var cut = Render<Dashboard>();

        // Assert
        cut.WaitForState(() => cut.FindAll("h2.display-4").Count > 0);
        var counters = cut.FindAll("h2.display-4");
        Assert.Equal("10", counters[0].TextContent);
        Assert.Equal("5", counters[1].TextContent);
    }

    [Fact]
    public void Dashboard_Updates_Deaths_When_Button_Clicked()
    {
        // Arrange
        var userId = "12345";
        var authContext = new TestAuthorizationContext();
        authContext.SetAuthorized("TestUser");
        authContext.SetClaims(new Claim(ClaimTypes.NameIdentifier, userId));
        Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(authContext);

        var counter = new CoreCounter { TwitchUserId = userId, Deaths = 10, Swears = 5 };
        _mockCounterRepo.Setup(r => r.GetCountersAsync(userId)).ReturnsAsync(counter);

        var cut = Render<Dashboard>();
        cut.WaitForState(() => cut.FindAll("h2.display-4").Count > 0);

        // Act
        // Find the + button for Deaths (first card)
        var buttons = cut.FindAll("button.btn-success");
        buttons[0].Click();

        // Assert
        _mockCounterRepo.Verify(r => r.SaveCountersAsync(It.Is<CoreCounter>(c => c.Deaths == 11)), Times.Once);
        _mockOverlayNotifier.Verify(n => n.NotifyCounterUpdateAsync(userId, It.Is<CoreCounter>(c => c.Deaths == 11)), Times.Once);

        var counters = cut.FindAll("h2.display-4");
        Assert.Equal("11", counters[0].TextContent);
    }
}
*/
