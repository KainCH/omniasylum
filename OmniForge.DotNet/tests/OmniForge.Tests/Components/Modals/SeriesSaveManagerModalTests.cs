using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

#pragma warning disable CS0618

namespace OmniForge.Tests.Components.Modals
{
    public class SeriesSaveManagerModalTests : TestContext
    {
        private readonly Mock<IUserRepository> _mockUserRepository;

        public SeriesSaveManagerModalTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            Services.AddSingleton(_mockUserRepository.Object);
        }

        [Fact]
        public void Modal_ShouldNotRender_WhenShowIsFalse()
        {
            // Act
            var cut = Render(b => {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", false);
                b.CloseComponent();
            });

            // Assert
            Assert.Empty(cut.Markup.Trim());
        }

        [Fact]
        public void Modal_ShouldRender_WhenShowIsTrue()
        {
            // Act
            var cut = Render(b => {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Assert
            Assert.NotNull(cut.Find(".modal"));
            cut.Find(".modal-title").MarkupMatches("<h5 class=\"modal-title\"><i class=\"bi bi-save me-2\"></i>Series Save Manager</h5>");
        }

        [Fact]
        public void Modal_ShouldClose_WhenCloseButtonClicked()
        {
            // Arrange
            var show = true;
            var cut = Render(b => {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", show);
                b.AddAttribute(2, "UserId", "123");
                b.AddAttribute(3, "ShowChanged", EventCallback.Factory.Create<bool>(this, (bool newValue) => show = newValue));
                b.CloseComponent();
            });

            // Act
            cut.Find(".btn-close").Click();

            // Assert
            Assert.False(show);
        }

        [Fact]
        public void Modal_ShouldLoadSeries_WhenShown()
        {
            // Arrange
            var cut = Render(b => {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Act
            // Wait for async loading to complete (simulated delay in component)
            cut.WaitForState(() => cut.FindAll("option").Count > 1, TimeSpan.FromSeconds(1));

            // Assert
            var options = cut.FindAll("option");
            Assert.Contains(options, o => o.TextContent.Contains("Dark Souls Run"));
            Assert.Contains(options, o => o.TextContent.Contains("Elden Ring"));
        }

        [Fact]
        public void Modal_ShouldShowDetails_WhenSeriesSelected()
        {
            // Arrange
            var cut = Render(b => {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("option").Count > 1, TimeSpan.FromSeconds(1));

            // Act
            var select = cut.Find("select");
            select.Change("1"); // Select "Dark Souls Run"

            // Assert
            cut.Find(".card-body h6").MarkupMatches("<h6>Dark Souls Run</h6>");
            cut.Find(".card-body .fs-4").MarkupMatches("<div class=\"fs-4\">452</div>"); // Deaths
        }
    }
}
