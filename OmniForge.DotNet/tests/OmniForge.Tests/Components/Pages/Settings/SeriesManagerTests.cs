using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Settings;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System;

namespace OmniForge.Tests.Components.Pages.Settings
{
    public class SeriesManagerTests : BunitContext
    {
        private readonly Mock<ISeriesRepository> _mockSeriesRepo;
        private readonly Mock<ICounterRepository> _mockCounterRepo;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly MockAuthenticationStateProvider _mockAuthenticationStateProvider;

        public SeriesManagerTests()
        {
            _mockSeriesRepo = new Mock<ISeriesRepository>();
            _mockCounterRepo = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockAuthenticationStateProvider = new MockAuthenticationStateProvider();

            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);
            Services.AddAuthorizationCore();

            // Mock IAuthorizationService
            var mockAuthService = new Mock<IAuthorizationService>();
            mockAuthService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());
            Services.AddSingleton<IAuthorizationService>(mockAuthService.Object);

            Services.AddSingleton<ISeriesRepository>(_mockSeriesRepo.Object);
            Services.AddSingleton<ICounterRepository>(_mockCounterRepo.Object);
            Services.AddSingleton<IOverlayNotifier>(_mockOverlayNotifier.Object);
        }

        [Fact]
        public void RendersLoadingState_Initially()
        {
            // Arrange
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync(It.IsAny<string>()))
                .Returns(async () => { await Task.Delay(100); return new List<Series>(); });

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<SeriesSaveManager>(2);
                    builder.AddAttribute(3, "UserId", "123");
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            Assert.NotNull(cut.Find(".spinner-border"));
        }

        [Fact]
        public void RendersList_WhenLoaded()
        {
            // Arrange
            var seriesList = new List<Series>
            {
                new Series { Id = "1", Name = "Elden Ring", Description = "Run 1", Snapshot = new Counter { Deaths = 10 } }
            };
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(seriesList);

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<SeriesSaveManager>(2);
                    builder.AddAttribute(3, "UserId", "123");
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0);
            Assert.Contains("Elden Ring", cut.Markup);
            Assert.Contains("Run 1", cut.Markup);
            Assert.Contains("Deaths: 10", cut.Markup);
        }

        [Fact]
        public void SaveSeries_CreatesNewSeries()
        {
            // Arrange
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(new List<Series>());
            _mockCounterRepo.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(new Counter { Deaths = 5, Swears = 2 });
            _mockSeriesRepo.Setup(x => x.CreateSeriesAsync(It.IsAny<Series>())).Returns(Task.CompletedTask);

            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<SeriesSaveManager>(2);
                    builder.AddAttribute(3, "UserId", "123");
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("input").Count > 0);

            // Act
            cut.Find("input[placeholder*='Series Name']").Input("Dark Souls");
            cut.Find("input[placeholder*='Description']").Input("No hit run");

            var saveBtn = cut.Find("button.btn-success");
            saveBtn.Click();

            // Assert
            _mockSeriesRepo.Verify(x => x.CreateSeriesAsync(It.Is<Series>(s =>
                s.UserId == "123" &&
                s.Name == "Dark Souls" &&
                s.Description == "No hit run" &&
                s.Snapshot.Deaths == 5
            )), Times.Once);

            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Series 'Dark Souls' saved successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void LoadSeries_UpdatesCountersAndNotifies()
        {
            // Arrange
            var series = new Series { Id = "1", Name = "Elden Ring", Snapshot = new Counter { Deaths = 100, Swears = 0, Screams = 0, Bits = 0 } };
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(new List<Series> { series });
            _mockCounterRepo.Setup(x => x.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);
            _mockOverlayNotifier.Setup(x => x.NotifyCounterUpdateAsync(It.IsAny<string>(), It.IsAny<Counter>())).Returns(Task.CompletedTask);
            _mockSeriesRepo.Setup(x => x.UpdateSeriesAsync(It.IsAny<Series>())).Returns(Task.CompletedTask);

            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<SeriesSaveManager>(2);
                    builder.AddAttribute(3, "UserId", "123");
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0);

            // Act
            var loadBtn = cut.Find("button.btn-outline-primary");
            loadBtn.Click();

            cut.WaitForState(() => cut.FindAll(".bg-body-tertiary").Count > 0);
            cut.Find("button.btn-warning").Click();

            // Assert
            _mockCounterRepo.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.Deaths == 100 && c.TwitchUserId == "123")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("123", It.Is<Counter>(c => c.Deaths == 100)), Times.Once);

            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Loaded 'Elden Ring' successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void DeleteSeries_RemovesSeries()
        {
            // Arrange
            var series = new Series { Id = "1", Name = "Elden Ring" };
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(new List<Series> { series });
            _mockSeriesRepo.Setup(x => x.DeleteSeriesAsync("123", "1")).Returns(Task.CompletedTask);

            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<SeriesSaveManager>(2);
                    builder.AddAttribute(3, "UserId", "123");
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0);

            // Act
            var deleteBtn = cut.Find("button.btn-outline-danger");
            deleteBtn.Click();

            cut.WaitForState(() => cut.FindAll(".bg-body-tertiary").Count > 0);
            cut.Find("button.btn-danger").Click();

            // Assert
            _mockSeriesRepo.Verify(x => x.DeleteSeriesAsync("123", "1"), Times.Once);

            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("'Elden Ring' deleted successfully.", cut.Find(".alert-success").TextContent);
        }
    }
}
