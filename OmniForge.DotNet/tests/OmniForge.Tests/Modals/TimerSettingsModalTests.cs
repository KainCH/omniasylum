using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Modals;

public class TimerSettingsModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;

    public TimerSettingsModalTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockOverlayNotifier = new Mock<IOverlayNotifier>();

        Services.AddSingleton(_mockUserRepository.Object);
        Services.AddSingleton(_mockOverlayNotifier.Object);

        _mockOverlayNotifier
            .Setup(n => n.NotifySettingsUpdateAsync(It.IsAny<string>(), It.IsAny<OverlaySettings>()))
            .Returns(Task.CompletedTask);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        var cut = Render(b =>
        {
            b.OpenComponent<TimerSettingsModal>(0);
            b.AddAttribute(1, nameof(TimerSettingsModal.Show), false);
            b.AddAttribute(2, nameof(TimerSettingsModal.UserId), "user1");
            b.CloseComponent();
        });

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void Modal_ShouldLoadExistingTimerSettings_WhenShown()
    {
        var user = new User
        {
            TwitchUserId = "user1",
            OverlaySettings = new OverlaySettings
            {
                TimerDurationMinutes = 12,
                TimerTextColor = "#00ff00",
                TimerManualRunning = true
            }
        };

        _mockUserRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<TimerSettingsModal>(0);
            b.AddAttribute(1, nameof(TimerSettingsModal.Show), true);
            b.AddAttribute(2, nameof(TimerSettingsModal.UserId), "user1");
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".modal-title").Count == 1);

        var minutesInput = cut.Find("input.form-control");
        Assert.Equal("12", minutesInput.GetAttribute("value"));

        var colorHexInput = cut.FindAll("input.form-control").Last();
        Assert.Equal("#00ff00", colorHexInput.GetAttribute("value"));

        var runningToggle = cut.Find("#timerRunningToggle");
        Assert.NotNull(runningToggle.GetAttribute("checked"));
    }

    [Fact]
    public void Save_ShouldPersistTimerSettings_AndNotifyOverlay()
    {
        var user = new User
        {
            TwitchUserId = "user1",
            OverlaySettings = new OverlaySettings
            {
                TimerEnabled = false,
                TimerDurationMinutes = 0,
                TimerTextColor = null,
                TimerManualRunning = false,
                TimerManualStartUtc = null
            }
        };

        _mockUserRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<TimerSettingsModal>(0);
            b.AddAttribute(1, nameof(TimerSettingsModal.Show), true);
            b.AddAttribute(2, nameof(TimerSettingsModal.UserId), "user1");
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".modal-title").Count == 1);

        // Set duration minutes
        var minutesInput = cut.Find("input.form-control");
        minutesInput.Change("15");

        // Set a specific timer color
        var colorHexInput = cut.FindAll("input.form-control").Last();
        colorHexInput.Change("#112233");

        // Enable running (forces timer enabled and sets start time)
        cut.Find("#timerRunningToggle").Change(true);

        // Click Save
        cut.Find("button.btn.btn-primary").Click();

        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u =>
            u.TwitchUserId == "user1"
            && u.OverlaySettings.TimerDurationMinutes == 15
            && u.OverlaySettings.TimerTextColor == "#112233"
            && u.OverlaySettings.TimerEnabled
            && u.OverlaySettings.TimerManualRunning
            && u.OverlaySettings.TimerManualStartUtc.HasValue
        )), Times.Once);

        _mockOverlayNotifier.Verify(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>()), Times.Once);
    }

    [Fact]
    public void StopTimerNow_ShouldClearManualState_AndNotifyOverlay()
    {
        var user = new User
        {
            TwitchUserId = "user1",
            OverlaySettings = new OverlaySettings
            {
                TimerEnabled = true,
                TimerDurationMinutes = 5,
                TimerManualRunning = true,
                TimerManualStartUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };

        _mockUserRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<TimerSettingsModal>(0);
            b.AddAttribute(1, nameof(TimerSettingsModal.Show), true);
            b.AddAttribute(2, nameof(TimerSettingsModal.UserId), "user1");
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".modal-title").Count == 1);

        // Click Stop Timer
        cut.Find("button.btn.btn-outline-danger").Click();

        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u =>
            !u.OverlaySettings.TimerManualRunning
            && u.OverlaySettings.TimerManualStartUtc == null
        )), Times.Once);

        _mockOverlayNotifier.Verify(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>()), Times.Once);
    }
}
