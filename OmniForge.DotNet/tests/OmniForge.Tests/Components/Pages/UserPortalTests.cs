using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
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

        public UserPortalTests()
        {
            _mockAuthenticationStateProvider = new MockAuthenticationStateProvider();
            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);

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
                new Claim(ClaimTypes.Name, "StreamerOne")
            }, "TestAuthType")));

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
            cut.Find("h1").MarkupMatches("<h1 class=\"display-4\">Welcome, StreamerOne!</h1>");
        }

        [Fact]
        public void RendersNavigationCards()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne")
            }, "TestAuthType")));

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
            var cards = cut.FindAll(".card");
            Assert.True(cards.Count >= 5);

            // Check specific links
            Assert.NotNull(cut.Find("a[href='dashboard']"));
            Assert.NotNull(cut.Find("a[href='template-designer']"));
            Assert.NotNull(cut.Find("a[href='settings/discord']"));
            Assert.NotNull(cut.Find("a[href='settings/alert-effects']"));
        }
    }
}
