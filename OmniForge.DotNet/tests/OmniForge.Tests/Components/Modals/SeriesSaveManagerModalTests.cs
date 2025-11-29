using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
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
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<ISeriesRepository> _mockSeriesRepository;

        public SeriesSaveManagerModalTests()
        {
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockSeriesRepository = new Mock<ISeriesRepository>();

            Services.AddSingleton(_mockOverlayNotifier.Object);
            Services.AddSingleton(_mockCounterRepository.Object);
            Services.AddSingleton(_mockSeriesRepository.Object);

            // Default setup
            _mockSeriesRepository.Setup(x => x.GetSeriesAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Series>());
            _mockCounterRepository.Setup(x => x.GetCountersAsync(It.IsAny<string>()))
                .ReturnsAsync(new Counter());
        }

        [Fact]
        public void Modal_ShouldNotRender_WhenShowIsFalse()
        {
            // Act
            var cut = Render(b =>
            {
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
            var cut = Render(b =>
            {
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
            var show = true;
            var cut = Render(b =>
            {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", show);
                b.AddAttribute(2, "UserId", "123");
                b.AddAttribute(3, "ShowChanged", Microsoft.AspNetCore.Components.EventCallback.Factory.Create<bool>(this, (bool newValue) => show = newValue));
                b.CloseComponent();
            });

            // Wait for modal to render
            cut.WaitForElement(".btn-close", TimeSpan.FromSeconds(2));

            // Act
            cut.Find(".btn-close").Click();

            // Assert
            Assert.False(show);
        }

        [Fact]
        public void Modal_ShouldLoadSeries_WhenShown()
        {
            // Arrange
            var seriesList = new List<Series>
            {
                new Series { Id = "1", Name = "Dark Souls Run", Description = "", Snapshot = new Counter { Deaths = 452, Swears = 120, Screams = 30 }, LastUpdated = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, IsActive = false },
                new Series { Id = "2", Name = "Elden Ring", Description = "First playthrough", Snapshot = new Counter { Deaths = 890, Swears = 450, Screams = 100, Bits = 500 }, LastUpdated = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, IsActive = true }
            };

            _mockSeriesRepository.Setup(x => x.GetSeriesAsync("123"))
                .ReturnsAsync(seriesList);

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Wait for async loading to complete
            cut.WaitForState(() => !cut.Markup.Contains("spinner-border"), TimeSpan.FromSeconds(3));

            // Assert
            var markup = cut.Markup;
            Assert.Contains("Dark Souls Run", markup);
            Assert.Contains("Elden Ring", markup);
        }

        [Fact]
        public void Modal_ShouldShowCreateForm()
        {
            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Wait for modal to render
            cut.WaitForElement(".card-header", TimeSpan.FromSeconds(2));

            // Assert
            Assert.Contains("Create New Save", cut.Markup);
            Assert.NotNull(cut.Find("input[placeholder*='Series Name']"));
            Assert.NotNull(cut.Find("input[placeholder*='Description']"));
        }

        [Fact]
        public void Modal_ShouldDisableSaveButton_WhenNameIsEmpty()
        {
            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Wait for modal to render
            cut.WaitForElement(".btn-success", TimeSpan.FromSeconds(2));

            // Assert
            var saveButton = cut.Find(".card-body .btn-success");
            Assert.True(saveButton.HasAttribute("disabled"));
        }

        [Fact]
        public void Modal_ShouldShowEmptyState_WhenNoSaves()
        {
            // Arrange
            _mockSeriesRepository.Setup(x => x.GetSeriesAsync("123"))
                .ReturnsAsync(new List<Series>());

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Wait for modal to render
            cut.WaitForState(() => !cut.Markup.Contains("spinner-border"), TimeSpan.FromSeconds(2));

            // Assert
            Assert.Contains("No saved series yet", cut.Markup);
        }

        [Fact]
        public void Modal_ShouldShowActiveBadge_WhenSeriesIsActive()
        {
            // Arrange
            var seriesList = new List<Series>
            {
                new Series { Id = "1", Name = "Dark Souls Run", Snapshot = new Counter { Deaths = 452 }, LastUpdated = DateTimeOffset.UtcNow, IsActive = true }
            };

            _mockSeriesRepository.Setup(x => x.GetSeriesAsync("123"))
                .ReturnsAsync(seriesList);

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Wait for series to load
            cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0, TimeSpan.FromSeconds(2));

            // Assert
            Assert.Contains("Active", cut.Markup);
            Assert.NotNull(cut.Find(".badge.bg-primary"));
        }

        [Fact]
        public void Modal_ShouldShowActionButtons_ForEachSeries()
        {
            // Arrange
            var seriesList = new List<Series>
            {
                new Series { Id = "1", Name = "Dark Souls Run", Snapshot = new Counter { Deaths = 452 }, LastUpdated = DateTimeOffset.UtcNow, IsActive = false }
            };

            _mockSeriesRepository.Setup(x => x.GetSeriesAsync("123"))
                .ReturnsAsync(seriesList);

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<SeriesSaveManagerModal>(0);
                b.AddAttribute(1, "Show", true);
                b.AddAttribute(2, "UserId", "123");
                b.CloseComponent();
            });

            // Wait for series to load
            cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0, TimeSpan.FromSeconds(2));

            // Assert - Check for Load, Overwrite, Delete buttons
            Assert.NotNull(cut.Find(".btn-outline-primary")); // Load
            Assert.NotNull(cut.Find(".btn-outline-warning")); // Overwrite
            Assert.NotNull(cut.Find(".btn-outline-danger"));  // Delete
        }
    }
}
