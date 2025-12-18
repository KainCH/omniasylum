using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Web.Components.Pages;
using Xunit;

namespace OmniForge.Tests.Components.Pages;

public class HomeTests : BunitContext
{
    private readonly Mock<IAuthorizationService> _mockAuthorizationService;

    public HomeTests()
    {
        _mockAuthorizationService = new Mock<IAuthorizationService>();

        Services.AddAuthorizationCore();
        Services.AddSingleton(_mockAuthorizationService.Object);

        // Default authorization setup
        _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());

        _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());
    }

    [Fact]
    public void Home_ShouldRedirectToPortal_WhenAuthenticated()
    {
        // Arrange - Create authenticated user
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "TestUser"),
            new Claim(ClaimTypes.NameIdentifier, "12345")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var authState = Task.FromResult(new AuthenticationState(user));

        var authStateProvider = new Mock<AuthenticationStateProvider>();
        authStateProvider.Setup(x => x.GetAuthenticationStateAsync()).Returns(authState);
        Services.AddSingleton(authStateProvider.Object);
        var nav = Services.GetRequiredService<NavigationManager>();

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<Home>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForAssertion(() => Assert.EndsWith("/portal", nav.Uri));
    }

    [Fact]
    public void Home_ShouldRedirectToLogin_WhenNotAuthenticated()
    {
        // Arrange - unauthenticated user
        var identity = new ClaimsIdentity();
        var user = new ClaimsPrincipal(identity);
        var authState = Task.FromResult(new AuthenticationState(user));

        var authStateProvider = new Mock<AuthenticationStateProvider>();
        authStateProvider.Setup(x => x.GetAuthenticationStateAsync()).Returns(authState);
        Services.AddSingleton(authStateProvider.Object);
        var nav = Services.GetRequiredService<NavigationManager>();

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<Home>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForAssertion(() => Assert.EndsWith("/auth/twitch", nav.Uri));
    }
}
