using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages.Settings;
using OmniForge.Web.Components.Settings;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Components.Pages.Settings;

public class SeriesManagerPageTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly TestAuthStateProvider _authProvider;

    public SeriesManagerPageTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _authProvider = new TestAuthStateProvider();

        Services.AddSingleton<AuthenticationStateProvider>(_authProvider);
        Services.AddAuthorizationCore();

        var mockAuthService = new Mock<IAuthorizationService>();
        mockAuthService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());
        Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

        Services.AddSingleton<IUserRepository>(_mockUserRepository.Object);

        ComponentFactories.AddStub<SeriesSaveManager>();
    }

    [Fact]
    public void ManagedStreamer_AsModerator_PassesTargetUserIdToSeriesSaveManager()
    {
        // Arrange
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "ModUser"),
            new Claim("userId", "mod123")
        }, "TestAuthType")));

        _mockUserRepository.Setup(r => r.GetUserAsync("mod123"))
            .ReturnsAsync(new User { TwitchUserId = "mod123", Role = "streamer", ManagedStreamers = new List<string> { "streamer456" } });

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<SeriesManager>(2);
                builder.AddAttribute(3, "StreamerId", "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".spinner-border").Count == 0);
        var stub = cut.FindComponent<Bunit.TestDoubles.Stub<SeriesSaveManager>>();
        Assert.Equal("streamer456", stub.Instance.Parameters.Get(p => p.UserId));
    }

    [Fact]
    public void ManagedStreamer_WhenUnauthorized_ShowsAccessDenied()
    {
        // Arrange
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "ModUser"),
            new Claim("userId", "mod123")
        }, "TestAuthType")));

        _mockUserRepository.Setup(r => r.GetUserAsync("mod123"))
            .ReturnsAsync(new User { TwitchUserId = "mod123", Role = "streamer", ManagedStreamers = new List<string>() });

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<SeriesManager>(2);
                builder.AddAttribute(3, "StreamerId", "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("You do not have permission", cut.Find(".alert-danger").TextContent);
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
