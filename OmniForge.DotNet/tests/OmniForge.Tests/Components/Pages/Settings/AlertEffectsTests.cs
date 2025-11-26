using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages.Settings;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace OmniForge.Tests.Components.Pages.Settings
{
    public class AlertEffectsTests : BunitContext
    {
        private readonly Mock<IUserRepository> _mockUserRepo;
        private readonly MockAuthenticationStateProvider _mockAuthenticationStateProvider;

        public AlertEffectsTests()
        {
            _mockUserRepo = new Mock<IUserRepository>();
            _mockAuthenticationStateProvider = new MockAuthenticationStateProvider();

            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);
            Services.AddAuthorizationCore();

            // Mock IAuthorizationService
            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());
            Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

            Services.AddSingleton<IUserRepository>(_mockUserRepo.Object);
        }

        [Fact]
        public void RendersLoadingState_Initially()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>()))
                .Returns(async () => { await Task.Delay(100); return null; });

            // Act
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<AlertEffects>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            Assert.NotNull(cut.Find(".spinner-border"));
        }

        [Fact]
        public void RendersForm_WhenUserIsLoaded()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User { TwitchUserId = "123", Username = "StreamerOne" };
            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>())).ReturnsAsync(user);

            // Act
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<AlertEffects>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll("h5").Count > 0);
            Assert.Contains("Visual & Audio Settings", cut.Markup);
        }

        [Fact]
        public void SaveSettings_UpdatesUserAndShowsSuccess()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User { TwitchUserId = "123", Username = "StreamerOne" };
            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>())).ReturnsAsync(user);
            _mockUserRepo.Setup(x => x.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<AlertEffects>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Act
            // Toggle animation enabled
            var animCheckbox = cut.Find("#animEnabled");
            animCheckbox.Change(false);

            // Save - button is warning color
            var saveBtn = cut.WaitForElement("button.btn-warning");
            saveBtn.Click();

            // Assert
            _mockUserRepo.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.OverlaySettings.Animations.Enabled == false)), Times.Once);
            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0, System.TimeSpan.FromSeconds(5));
            Assert.Contains("Alert effects saved successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void AlertEffects_UserNotFound_ShowsError()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            _mockUserRepo.Setup(x => x.GetUserAsync("123")).ReturnsAsync((User?)null);

            // Act
            var cut = Render(builder => {
                builder.OpenComponent<AlertEffects>(0);
                builder.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
            Assert.Contains("User not found", cut.Find(".alert-danger").TextContent);
        }

        [Fact]
        public void AlertEffects_LoadError_ShowsErrorMessage()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            _mockUserRepo.Setup(x => x.GetUserAsync("123")).ThrowsAsync(new Exception("Database error"));

            // Act
            var cut = Render(builder => {
                builder.OpenComponent<AlertEffects>(0);
                builder.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
            Assert.Contains("Error loading user data: Database error", cut.Find(".alert-danger").TextContent);
        }

        [Fact]
        public void AlertEffects_SaveError_ShowsErrorMessage()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User { TwitchUserId = "123", Username = "StreamerOne" };
            _mockUserRepo.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockUserRepo.Setup(x => x.SaveUserAsync(It.IsAny<User>())).ThrowsAsync(new Exception("Save failed"));

            var cut = Render(builder => {
                builder.OpenComponent<AlertEffects>(0);
                builder.CloseComponent();
            });

            // Act
            cut.WaitForElement("button.btn-warning").Click();

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
            Assert.Contains("Error saving settings: Save failed", cut.Find(".alert-danger").TextContent);
        }

        [Fact]
        public void AlertEffects_ResetDefaults_ResetsAndShowsMessage()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User
            {
                TwitchUserId = "123",
                OverlaySettings = new OverlaySettings
                {
                    Animations = new OverlayAnimations { Enabled = false, Volume = 50 }
                }
            };
            _mockUserRepo.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            var cut = Render(builder => {
                builder.OpenComponent<AlertEffects>(0);
                builder.CloseComponent();
            });

            // Act
            cut.WaitForElement("button.btn-outline-secondary").Click();

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-info").Count > 0);
            Assert.Contains("Settings reset to defaults", cut.Find(".alert-info").TextContent);

            // Verify defaults (Enabled should be true by default in new OverlayAnimations)
            Assert.True(user.OverlaySettings.Animations.Enabled);
        }

        [Fact]
        public void AlertEffects_DetailedSettings_BindCorrectly()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User { TwitchUserId = "123" };
            _mockUserRepo.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockUserRepo.Setup(x => x.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            var cut = Render(builder => {
                builder.OpenComponent<AlertEffects>(0);
                builder.CloseComponent();
            });

            // Act
            cut.WaitForElement("#soundEnabled").Change(true);
            cut.Find("#particles").Change(true);
            cut.Find("#screenEffects").Change(true);

            // Save
            cut.Find("button.btn-warning").Click();

            // Assert
            _mockUserRepo.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.OverlaySettings.Animations.EnableSound == true &&
                u.OverlaySettings.Animations.EnableParticles == true &&
                u.OverlaySettings.Animations.EnableScreenEffects == true
            )), Times.Once);
        }    }
}

