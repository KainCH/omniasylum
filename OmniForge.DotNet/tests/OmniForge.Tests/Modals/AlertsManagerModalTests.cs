using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Modals;

public class AlertsManagerModalTests : BunitContext
{
    private readonly Mock<IAlertRepository> _mockAlertRepository;
    private readonly Mock<IUserRepository> _mockUserRepository;

    public AlertsManagerModalTests()
    {
        _mockAlertRepository = new Mock<IAlertRepository>();
        _mockUserRepository = new Mock<IUserRepository>();
        Services.AddSingleton(_mockAlertRepository.Object);
        Services.AddSingleton(_mockUserRepository.Object);

        _mockUserRepository
            .Setup(r => r.GetUserAsync(It.IsAny<string>()))
            .ReturnsAsync((string userId) => new User
            {
                TwitchUserId = userId,
                Features = new FeatureFlags { StreamAlerts = true }
            });

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<AlertsManagerModal>(0);
            b.AddAttribute(1, nameof(AlertsManagerModal.Show), false);
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
        var alerts = new List<Alert>
        {
            new Alert { Id = "1", UserId = userId, Name = "Test Alert", Type = "death", VisualCue = "shake", IsEnabled = true }
        };
        _mockAlertRepository.Setup(r => r.GetAlertsAsync(userId)).ReturnsAsync(alerts);

        var cut = Render(b =>
        {
            b.OpenComponent<AlertsManagerModal>(0);
            b.AddAttribute(1, nameof(AlertsManagerModal.Show), true);
            b.AddAttribute(2, nameof(AlertsManagerModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("table").Count > 0);
        Assert.Contains("Manage Alerts", cut.Markup);
        Assert.Contains("Test Alert", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldShowLoading_WhenFetchingAlerts()
    {
        // Arrange
        var userId = "test-user-id";
        var taskCompletionSource = new TaskCompletionSource<IEnumerable<Alert>>();
        _mockAlertRepository.Setup(r => r.GetAlertsAsync(userId)).Returns(taskCompletionSource.Task);

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<AlertsManagerModal>(0);
            b.AddAttribute(1, nameof(AlertsManagerModal.Show), true);
            b.AddAttribute(2, nameof(AlertsManagerModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        Assert.Contains("spinner-border", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldShowEmptyMessage_WhenNoAlerts()
    {
        // Arrange
        var userId = "test-user-id";
        _mockAlertRepository
            .SetupSequence(r => r.GetAlertsAsync(userId))
            .ReturnsAsync(new List<Alert>())
            .ReturnsAsync(new List<Alert>
            {
                new Alert { Id = "seeded", UserId = userId, Name = "New Follower", Type = "follow", VisualCue = "door", IsEnabled = true }
            });

        _mockAlertRepository.Setup(r => r.SaveAlertAsync(It.IsAny<Alert>())).Returns(Task.CompletedTask);
        _mockAlertRepository.Setup(r => r.GetEventMappingsAsync(userId)).ReturnsAsync(new Dictionary<string, string>());

        var cut = Render(b =>
        {
            b.OpenComponent<AlertsManagerModal>(0);
            b.AddAttribute(1, nameof(AlertsManagerModal.Show), true);
            b.AddAttribute(2, nameof(AlertsManagerModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("table").Count > 0);
        _mockAlertRepository.Verify(r => r.SaveAlertAsync(It.IsAny<Alert>()), Times.AtLeastOnce);
    }

    [Fact]
    public void ToggleAlert_ShouldCallSaveAlert()
    {
        // Arrange
        var userId = "test-user-id";
        var alert = new Alert { Id = "1", UserId = userId, Name = "Test Alert", Type = "death", IsEnabled = true };
        _mockAlertRepository.Setup(r => r.GetAlertsAsync(userId)).ReturnsAsync(new List<Alert> { alert });
        _mockAlertRepository.Setup(r => r.SaveAlertAsync(It.IsAny<Alert>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<AlertsManagerModal>(0);
            b.AddAttribute(1, nameof(AlertsManagerModal.Show), true);
            b.AddAttribute(2, nameof(AlertsManagerModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("table").Count > 0);

        // Act
        var toggle = cut.Find("input[type='checkbox']");
        toggle.Change(false);

        // Assert
        _mockAlertRepository.Verify(r => r.SaveAlertAsync(It.Is<Alert>(a => a.IsEnabled == false)), Times.Once);
    }

    [Fact]
    public void Close_ShouldInvokeShowChanged()
    {
        // Arrange
        var userId = "test-user-id";
        _mockAlertRepository
            .SetupSequence(r => r.GetAlertsAsync(userId))
            .ReturnsAsync(new List<Alert>())
            .ReturnsAsync(new List<Alert>
            {
                new Alert { Id = "seeded", UserId = userId, Name = "New Follower", Type = "follow", VisualCue = "door", IsEnabled = true }
            });

        _mockAlertRepository.Setup(r => r.SaveAlertAsync(It.IsAny<Alert>())).Returns(Task.CompletedTask);
        _mockAlertRepository.Setup(r => r.GetEventMappingsAsync(userId)).ReturnsAsync(new Dictionary<string, string>());
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<AlertsManagerModal>(0);
            b.AddAttribute(1, nameof(AlertsManagerModal.Show), true);
            b.AddAttribute(2, nameof(AlertsManagerModal.UserId), userId);
            b.AddAttribute(3, nameof(AlertsManagerModal.ShowChanged), Microsoft.AspNetCore.Components.EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".modal").Count > 0);

        // Act
        var closeButton = cut.Find("button.btn-close");
        closeButton.Click();

        // Assert
        Assert.True(showChangedInvoked);
    }

    [Fact]
    public void Modal_ShouldDisplayAlertDetails()
    {
        // Arrange
        var userId = "test-user-id";
        var alerts = new List<Alert>
        {
            new Alert { Id = "1", UserId = userId, Name = "Death Alert", Type = "death", VisualCue = "shake", IsEnabled = true },
            new Alert { Id = "2", UserId = userId, Name = "Swear Alert", Type = "swear", VisualCue = "flash", IsEnabled = false }
        };
        _mockAlertRepository.Setup(r => r.GetAlertsAsync(userId)).ReturnsAsync(alerts);

        var cut = Render(b =>
        {
            b.OpenComponent<AlertsManagerModal>(0);
            b.AddAttribute(1, nameof(AlertsManagerModal.Show), true);
            b.AddAttribute(2, nameof(AlertsManagerModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("table").Count > 0);
        Assert.Contains("Death Alert", cut.Markup);
        Assert.Contains("Swear Alert", cut.Markup);
        Assert.Contains("death", cut.Markup);
        Assert.Contains("swear", cut.Markup);
    }
}
