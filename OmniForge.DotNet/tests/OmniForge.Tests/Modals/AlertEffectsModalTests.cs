using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using OmniForge.Web.Components.Modals;
using Xunit;
using System.Text.Json;

namespace OmniForge.Tests.Modals;

public class AlertEffectsModalTests : BunitContext
{
    private readonly Mock<IJSRuntime> _mockJSRuntime;

    public AlertEffectsModalTests()
    {
        _mockJSRuntime = new Mock<IJSRuntime>();
        Services.AddSingleton(_mockJSRuntime.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<AlertEffectsModal>(0);
            b.AddAttribute(1, nameof(AlertEffectsModal.Show), false);
            b.CloseComponent();
        });

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Modal_ShouldRender_WhenShowIsTrue()
    {
        // Arrange
        _mockJSRuntime.Setup(x => x.InvokeAsync<string>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync(string.Empty);

        var cut = Render(b =>
        {
            b.OpenComponent<AlertEffectsModal>(0);
            b.AddAttribute(1, nameof(AlertEffectsModal.Show), true);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("input").Count > 0);
        Assert.Contains("Alert Effects", cut.Markup);
    }

    [Fact]
    public void SaveSettings_ShouldCallLocalStorage_WhenClicked()
    {
        // Arrange
        _mockJSRuntime.Setup(x => x.InvokeAsync<string>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync(string.Empty);

        var cut = Render(b =>
        {
            b.OpenComponent<AlertEffectsModal>(0);
            b.AddAttribute(1, nameof(AlertEffectsModal.Show), true);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("input").Count > 0);

        // Act
        var saveButton = cut.Find("button.btn-primary");
        saveButton.Click();

        // Assert
        _mockJSRuntime.Verify(x => x.InvokeAsync<object>("localStorage.setItem", It.Is<object[]>(args =>
            args.Length == 2 &&
            (string)args[0] == "asylumEffectsSettings"
        )), Times.Once);
    }

    [Fact]
    public void SaveVolume_ShouldCallLocalStorage_WhenChanged()
    {
        // Arrange
        _mockJSRuntime.Setup(x => x.InvokeAsync<string>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync(string.Empty);

        var cut = Render(b =>
        {
            b.OpenComponent<AlertEffectsModal>(0);
            b.AddAttribute(1, nameof(AlertEffectsModal.Show), true);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("input").Count > 0);

        // Act
        var volumeInput = cut.Find("input[type='range']");
        volumeInput.Change(50);

        // Assert
        _mockJSRuntime.Verify(x => x.InvokeAsync<object>("localStorage.setItem", It.Is<object[]>(args =>
            args.Length == 2 &&
            (string)args[0] == "asylumEffectsVolume" &&
            (string)args[1] == "50"
        )), Times.Once);
    }

    [Fact]
    public void Close_ShouldInvokeShowChanged_WhenClicked()
    {
        // Arrange
        _mockJSRuntime.Setup(x => x.InvokeAsync<string>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync(string.Empty);
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<AlertEffectsModal>(0);
            b.AddAttribute(1, nameof(AlertEffectsModal.Show), true);
            b.AddAttribute(2, nameof(AlertEffectsModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("input").Count > 0);

        // Act
        var closeButton = cut.Find("button.btn-secondary");
        closeButton.Click();

        // Assert
        Assert.True(showChangedInvoked);
    }
}
