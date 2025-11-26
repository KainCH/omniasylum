using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Modals;

public class UserRequestsModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;

    public UserRequestsModalTests()
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
            b.OpenComponent<UserRequestsModal>(0);
            b.AddAttribute(1, nameof(UserRequestsModal.Show), false);
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
            b.OpenComponent<UserRequestsModal>(0);
            b.AddAttribute(1, nameof(UserRequestsModal.Show), true);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".text-muted").Count > 0, TimeSpan.FromSeconds(3));
        Assert.Contains("User Requests", cut.Markup);
        Assert.Contains("No pending requests found", cut.Markup);
    }

    [Fact]
    public void Close_ShouldInvokeShowChanged_WhenClicked()
    {
        // Arrange
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<UserRequestsModal>(0);
            b.AddAttribute(1, nameof(UserRequestsModal.Show), true);
            b.AddAttribute(2, nameof(UserRequestsModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".text-muted").Count > 0, TimeSpan.FromSeconds(3));

        // Act
        var closeButton = cut.Find("button.btn-close");
        closeButton.Click();

        // Assert
        cut.WaitForAssertion(() => Assert.True(showChangedInvoked));
    }
}
