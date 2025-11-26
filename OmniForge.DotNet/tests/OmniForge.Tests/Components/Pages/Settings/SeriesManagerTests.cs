using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages.Settings;
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
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            _mockSeriesRepo.Setup(x => x.GetSeriesAsync(It.IsAny<string>()))
                .Returns(async () => { await Task.Delay(100); return new List<Series>(); });

            // Act
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<SeriesManager>(2);
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
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var seriesList = new List<Series>
            {
                new Series { Id = "1", Name = "Elden Ring", Description = "Run 1", Snapshot = new Counter { Deaths = 10 } }
            };
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(seriesList);

            // Act
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<SeriesManager>(2);
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
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(new List<Series>());
            _mockCounterRepo.Setup(x => x.GetCountersAsync("123")).ReturnsAsync(new Counter { Deaths = 5, Swears = 2 });
            _mockSeriesRepo.Setup(x => x.CreateSeriesAsync(It.IsAny<Series>())).Returns(Task.CompletedTask);

            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<SeriesManager>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll("input").Count > 0);

            // Act
            cut.Find("input[placeholder*='Elden Ring']").Change("Dark Souls");
            cut.Find("textarea").Change("No hit run");

            var saveBtn = cut.Find("button.btn-info");
            saveBtn.Click();

            // Assert
            _mockSeriesRepo.Verify(x => x.CreateSeriesAsync(It.Is<Series>(s =>
                s.UserId == "123" &&
                s.Name == "Dark Souls" &&
                s.Description == "No hit run" &&
                s.Snapshot.Deaths == 5
            )), Times.Once);

            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Series saved successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void LoadSeries_UpdatesCountersAndNotifies()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var series = new Series { Id = "1", Name = "Elden Ring", Snapshot = new Counter { Deaths = 100 } };
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(new List<Series> { series });
            _mockCounterRepo.Setup(x => x.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);
            _mockOverlayNotifier.Setup(x => x.NotifyCounterUpdateAsync(It.IsAny<string>(), It.IsAny<Counter>())).Returns(Task.CompletedTask);

            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<SeriesManager>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0);

            // Act
            var loadBtn = cut.Find("button.btn-outline-primary");
            loadBtn.Click();

            // Assert
            _mockCounterRepo.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.Deaths == 100 && c.TwitchUserId == "123")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("123", It.Is<Counter>(c => c.Deaths == 100)), Times.Once);

            cut.WaitForState(() => cut.FindAll(".alert-success").Count > 0);
            Assert.Contains("Loaded series 'Elden Ring' successfully!", cut.Find(".alert-success").TextContent);
        }

        [Fact]
        public void DeleteSeries_RemovesSeries()
        {
            // Arrange
            _mockAuthenticationStateProvider.SetUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "StreamerOne"),
                new Claim("userId", "123")
            }, "TestAuthType")));

            var series = new Series { Id = "1", Name = "Elden Ring" };
            _mockSeriesRepo.Setup(x => x.GetSeriesAsync("123")).ReturnsAsync(new List<Series> { series });
            _mockSeriesRepo.Setup(x => x.DeleteSeriesAsync("123", "1")).Returns(Task.CompletedTask);

            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<SeriesManager>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0);

            // Act
            var deleteBtn = cut.Find("button.btn-outline-danger");
            deleteBtn.Click();

            // Assert
            _mockSeriesRepo.Verify(x => x.DeleteSeriesAsync("123", "1"), Times.Once);

            cut.WaitForState(() => cut.FindAll(".alert-info").Count > 0);
            Assert.Contains("Series deleted.", cut.Find(".alert-info").TextContent);
        }
    }
}
