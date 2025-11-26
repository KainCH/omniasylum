using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
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

            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);
            Services.AddAuthorizationCore();

            // Mock IAuthorizationService
            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());
            Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

            Services.AddSingleton<IUserRepository>(_mockUserRepo.Object);
            Services.AddSingleton<IDiscordService>(_mockDiscordService.Object);
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

            // Delay the user loading to simulate loading state
            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>()))
                .Returns(async () => { await Task.Delay(100); return null; });

            // Act
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            Assert.NotNull(cut.Find(".spinner-border"));
        }

        [Fact]
        public void RendersUserNotFound_WhenUserIsNull()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync((User?)null);

            // Act
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
            cut.Find(".alert-danger").MarkupMatches("<div class=\"alert alert-danger\">User not found. Please try logging in again.</div>");
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

            var user = new User {
                TwitchUserId = "123",
                Username = "StreamerOne",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/..."
            };
            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync(user);

            // Act
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll("h5").Count > 0);
            Assert.Contains("Webhook Configuration", cut.Markup);
        }

        [Fact]
        public void SaveWebhook_UpdatesUserAndShowsSuccess()
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
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("input[placeholder*='https://discord.com/api/webhooks']").Count > 0);

            // Act
            var input = cut.Find("input[placeholder*='https://discord.com/api/webhooks']");
            input.Change("https://discord.com/api/webhooks/new-url");

            var saveBtn = cut.Find("button.btn-primary"); // Assuming the first primary button is Save Webhook
            // Better selector: button that follows the input
            // Or find by text content if possible, but BUnit Find matches CSS selectors.
            // Let's look at the razor file to find a unique selector.

            // I'll assume the button is in the same card body.
            // Let's use a more specific selector based on the structure I saw earlier or just find all buttons and pick one.
            // But I should verify the selector.

            // For now, let's assume I can find it by text content using a helper or XPath if supported (BUnit doesn't support XPath directly).
            // I'll use: cut.FindAll("button").First(b => b.TextContent.Contains("Save Webhook")).Click();

            var buttons = cut.FindAll("button");
            var saveWebhookBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Save Webhook"));
            Assert.NotNull(saveWebhookBtn);
            saveWebhookBtn.Click();

            // Assert
            _mockUserRepo.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordWebhookUrl == "https://discord.com/api/webhooks/new-url")), Times.Once);
            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Webhook URL saved successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void TestWebhook_SendsNotification()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User { TwitchUserId = "123", Username = "StreamerOne", DiscordWebhookUrl = "valid-url" };
            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>())).ReturnsAsync(user);
            _mockDiscordService.Setup(x => x.SendTestNotificationAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("button").Count > 0);

            // Act
            var buttons = cut.FindAll("button");
            var testBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Test Webhook"));
            Assert.NotNull(testBtn);
            testBtn.Click();

            // Assert
            _mockDiscordService.Verify(x => x.SendTestNotificationAsync(It.Is<User>(u => u.TwitchUserId == "123")), Times.Once);
            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Test notification sent!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void UpdateThresholds_UpdatesUserAndSaves()
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
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("input.form-control-sm").Count > 0);

            // Act - Update Deaths Thresholds
            var inputs = cut.FindAll("input.form-control-sm");
            var deathsInput = inputs[0];

            deathsInput.Change("100, 200, 300");

            // Save Settings
            var buttons = cut.FindAll("button");
            var saveSettingsBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Save Notification Settings"));
            Assert.NotNull(saveSettingsBtn);
            saveSettingsBtn.Click();

            // Assert
            _mockUserRepo.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.MilestoneThresholds.Deaths.SequenceEqual(new List<int> { 100, 200, 300 })
            )), Times.Once);

            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Notification settings saved successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void SaveInvite_UpdatesUserAndShowsSuccess()
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
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("input[placeholder*='https://discord.gg']").Count > 0);

            // Act
            var input = cut.Find("input[placeholder*='https://discord.gg']");
            input.Change("https://discord.gg/new-invite");

            var buttons = cut.FindAll("button");
            var saveInviteBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Save Invite"));
            Assert.NotNull(saveInviteBtn);
            saveInviteBtn.Click();

            // Assert
            _mockUserRepo.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordInviteLink == "https://discord.gg/new-invite")), Times.Once);
            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Invite link saved successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void SaveWebhook_ShowsError_WhenException()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User { TwitchUserId = "123", Username = "StreamerOne" };
            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>())).ReturnsAsync(user);
            _mockUserRepo.Setup(x => x.SaveUserAsync(It.IsAny<User>())).ThrowsAsync(new System.Exception("Database error"));

            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("button").Count > 0);

            // Act
            var buttons = cut.FindAll("button");
            var saveWebhookBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Save Webhook"));
            Assert.NotNull(saveWebhookBtn);
            saveWebhookBtn.Click();

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
            Assert.Contains("Error saving webhook: Database error", cut.Find(".alert-danger").TextContent);
        }

        [Fact]
        public void TestWebhook_ShowsError_WhenException()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var user = new User { TwitchUserId = "123", Username = "StreamerOne", DiscordWebhookUrl = "valid" };
            _mockUserRepo.Setup(x => x.GetUserAsync(It.IsAny<string>())).ReturnsAsync(user);
            _mockDiscordService.Setup(x => x.SendTestNotificationAsync(It.IsAny<User>())).ThrowsAsync(new System.Exception("Network error"));

            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("button").Count > 0);

            // Act
            var buttons = cut.FindAll("button");
            var testBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Test Webhook"));
            Assert.NotNull(testBtn);
            testBtn.Click();

            // Assert
            cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
            Assert.Contains("Error sending test notification: Network error", cut.Find(".alert-danger").TextContent);
        }

        [Fact]
        public void UpdateThresholds_Swears_UpdatesUser()
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
                    builder.OpenComponent<DiscordWebhooks>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("input.form-control-sm").Count > 0);

            // Act
            var inputs = cut.FindAll("input.form-control-sm");
            // Assuming the second input is for swears, or find by binding/id if possible.
            // Based on razor, it's likely the second one if deaths is first.
            // But let's be safer. The razor uses @onchange="e => UpdateThresholds(e, 'swears')".
            // I can't easily select by event handler.
            // I'll assume order: Deaths, Swears.
            var swearsInput = inputs[1];

            swearsInput.Change("5, 10, 15");

            var buttons = cut.FindAll("button");
            var saveSettingsBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Save Notification Settings"));
            Assert.NotNull(saveSettingsBtn);
            saveSettingsBtn.Click();

            // Assert
            _mockUserRepo.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.MilestoneThresholds.Swears.SequenceEqual(new List<int> { 5, 10, 15 })
            )), Times.Once);
        }
    }
}
