using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Modals;

public class AutoShoutoutSettingsModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;

    public AutoShoutoutSettingsModalTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        Services.AddSingleton<IUserRepository>(_mockUserRepository.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Visibility
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Modal_Hidden_WhenShowIsFalse()
    {
        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), false);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });

        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Modal_Visible_WhenShowIsTrue()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));
        Assert.Contains("Auto-Shoutout Settings", cut.Markup);
        Assert.Contains("Exclude List", cut.Markup);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Exclusion list rendering
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Modal_ShowsNoExclusionsMessage_WhenUserHasEmptyList()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                AutoShoutoutExcludeList = new List<string>()
            });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.Markup.Contains("No exclusions"));
        Assert.Contains("No exclusions", cut.Markup);
    }

    [Fact]
    public void Modal_ShowsExistingExclusions_AsBadges()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                AutoShoutoutExcludeList = new List<string> { "nightbot", "streamelements" }
            });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".badge").Count == 2);
        var badges = cut.FindAll(".badge");
        Assert.Equal(2, badges.Count);
        Assert.Contains(badges, b => b.TextContent.Contains("nightbot"));
        Assert.Contains(badges, b => b.TextContent.Contains("streamelements"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Adding exclusions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Modal_AddExclusion_AppearsAsBadge()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        var input = cut.Find("input.form-control");
        input.Input("moobot");
        cut.FindAll("button.btn-primary").First(b => b.TextContent.Trim() == "Add").Click();

        cut.WaitForState(() => cut.FindAll(".badge").Count > 0);
        Assert.Contains("moobot", cut.Markup);
    }

    [Fact]
    public void Modal_AddExclusion_NormalizesToLowercase()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        cut.Find("input.form-control").Input("Nightbot");
        cut.FindAll("button.btn-primary").First(b => b.TextContent.Trim() == "Add").Click();

        cut.WaitForState(() => cut.FindAll(".badge").Count > 0);
        Assert.Contains("nightbot", cut.Markup);
        Assert.DoesNotContain("Nightbot", cut.Markup);
    }

    [Fact]
    public void Modal_AddDuplicateExclusion_IsIgnored()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        cut.Find("input.form-control").Input("nightbot");
        cut.FindAll("button.btn-primary").First(b => b.TextContent.Trim() == "Add").Click();
        cut.WaitForState(() => cut.FindAll(".badge").Count == 1);

        // Attempt a second add of the same login
        cut.Find("input.form-control").Input("nightbot");
        cut.FindAll("button.btn-primary").First(b => b.TextContent.Trim() == "Add").Click();

        Assert.Single(cut.FindAll(".badge"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Removing exclusions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Modal_RemoveExclusion_RemovesBadge()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                AutoShoutoutExcludeList = new List<string> { "nightbot", "streamelements" }
            });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.FindAll(".badge").Count == 2);

        cut.FindAll(".badge .btn-close").First().Click();

        cut.WaitForState(() => cut.FindAll(".badge").Count == 1);
        Assert.Single(cut.FindAll(".badge"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Modal_Save_CallsSaveUserAsync()
    {
        User? savedUser = null;
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });
        _mockUserRepository
            .Setup(r => r.SaveUserAsync(It.IsAny<User>()))
            .Callback<User>(u => savedUser = u)
            .Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        // Add a login then save
        cut.Find("input.form-control").Input("botuser");
        cut.FindAll("button.btn-primary").First(b => b.TextContent.Trim() == "Add").Click();
        cut.WaitForState(() => cut.FindAll(".badge").Count == 1);

        cut.FindAll("button.btn-primary").Last().Click();

        cut.WaitForState(() => savedUser != null);
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.IsAny<User>()), Times.Once);
        Assert.Contains("botuser", savedUser!.AutoShoutoutExcludeList);
    }

    [Fact]
    public void Modal_Save_ShowsSuccessMessage()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        cut.FindAll("button.btn-primary").Last().Click();

        cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
        Assert.Contains("Saved!", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public void Modal_Save_ShowsError_WhenSaveFails()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>()))
            .ThrowsAsync(new System.Exception("Storage error"));

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        cut.FindAll("button.btn-primary").Last().Click();

        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0);
        Assert.Contains("Failed to save", cut.Find(".alert-danger").TextContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Close / cancel
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Modal_CancelButton_InvokesShowChangedWithFalse()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        bool? captured = null;
        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.AddAttribute(3, nameof(AutoShoutoutSettingsModal.ShowChanged),
                EventCallback.Factory.Create<bool>(new object(), (bool v) => captured = v));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        cut.Find("button.btn-secondary").Click();

        cut.WaitForState(() => captured.HasValue);
        Assert.False(captured);
    }

    [Fact]
    public void Modal_HeaderCloseButton_InvokesShowChangedWithFalse()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User { TwitchUserId = "user123" });

        bool? captured = null;
        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.AddAttribute(3, nameof(AutoShoutoutSettingsModal.ShowChanged),
                EventCallback.Factory.Create<bool>(new object(), (bool v) => captured = v));
            b.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Auto-Shoutout Settings"));

        cut.Find(".modal-header .btn-close").Click();

        cut.WaitForState(() => captured.HasValue);
        Assert.False(captured);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Load behaviour — bug fix verification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Modal_LoadsExcludeListOnce_WhenInitiallyShown()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("user123"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "user123",
                AutoShoutoutExcludeList = new List<string> { "nightbot" }
            });

        var cut = Render(b =>
        {
            b.OpenComponent<AutoShoutoutSettingsModal>(0);
            b.AddAttribute(1, nameof(AutoShoutoutSettingsModal.Show), true);
            b.AddAttribute(2, nameof(AutoShoutoutSettingsModal.UserId), "user123");
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".badge").Count == 1);

        // GetUserAsync should only be called once
        _mockUserRepository.Verify(r => r.GetUserAsync("user123"), Times.Once);
    }
}
