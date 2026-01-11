using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Modals;

public class OverlaySettingsModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
    private readonly Mock<IGameContextRepository> _mockGameContextRepository;
    private readonly Mock<IGameCoreCountersConfigRepository> _mockGameCoreCountersConfigRepository;

    public OverlaySettingsModalTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockOverlayNotifier = new Mock<IOverlayNotifier>();
        _mockGameContextRepository = new Mock<IGameContextRepository>();
        _mockGameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();

        _mockOverlayNotifier
            .Setup(n => n.NotifySettingsUpdateAsync(It.IsAny<string>(), It.IsAny<OverlaySettings>()))
            .Returns(Task.CompletedTask);

        _mockGameContextRepository
            .Setup(r => r.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((GameContext?)null);

        Services.AddSingleton(_mockUserRepository.Object);
        Services.AddSingleton(_mockOverlayNotifier.Object);
        Services.AddSingleton(_mockGameContextRepository.Object);
        Services.AddSingleton(_mockGameCoreCountersConfigRepository.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), false);
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
        var user = new User { TwitchUserId = userId };
        user.OverlaySettings.Position = "top-right";
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        Assert.Contains("Overlay Settings", cut.Markup);
        Assert.Contains("top-right", cut.Find("select.form-select").GetAttribute("value"));
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
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Act
        var positionSelect = cut.Find("select.form-select");
        positionSelect.Change("bottom-left");

        var form = cut.Find("form");
        form.Submit();

        // Assert
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.OverlaySettings.Position == "bottom-left")), Times.Once);
    }

    [Fact]
    public void VisibleCounters_ShouldBeReadOnlyAndReflectCurrentSettings()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        user.OverlaySettings.Counters.Deaths = false;
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Assert
        var deathsCheckbox = cut.Find("#showDeaths");
        Assert.Equal("checkbox", deathsCheckbox.GetAttribute("type"));
        Assert.NotNull(deathsCheckbox.GetAttribute("disabled"));
        Assert.Null(deathsCheckbox.GetAttribute("checked"));

        // Saving the modal should not overwrite counter visibility.
        var form = cut.Find("form");
        form.Submit();
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.OverlaySettings.Counters.Deaths == false)), Times.Once);
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
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.AddAttribute(3, nameof(OverlaySettingsModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Act
        var closeButton = cut.Find("button.btn-close");
        closeButton.Click();

        // Assert
        Assert.True(showChangedInvoked);
    }
}
