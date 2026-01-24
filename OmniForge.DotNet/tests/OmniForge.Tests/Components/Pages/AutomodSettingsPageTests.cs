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

public class AutomodSettingsPageTests : BunitContext
{
    private readonly Mock<ITwitchApiService> _mockTwitchApiService;
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly TestAuthStateProvider _authProvider;

    public AutomodSettingsPageTests()
    {
        _mockTwitchApiService = new Mock<ITwitchApiService>();
        _mockUserRepository = new Mock<IUserRepository>();
        _authProvider = new TestAuthStateProvider();

        Services.AddSingleton<AuthenticationStateProvider>(_authProvider);
        Services.AddAuthorizationCore();

        var mockAuthService = new Mock<IAuthorizationService>();
        mockAuthService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());
        Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

        Services.AddSingleton<ITwitchApiService>(_mockTwitchApiService.Object);
        Services.AddSingleton<IUserRepository>(_mockUserRepository.Object);
        Services.AddSingleton(NullLogger<AutomodSettings>.Instance);
    }

    [Fact]
    public void ManagedStreamer_AsAdmin_LoadsSettingsForTargetUser()
    {
        // Arrange
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "AdminUser"),
            new Claim("userId", "admin123")
        }, "TestAuthType")));

        _mockUserRepository.Setup(r => r.GetUserAsync("admin123"))
            .ReturnsAsync(new User { TwitchUserId = "admin123", Role = "admin" });

        _mockTwitchApiService.Setup(s => s.GetAutomodSettingsAsync("streamer456"))
            .ReturnsAsync(new AutomodSettingsDto
            {
                OverallLevel = null,
                Aggression = 1,
                Bullying = 1,
                Disability = 1,
                Misogyny = 1,
                RaceEthnicityOrReligion = 1,
                SexBasedTerms = 1,
                SexualitySexOrGender = 1,
                Swearing = 1
            });

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<AutomodSettings>(2);
                builder.AddAttribute(3, "StreamerId", "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.Markup.Contains("AutoMod Settings"));
        cut.WaitForState(() => cut.Markup.Contains("Aggression"));
        _mockTwitchApiService.Verify(s => s.GetAutomodSettingsAsync("streamer456"), Times.Once);
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
                builder.OpenComponent<AutomodSettings>(2);
                builder.AddAttribute(3, "StreamerId", "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("You do not have permission", cut.Find(".alert-danger").TextContent);
        _mockTwitchApiService.Verify(s => s.GetAutomodSettingsAsync(It.IsAny<string>()), Times.Never);
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
