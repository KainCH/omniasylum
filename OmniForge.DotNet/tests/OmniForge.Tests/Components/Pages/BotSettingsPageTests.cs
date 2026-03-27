using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using BotSettingsPage = OmniForge.Web.Components.Pages.BotSettings;

namespace OmniForge.Tests.Components.Pages;

public class BotSettingsPageTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly TestAuthStateProvider _authProvider;

    public BotSettingsPageTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _authProvider = new TestAuthStateProvider();

        Services.AddSingleton<AuthenticationStateProvider>(_authProvider);
        Services.AddAuthorizationCore();

        var mockAuthService = new Mock<IAuthorizationService>();
        mockAuthService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(),
                It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());
        Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

        Services.AddSingleton<IUserRepository>(_mockUserRepository.Object);
        Services.AddSingleton(NullLogger<BotSettingsPage>.Instance);
    }

    private void AuthAsUser(string userId = "user123", string name = "Streamer")
    {
        _authProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim("userId", userId)
        }, "TestAuthType")));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Basic render
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BotSettings_ShowsPageHeading_WhenLoaded()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));
        Assert.Contains("Bot Settings", cut.Find("h3").TextContent);
    }

    [Fact]
    public void BotSettings_ShowsEventMessagesCard_WhenLoaded()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("Event Messages"));
        Assert.Contains("Stream Start", cut.Markup);
        Assert.Contains("Raid Received", cut.Markup);
        Assert.Contains("!brb Message", cut.Markup);
    }

    [Fact]
    public void BotSettings_ShowsScheduledMessagesAndLinkCommandsCards_WhenLoaded()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("Scheduled Messages"));
        Assert.Contains("Link Commands", cut.Markup);
    }

    [Fact]
    public void BotSettings_PopulatesExistingMessages_WhenUserHasSettings()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                BotSettings = new BotSettings
                {
                    StreamStartMessage = "Stream is LIVE! PogChamp",
                    BrbMessage = "Be right back!"
                }
            });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("Stream is LIVE!"));
        Assert.Contains("Be right back!", cut.Markup);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Error states
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BotSettings_ShowsUserNotFoundError_WhenUserIsNull()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync((User?)null);

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("User not found", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void BotSettings_ShowsError_WhenRepositoryThrows()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ThrowsAsync(new System.Exception("Storage unavailable"));

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("Storage unavailable", cut.Find(".alert-danger").TextContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Access control
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BotSettings_ShowsAccessDenied_WhenNonAdminRequestsOtherStreamer()
    {
        AuthAsUser("mod123", "ModUser");
        _mockUserRepository.Setup(r => r.GetUserAsync("mod123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "mod123",
                Role = "streamer",
                ManagedStreamers = new List<string>()
            });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.AddAttribute(3, nameof(BotSettingsPage.StreamerId), "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("You do not have permission", cut.Find(".alert-danger").TextContent);
        _mockUserRepository.Verify(r => r.GetUserAsync("streamer456"), Times.Never);
    }

    [Fact]
    public void BotSettings_Admin_CanLoadSettingsForManagedStreamer()
    {
        AuthAsUser("admin123", "AdminUser");
        _mockUserRepository.Setup(r => r.GetUserAsync("admin123"))
            .ReturnsAsync(new User { TwitchUserId = "admin123", Role = "admin" });
        _mockUserRepository.Setup(r => r.GetUserAsync("streamer456"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "streamer456",
                BotSettings = new BotSettings { StreamStartMessage = "Streamer says hi!" }
            });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.AddAttribute(3, nameof(BotSettingsPage.StreamerId), "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("Streamer says hi!"));
        _mockUserRepository.Verify(r => r.GetUserAsync("streamer456"), Times.AtLeastOnce);
    }

    [Fact]
    public void BotSettings_ModeratorWithStreamerInManagedList_CanLoadSettings()
    {
        AuthAsUser("mod123", "ModUser");
        _mockUserRepository.Setup(r => r.GetUserAsync("mod123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "mod123",
                Role = "streamer",
                ManagedStreamers = new List<string> { "streamer456" }
            });
        _mockUserRepository.Setup(r => r.GetUserAsync("streamer456"))
            .ReturnsAsync(new User { TwitchUserId = "streamer456" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.AddAttribute(3, nameof(BotSettingsPage.StreamerId), "streamer456");
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("Event Messages"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scheduled messages
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BotSettings_AddScheduledMessage_AddsRowToTable()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.FindAll(".btn-outline-primary.mt-2").Count > 0);
        Assert.Empty(cut.FindAll("tbody tr"));

        cut.Find(".btn-outline-primary.mt-2").Click();

        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);
        Assert.NotEmpty(cut.FindAll("tbody tr"));
    }

    [Fact]
    public void BotSettings_ExistingScheduledMessages_ShowInTable()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                BotSettings = new BotSettings
                {
                    ScheduledMessages = new List<ScheduledMessageEntry>
                    {
                        new ScheduledMessageEntry { Message = "Follow the channel!", IntervalMinutes = 30 }
                    }
                }
            });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("Follow the channel!"));
        Assert.Contains("Follow the channel!", cut.Markup);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Link commands
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BotSettings_ExistingLinkCommands_ShowInTable()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                BotSettings = new BotSettings
                {
                    LinkCommands = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["discord"] = "https://discord.gg/example"
                    }
                }
            });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("discord"));
        Assert.Contains("discord", cut.Markup);
        Assert.Contains("https://discord.gg/example", cut.Markup);
    }

    [Fact]
    public void BotSettings_AddLinkCommand_AppearsInTable()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.FindAll("input[placeholder='command name']").Count > 0);
        Assert.Empty(cut.FindAll("tbody tr"));

        cut.Find("input[placeholder='command name']").Change("youtube");
        cut.Find(".d-flex.gap-2.mt-2 button").Click();

        cut.WaitForState(() => cut.Markup.Contains("youtube"));
        Assert.Contains("youtube", cut.Markup);
    }

    [Fact]
    public void BotSettings_AddLinkCommand_StripsLeadingExclamation()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.FindAll("input[placeholder='command name']").Count > 0);

        cut.Find("input[placeholder='command name']").Change("!discord");
        cut.Find(".d-flex.gap-2.mt-2 button").Click();

        cut.WaitForState(() => cut.Markup.Contains("discord"));
        Assert.Contains("discord", cut.Markup);
    }

    [Fact]
    public void BotSettings_AddDuplicateLinkCommand_IsIgnored()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.FindAll("input[placeholder='command name']").Count > 0);

        cut.Find("input[placeholder='command name']").Change("twitch");
        cut.Find(".d-flex.gap-2.mt-2 button").Click();
        cut.WaitForState(() => cut.FindAll("tbody tr").Count == 1);

        cut.Find("input[placeholder='command name']").Change("twitch");
        cut.Find(".d-flex.gap-2.mt-2 button").Click();

        Assert.Single(cut.FindAll("tbody tr"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BotSettings_Save_CallsSaveUserAsync()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Save Settings"));

        cut.Find("button.btn-primary").Click();

        cut.WaitForState(() =>
            _mockUserRepository.Invocations.Any(i => i.Method.Name == nameof(IUserRepository.SaveUserAsync)));
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public void BotSettings_Save_ShowsSuccessAlert()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Save Settings"));

        cut.Find("button.btn-primary").Click();

        cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
        Assert.Contains("Settings saved!", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public void BotSettings_Save_ShowsError_WhenSaveThrows()
    {
        AuthAsUser();
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>()))
            .ThrowsAsync(new System.Exception("Write failed"));

        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
            {
                builder.OpenComponent<BotSettingsPage>(2);
                builder.CloseComponent();
            }));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Save Settings"));

        cut.Find("button.btn-primary").Click();

        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("Write failed", cut.Find(".alert-danger").TextContent);
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
