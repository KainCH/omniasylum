using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Web.Components.Layout;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Components.Layout;

public class NavMenuTests : BunitContext
{
    private readonly TestAuthStateProvider _authProvider;

    public NavMenuTests()
    {
        _authProvider = new TestAuthStateProvider();

        Services.AddSingleton<AuthenticationStateProvider>(_authProvider);
        Services.AddAuthorizationCore();

        var mockAuthService = new Mock<IAuthorizationService>();
        mockAuthService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());
        Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);
    }

    [Fact]
    public void WhenInManagedContext_RewritesUserLinksWithManagePrefix()
    {
        // Arrange
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin123"),
            new Claim(ClaimTypes.Role, "admin")
        }, "TestAuthType")));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/manage/streamer456/dashboard");

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<NavMenu>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.Markup.Contains("manage/streamer456/dashboard"));
        Assert.Contains("manage/streamer456/alerts", cut.Markup);
        Assert.Contains("manage/streamer456/automod", cut.Markup);
        Assert.Contains("manage/streamer456/settings/series", cut.Markup);
        Assert.Contains("manage/streamer456/settings/games", cut.Markup);
    }

    [Fact]
    public void WhenNotInManagedContext_UsesNormalUserLinks()
    {
        // Arrange
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user123")
        }, "TestAuthType")));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/dashboard");

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<NavMenu>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.Markup.Contains("href=\"dashboard\""));
        Assert.DoesNotContain("manage/", cut.Markup);
    }

    private sealed class TestAuthStateProvider : AuthenticationStateProvider
    {
        private AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity()));

        public void SetUser(ClaimsPrincipal user)
        {
            _state = new AuthenticationState(user);
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }
}
