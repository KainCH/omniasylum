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
        var user = new User { TwitchUserId = userId, DiscordWebhookUrl = "https://discord.com/api/webhooks/123" };
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

        Assert.Contains(user.DiscordWebhookUrl, cut.Find("input.form-control").GetAttribute("value"));
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
        var urlInput = cut.Find("input.form-control");
        urlInput.Change("https://new-webhook-url");

        var form = cut.Find("form");
        form.Submit();

        // Assert
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.DiscordWebhookUrl == "https://new-webhook-url")), Times.Once);
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
}
