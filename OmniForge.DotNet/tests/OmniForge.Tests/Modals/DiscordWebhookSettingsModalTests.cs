using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Modals;

public class DiscordWebhookSettingsModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IDiscordService> _mockDiscordService;

    public DiscordWebhookSettingsModalTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockDiscordService = new Mock<IDiscordService>();

        Services.AddSingleton(_mockUserRepository.Object);
        Services.AddSingleton(_mockDiscordService.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), false);
            b.CloseComponent();
        });

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Modal_ShouldRender_WhenShowIsTrue()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId, DiscordChannelId = "123456789012345678" };
        user.Features.DiscordWebhook = true;
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);
        Assert.Contains("Discord Settings", cut.Markup);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        var channelIdInput = cut.Find("input[placeholder*='123456789012345678']");
        Assert.Contains(user.DiscordChannelId, channelIdInput.GetAttribute("value"));
    }

    [Fact]
    public void SaveSettings_ShouldCallUpdateUser_WhenFormIsSubmitted()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Act
        var channelIdInput = cut.Find("input[placeholder*='123456789012345678']");
        channelIdInput.Change("234567890123456789");

        var form = cut.Find("form");
        form.Submit();

        // Assert
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.DiscordChannelId == "234567890123456789")), Times.Once);
    }

    [Fact]
    public void TestWebhook_ShouldCallDiscordService_WhenClicked()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId, DiscordWebhookUrl = "https://valid-url" };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockDiscordService.Setup(s => s.SendTestNotificationAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Act
        var testButton = cut.FindAll("button").First(b => b.TextContent.Contains("Send Test"));
        testButton.Click();

        // Assert
        _mockDiscordService.Verify(s => s.SendTestNotificationAsync(It.Is<User>(u => u.TwitchUserId == userId)), Times.Once);
    }

    [Fact]
    public void Close_ShouldInvokeShowChanged_WhenClicked()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.AddAttribute(3, nameof(DiscordWebhookSettingsModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("input").Count > 0);

        // Act
        var closeButton = cut.Find("button.btn-close");
        closeButton.Click();

        // Assert
        Assert.True(showChangedInvoked);
    }

    [Fact]
    public void Modal_ShouldShowLoadingSpinner_WhileLoading()
    {
        // Arrange - Setup repository to delay response
        var userId = "test-user-id";
        var tcs = new TaskCompletionSource<User?>();
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).Returns(tcs.Task);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert - Should show loading spinner
        Assert.Contains("spinner-border", cut.Markup);
        Assert.Contains("Loading", cut.Markup);

        // Complete the task
        tcs.SetResult(new User { TwitchUserId = userId });
    }

    [Fact]
    public void Modal_ShouldSwitchTabs_WhenTabClicked()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Assert - Default tab is Notifications
        Assert.Contains("Basic Notification Types", cut.Markup);

        // Switch to Counters tab
        var countersTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Counters"));
        countersTab.Click();

        // Assert - Counters content visible
        Assert.Contains("Milestone Notifications", cut.Markup);
        Assert.Contains("Death Count Milestones", cut.Markup);
        Assert.Contains("Swear Count Milestones", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldDisplayExistingWebhookAlert_WhenUrlExists()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User
        {
            TwitchUserId = userId,
            DiscordWebhookUrl = "https://discord.com/api/webhooks/existing"
        };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Assert
        Assert.Contains("Existing legacy Discord configuration detected", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldDisplayEnabledStatus_WhenWebhookFeatureEnabled()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User
        {
            TwitchUserId = userId,
            DiscordChannelId = "123456789012345678"
        };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Assert
        Assert.Contains("Configured", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldDisplayDisabledStatus_WhenWebhookFeatureDisabled()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Assert
        Assert.Contains("Not configured", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldDisplayDiscordInviteInfo_WhenInviteLinkSet()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User
        {
            TwitchUserId = userId,
            DiscordInviteLink = "https://discord.gg/ABC123"
        };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Assert
        Assert.Contains("Invite Link Active", cut.Markup);
        Assert.Contains("!discord", cut.Markup);
        Assert.Contains("https://discord.gg/ABC123", cut.Markup);
    }

    [Fact]
    public void SaveSettings_ShouldEnableWebhookFeature_WhenUrlIsProvided()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        user.Features.DiscordWebhook = false;
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Act - Set channel ID
        var channelIdInput = cut.Find("input[placeholder*='123456789012345678']");
        channelIdInput.Change("234567890123456789");

        var form = cut.Find("form");
        form.Submit();

        // Assert - Should auto-enable webhook feature
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u =>
            u.DiscordChannelId == "234567890123456789" &&
            u.Features.DiscordWebhook == true
        )), Times.Once);
    }

    [Fact]
    public void SaveSettings_ShouldDisableWebhookFeature_WhenUrlIsEmpty()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User
        {
            TwitchUserId = userId,
            DiscordChannelId = "123456789012345678"
        };
        user.Features.DiscordWebhook = true;
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Act - Clear channel ID
        var channelIdInput = cut.Find("input[placeholder*='123456789012345678']");
        channelIdInput.Change("");

        var form = cut.Find("form");
        form.Submit();

        // Assert - Should auto-disable webhook feature
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u =>
            string.IsNullOrEmpty(u.DiscordChannelId) &&
            string.IsNullOrEmpty(u.DiscordWebhookUrl) &&
            u.Features.DiscordWebhook == false
        )), Times.Once);
    }

    [Fact]
    public void TestWebhook_ShouldShowError_WhenServiceFails()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId, DiscordWebhookUrl = "https://valid-url" };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockDiscordService.Setup(s => s.SendTestNotificationAsync(It.IsAny<User>()))
            .ThrowsAsync(new Exception("Network error"));

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Act
        var testButton = cut.FindAll("button").First(b => b.TextContent.Contains("Send Test"));
        testButton.Click();

        // Assert - Should invoke JS alert with error
        var alerts = JSInterop.Invocations.Where(i => i.Identifier == "alert").ToList();
        Assert.Contains(alerts, a => a.Arguments[0]?.ToString()?.Contains("Failed") == true);
    }

    [Fact]
    public void Modal_ShouldInitializeNestedObjects_WhenNull()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        user.DiscordSettings = null!;
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Assert - Should not throw and render correctly
        Assert.Contains("Discord Settings", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldDisplayChannelNotificationToggle()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        user.DiscordSettings.EnableChannelNotifications = true;
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Assert - Should show Twitch Chat Notifications option
        Assert.Contains("Twitch Chat Notifications", cut.Markup);
        Assert.Contains("Announce milestones and events in your Twitch chat", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldClose_WhenCancelButtonClicked()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.AddAttribute(3, nameof(DiscordWebhookSettingsModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("button").Count > 0);

        // Act
        var cancelButton = cut.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();

        // Assert
        Assert.True(showChangedInvoked);
    }

    [Fact]
    public void SaveSettings_ShouldShowError_WhenRepositoryFails()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>()))
            .ThrowsAsync(new Exception("Database error"));

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Act
        var form = cut.Find("form");
        form.Submit();

        // Assert - Should invoke JS alert with error
        var alerts = JSInterop.Invocations.Where(i => i.Identifier == "alert").ToList();
        Assert.Contains(alerts, a => a.Arguments[0]?.ToString()?.Contains("Failed to save") == true);
    }

    [Fact]
    public void Modal_ShouldDisableTestButton_WhenNoWebhookUrl()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId, DiscordWebhookUrl = "" };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Switch to configuration tab
        var configTab = cut.FindAll(".nav-link").First(e => e.TextContent.Contains("Configuration"));
        configTab.Click();

        // Assert
        var testButton = cut.FindAll("button").First(b => b.TextContent.Contains("Send Test"));
        Assert.True(testButton.HasAttribute("disabled"));
    }

    [Fact]
    public void Modal_ShouldHandleNullUserFromRepository()
    {
        // Arrange
        var userId = "test-user-id";
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync((User?)null);

        var cut = Render(b =>
        {
            b.OpenComponent<DiscordWebhookSettingsModal>(0);
            b.AddAttribute(1, nameof(DiscordWebhookSettingsModal.Show), true);
            b.AddAttribute(2, nameof(DiscordWebhookSettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".nav-link").Count > 0);

        // Assert - Should still render with default user
        Assert.Contains("Discord Settings", cut.Markup);
    }
}
