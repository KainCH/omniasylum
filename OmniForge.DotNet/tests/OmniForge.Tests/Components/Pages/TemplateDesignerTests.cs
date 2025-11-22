using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using OmniForge.Web.Components.Pages;
using Xunit;
using System.Security.Claims;

namespace OmniForge.Tests.Components.Pages
{
    public class TemplateDesignerTests : BunitContext
    {
        private readonly Mock<IJSRuntime> _mockJSRuntime;
        private readonly Mock<IAuthorizationService> _mockAuthorizationService;
        private readonly MockAuthenticationStateProvider _mockAuthenticationStateProvider;

        public TemplateDesignerTests()
        {
            _mockJSRuntime = new Mock<IJSRuntime>();
            _mockAuthorizationService = new Mock<IAuthorizationService>();
            _mockAuthenticationStateProvider = new MockAuthenticationStateProvider();

            Services.AddSingleton(_mockJSRuntime.Object);
            Services.AddSingleton(_mockAuthorizationService.Object);
            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);
            Services.AddSingleton<IAuthorizationService>(_mockAuthorizationService.Object);
        }

        [Fact]
        public void RendersCorrectly_WithDefaultValues()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser")
            }, "TestAuthType")));

            // Act
            var cut = Render(b => {
                b.OpenComponent<TemplateDesigner>(0);
                b.CloseComponent();
            });

            // Assert
            // Check for default values in inputs
            var bgColorInput = cut.Find("input[title='Choose background color']");
            Assert.Equal("#000000", bgColorInput.GetAttribute("value"));

            var textColorInput = cut.Find("input[title='Choose text color']");
            Assert.Equal("#ffffff", textColorInput.GetAttribute("value"));
        }

        [Fact]
        public void UpdatesModel_WhenInputChanges()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser")
            }, "TestAuthType")));

            var cut = Render(b => {
                b.OpenComponent<TemplateDesigner>(0);
                b.CloseComponent();
            });

            // Act
            var bgColorInput = cut.Find("input[title='Choose background color']");
            bgColorInput.Input("#123456");

            // Assert
            // Verify the model updated by checking the text input bound to the same property
            var textInputs = cut.FindAll("input[type='text']");
            // The first text input is for background color
            Assert.Equal("#123456", textInputs[0].GetAttribute("value"));
        }

        [Fact]
        public void ResetTemplate_RestoresDefaultValues()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser")
            }, "TestAuthType")));

            var cut = Render(b => {
                b.OpenComponent<TemplateDesigner>(0);
                b.CloseComponent();
            });

            // Change some values
            var bgColorInput = cut.Find("input[title='Choose background color']");
            bgColorInput.Input("#abcdef");

            // Act
            var resetButton = cut.Find("button.btn-outline-secondary");
            resetButton.Click();

            // Assert
            // Should be back to default #000000
            var textInputs = cut.FindAll("input[type='text']");
            Assert.Equal("#000000", textInputs[0].GetAttribute("value"));
        }

        [Fact]
        public void SaveTemplate_InvokesJSRuntime()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser")
            }, "TestAuthType")));

            var cut = Render(b => {
                b.OpenComponent<TemplateDesigner>(0);
                b.CloseComponent();
            });

            // Act
            var saveButton = cut.Find("button.btn-primary");
            saveButton.Click();

            // Assert
            _mockJSRuntime.Verify(x => x.InvokeAsync<object>("alert", It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public void PreviewStyle_CalculatesRgbaCorrectly()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser")
            }, "TestAuthType")));

            var cut = Render(b => {
                b.OpenComponent<TemplateDesigner>(0);
                b.CloseComponent();
            });

            // Act
            // Set Opacity to 0.5 (50%)
            var opacityInput = cut.Find("input[type='range'][step='0.05']");
            opacityInput.Input("0.5");

            // Assert
            var previewDiv = cut.Find(".overlay-preview");
            var style = previewDiv.GetAttribute("style");

            // Default bg is #000000 (0,0,0). With 0.5 opacity -> rgba(0, 0, 0, 0.5)
            Assert.Contains("rgba(0, 0, 0, 0.5)", style);
        }
    }
}
