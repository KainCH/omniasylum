using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Web.Components.Pages;
using Xunit;

namespace OmniForge.Tests.Components.Pages
{
    public class RequestAccessTests : BunitContext
    {
        private readonly Mock<NavigationManager> _mockNavigationManager;

        public RequestAccessTests()
        {
            _mockNavigationManager = new Mock<NavigationManager>();
            Services.AddSingleton(new HttpClient()); // Add real HttpClient as it's not used in the simulation path
            // Services.AddSingleton(_mockNavigationManager.Object); // NavigationManager is usually provided by BUnit's TestContext
        }

        [Fact]
        public void RendersForm_Initially()
        {
            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<RequestAccess>(0);
                b.CloseComponent();
            });

            // Assert
            Assert.NotNull(cut.Find("form"));
            Assert.NotNull(cut.Find("#username"));
            Assert.NotNull(cut.Find("#email"));
            Assert.NotNull(cut.Find("#reason"));
            Assert.Empty(cut.FindAll(".alert-success"));
        }

        [Fact]
        public void ShowsValidationErrors_WhenSubmittedEmpty()
        {
            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<RequestAccess>(0);
                b.CloseComponent();
            });

            var form = cut.Find("form");
            form.Submit();

            // Assert
            // Validation messages should appear
            var validationMessages = cut.FindAll(".validation-message");
            Assert.NotEmpty(validationMessages);
            Assert.Contains(validationMessages, m => m.TextContent.Contains("Twitch Username is required"));
            Assert.Contains(validationMessages, m => m.TextContent.Contains("Email is required"));
            Assert.Contains(validationMessages, m => m.TextContent.Contains("Please tell us why you want to join"));
        }

        [Fact]
        public void ShowsSuccessMessage_WhenSubmittedValid()
        {
            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<RequestAccess>(0);
                b.CloseComponent();
            });

            // Fill form
            cut.Find("#username").Change("TestUser");
            cut.Find("#email").Change("test@example.com");
            cut.Find("#reason").Change("I want to test this.");

            // Submit
            cut.Find("form").Submit();

            // Assert
            // The component simulates a 1s delay. BUnit's WaitForState can handle this.
            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0, TimeSpan.FromSeconds(3));

            cut.Find(".alert-success").MarkupMatches(
                @"<div class=""alert alert-success text-center"" role=""alert"">
                    <h4 class=""alert-heading""><i class=""bi bi-check-circle-fill me-2""></i>Request Submitted!</h4>
                    <p>Thank you for your interest. We will review your request and contact you shortly.</p>
                    <hr>
                    <p class=""mb-0"">
                        <a href=""/"" class=""btn btn-outline-success"">Return Home</a>
                    </p>
                </div>");
        }
    }
}
