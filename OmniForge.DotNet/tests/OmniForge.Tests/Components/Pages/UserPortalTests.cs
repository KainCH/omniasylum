using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using Moq;
using System.Collections.Generic;

namespace OmniForge.Tests.Components.Pages
{
    public class UserPortalTests : BunitContext
    {
        private readonly MockAuthenticationStateProvider _mockAuthenticationStateProvider;
        private readonly Mock<IUserRepository> _mockUserRepository;

        public UserPortalTests()
        {
            _mockAuthenticationStateProvider = new MockAuthenticationStateProvider();
            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);

            _mockUserRepository = new Mock<IUserRepository>();
            Services.AddSingleton(_mockUserRepository.Object);

            Services.AddAuthorizationCore();

            // Mock IAuthorizationService to always allow
            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());

            // Replace the service registered by AddAuthorizationCore
            Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);
        }

        [Fact]
        public void RendersWelcomeMessage_WithUserName()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "user123")
            }, "TestAuthType")));

            _mockUserRepository.Setup(r => r.GetUserAsync("user123")).ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                DisplayName = "StreamerOne",
                Username = "streamerone",
                ManagedStreamers = new List<string>()
            });

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<UserPortal>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert - wait for data to load
            cut.WaitForState(() => cut.Markup.Contains("Welcome"));
            Assert.Contains("Welcome, StreamerOne!", cut.Markup);
        }

        [Fact]
        public void RendersNavigationCards()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "user123"),
                new Claim(ClaimTypes.Role, "admin")
            }, "TestAuthType")));

            _mockUserRepository.Setup(r => r.GetUserAsync("user123")).ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                DisplayName = "StreamerOne",
                Username = "streamerone",
                ManagedStreamers = new List<string>()
            });

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<UserPortal>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert - wait for data to load
            cut.WaitForState(() => cut.Markup.Contains("dashboard"));

            var cards = cut.FindAll(".card");
            Assert.True(cards.Count >= 5);

            // Check card links (using contextual links pattern)
            Assert.Contains("dashboard", cut.Markup);
            Assert.Contains("template-designer", cut.Markup);
            Assert.Contains("settings/discord", cut.Markup);
            Assert.Contains("settings/alert-effects", cut.Markup);
        }

        [Fact]
        public void RendersModTeamCard_InOwnContext()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "user123")
            }, "TestAuthType")));

            _mockUserRepository.Setup(r => r.GetUserAsync("user123")).ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                DisplayName = "StreamerOne",
                Username = "streamerone",
                ManagedStreamers = new List<string>()
            });

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<UserPortal>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert - Mod Team card should be visible
            cut.WaitForState(() => cut.Markup.Contains("Mod Team"));
            Assert.Contains("Mod Team", cut.Markup);
            Assert.Contains("Manage Team", cut.Markup);
        }

        [Fact]
        public void RendersContextSwitcher_WhenUserHasManagedStreamers()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "ModeratorOne"),
                new Claim("userId", "mod123")
            }, "TestAuthType")));

            _mockUserRepository.Setup(r => r.GetUserAsync("mod123")).ReturnsAsync(new User
            {
                TwitchUserId = "mod123",
                DisplayName = "ModeratorOne",
                Username = "moderatorone",
                ManagedStreamers = new List<string> { "streamer456" }
            });

            _mockUserRepository.Setup(r => r.GetUserAsync("streamer456")).ReturnsAsync(new User
            {
                TwitchUserId = "streamer456",
                DisplayName = "StreamerTwo",
                Username = "streamertwo",
                ManagedStreamers = new List<string>()
            });

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<UserPortal>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert - context switcher should be visible
            cut.WaitForState(() => cut.Markup.Contains("Currently viewing as"));
            Assert.Contains("Currently viewing as", cut.Markup);
            Assert.Contains("StreamerTwo's Forge", cut.Markup);
        }

        [Fact]
        public void Admin_LoadsAllActiveStreamers_ForContextSwitcher()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "AdminUser"),
                new Claim("userId", "admin1"),
                new Claim(ClaimTypes.Role, "admin")
            }, "TestAuthType")));

            _mockUserRepository.Setup(r => r.GetUserAsync("admin1")).ReturnsAsync(new User
            {
                TwitchUserId = "admin1",
                DisplayName = "AdminUser",
                Username = "adminuser",
                Role = "admin",
                ManagedStreamers = new List<string>()
            });

            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(new List<User>
            {
                new User { TwitchUserId = "s1", DisplayName = "StreamerOne", Username = "streamerone", Role = "streamer", IsActive = true },
                new User { TwitchUserId = "s2", DisplayName = "StreamerTwo", Username = "streamertwo", Role = "streamer", IsActive = true },
                new User { TwitchUserId = "disabled", DisplayName = "DisabledUser", Username = "disabled", Role = "streamer", IsActive = false },
                new User { TwitchUserId = "admin2", DisplayName = "OtherAdmin", Username = "otheradmin", Role = "admin", IsActive = true }
            });

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<UserPortal>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.Markup.Contains("Currently viewing as"));
            Assert.Contains("StreamerOne's Forge", cut.Markup);
            Assert.Contains("StreamerTwo's Forge", cut.Markup);
            Assert.DoesNotContain("DisabledUser's Forge", cut.Markup);
            Assert.DoesNotContain("OtherAdmin's Forge", cut.Markup);

            _mockUserRepository.Verify(r => r.GetAllUsersAsync(), Times.Once);
        }
    }
}
