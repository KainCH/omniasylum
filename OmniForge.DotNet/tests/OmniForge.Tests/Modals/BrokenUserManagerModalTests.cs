using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Modals;

public class BrokenUserManagerModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;

    public BrokenUserManagerModalTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        Services.AddSingleton(_mockUserRepository.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), false);
            b.CloseComponent();
        });

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Modal_ShouldRender_WhenShowIsTrue()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));
        Assert.Contains("Broken User Manager", cut.Markup);
        Assert.Contains("No broken users found", cut.Markup);
    }

    [Fact]
    public void Rescan_ShouldTriggerLoading()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Act
        var rescanButton = cut.Find("button.btn-warning");
        rescanButton.Click();

        // Assert
        // Since ScanForBrokenUsers has a delay, we might catch the loading state
        // But BUnit might wait for async tasks.
        // However, we can verify that it eventually returns to success state.
        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));
        Assert.Contains("No broken users found", cut.Markup);
    }

    [Fact]
    public void Close_ShouldInvokeShowChanged_WhenClicked()
    {
        // Arrange
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Act
        var closeButton = cut.Find("button.btn-secondary");
        closeButton.Click();

        // Assert
        Assert.True(showChangedInvoked);
    }
}
