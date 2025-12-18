using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Web.Components.Pages.Settings;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using System.Collections.Generic;

namespace OmniForge.Tests.Components.Pages.Settings
{
    public class DiscordWebhooksTests : BunitContext
    {
        private readonly Mock<IUserRepository> _mockUserRepo;
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly MockAuthenticationStateProvider _mockAuthenticationStateProvider;

        public DiscordWebhooksTests()
        {
            _mockUserRepo = new Mock<IUserRepository>();
            _mockDiscordService = new Mock<IDiscordService>();
            _mockAuthenticationStateProvider = new MockAuthenticationStateProvider();

            var mockJsRuntime = new Mock<IJSRuntime>();

            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);
            Services.AddAuthorizationCore();

            // Mock IAuthorizationService
            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());
            Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

            Services.AddSingleton<IUserRepository>(_mockUserRepo.Object);
            Services.AddSingleton<IDiscordService>(_mockDiscordService.Object);

            Services.AddSingleton<IJSRuntime>(mockJsRuntime.Object);
            Services.AddOptions();
            Services.Configure<DiscordBotSettings>(_ => { });
        }

        [Fact]
        public void RendersUserNotFound_WhenUserIdClaimMissing()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
            }, "TestAuthType")));

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
            Assert.Contains("User not found", cut.Find(".alert-danger").TextContent);
        }

        [Fact]
        public void RendersDiscordIntegrationSettings_WhenUserIdClaimPresent()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { TwitchUserId = "123", Username = "StreamerOne" });

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll(".nav-tabs").Count > 0);
            Assert.Contains("Message Designer", cut.Markup);

            _mockUserRepo.Verify(x => x.GetUserAsync("123"), Times.Once);
        }
    }
}
