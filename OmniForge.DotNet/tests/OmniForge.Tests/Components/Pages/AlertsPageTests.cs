using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Components.Pages;

public class AlertsPageTests : BunitContext
{
    private readonly Mock<IAlertRepository> _mockAlertRepository;
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly TestAuthStateProvider _authProvider;

    public AlertsPageTests()
    {
        _mockAlertRepository = new Mock<IAlertRepository>();
        _mockUserRepository = new Mock<IUserRepository>();
        _authProvider = new TestAuthStateProvider();

        Services.AddSingleton<AuthenticationStateProvider>(_authProvider);
        Services.AddAuthorizationCore();

        var mockAuthService = new Mock<IAuthorizationService>();
        mockAuthService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());
        Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

        Services.AddSingleton<IAlertRepository>(_mockAlertRepository.Object);
        Services.AddSingleton<IUserRepository>(_mockUserRepository.Object);
        Services.AddSingleton(NullLogger<Alerts>.Instance);
    }

    [Fact]
    public void ManagedStreamer_AsModerator_LoadsAlertsForTargetUser()
    {
        // Arrange
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "ModUser"),
            new Claim(ClaimTypes.NameIdentifier, "mod123")
        }, "TestAuthType")));

        _mockUserRepository.Setup(r => r.GetUserAsync("mod123"))
            .ReturnsAsync(new User { TwitchUserId = "mod123", Role = "streamer", ManagedStreamers = new List<string> { "streamer456" } });

        _mockUserRepository.Setup(r => r.GetUserAsync("streamer456"))
            .ReturnsAsync(new User { TwitchUserId = "streamer456" });

        _mockAlertRepository.Setup(r => r.GetAlertsAsync("streamer456"))
            .ReturnsAsync(new List<Alert>
            {
                new Alert { Name = "Test", Type = "follow", VisualCue = "sparkle", IsEnabled = true }
            });

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<Alerts>(2);
                builder.AddAttribute(3, "StreamerId", "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.Markup.Contains("Manage Alerts"));
        _mockAlertRepository.Verify(r => r.GetAlertsAsync("streamer456"), Times.AtLeastOnce);
    }

    [Fact]
    public void ManagedStreamer_WhenUnauthorized_ShowsAccessDenied()
    {
        // Arrange
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "ModUser"),
            new Claim(ClaimTypes.NameIdentifier, "mod123")
        }, "TestAuthType")));

        _mockUserRepository.Setup(r => r.GetUserAsync("mod123"))
            .ReturnsAsync(new User { TwitchUserId = "mod123", Role = "streamer", ManagedStreamers = new List<string>() });

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<Alerts>(2);
                builder.AddAttribute(3, "StreamerId", "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("You do not have permission", cut.Find(".alert-danger").TextContent);
        _mockAlertRepository.Verify(r => r.GetAlertsAsync(It.IsAny<string>()), Times.Never);
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
