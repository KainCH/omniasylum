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
    public void Home_ShouldRenderAuthCard_WhenAuthenticated()
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

        // Assert - Should contain portal link when authenticated
        Assert.Contains("auth-card", cut.Markup);
    }

    [Fact]
    public void Home_ShouldRenderPortalLink_WhenAuthenticated()
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
        var portalLink = cut.Find("a.twitch-login-btn");
        Assert.Contains("Go to User Portal", portalLink.TextContent);
    }

    [Fact]
    public void Home_ShouldShowWelcomeMessage_WhenAuthenticated()
    {
        // Arrange
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
        var subtitle = cut.Find(".auth-subtitle");
        Assert.Contains("Welcome back, TestUser", subtitle.TextContent);
    }

    [Fact]
    public void Home_ShouldShowLogoutButton_WhenAuthenticated()
    {
        // Arrange
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
        var logoutButton = cut.Find("a.btn-outline-danger");
        Assert.Contains("Logout", logoutButton.TextContent);
        Assert.Contains("auth/logout", logoutButton.GetAttribute("href"));
    }

    [Fact]
    public void Home_ShouldShowPageTitle_WhenAuthenticated()
    {
        // Arrange
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
        var header = cut.Find(".auth-header h1");
        Assert.Contains("OmniForgeStream", header.TextContent);
    }

    [Fact]
    public void Home_ShouldNotShowPrivacyNote_WhenAuthenticated()
    {
        // Arrange
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
        var privacyNotes = cut.FindAll(".privacy-note");
        Assert.Empty(privacyNotes);
    }
}
