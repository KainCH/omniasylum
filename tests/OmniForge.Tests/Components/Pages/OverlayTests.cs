using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages;
using System.Reflection;
using Xunit;

namespace OmniForge.Tests.Components.Pages
{
    public class OverlayTests : TestContext
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IAlertRepository> _mockAlertRepository;
        private readonly Mock<IJSRuntime> _mockJSRuntime;

        public OverlayTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockAlertRepository = new Mock<IAlertRepository>();
            _mockJSRuntime = new Mock<IJSRuntime>();

            Services.AddSingleton(_mockUserRepository.Object);
            Services.AddSingleton(_mockCounterRepository.Object);
            Services.AddSingleton(_mockAlertRepository.Object);
            Services.AddSingleton(_mockJSRuntime.Object);
        }

        [Fact]
        public void RendersLoading_WhenUserOrCounterIsNull()
        {
            // Arrange
            _mockUserRepository.Setup(r => r.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync((User?)null);

            // Act
            var cut = Render(b => b
                .OpenComponent<Overlay>(0)
                .AddAttribute(1, "TwitchUserId", "testuser")
                .CloseComponent());

            // Assert
            cut.Find(".loading").MarkupMatches("<div class=\"loading\">Loading...</div>");
        }

        [Fact]
        public void RendersError_WhenStreamOverlayDisabled()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "testuser",
                Features = new FeatureFlags { StreamOverlay = false }
            };
            var counter = new Counter { UserId = "testuser" };

            _mockUserRepository.Setup(r => r.GetUserAsync("testuser"))
                .ReturnsAsync(user);
            _mockCounterRepository.Setup(r => r.GetCountersAsync("testuser"))
                .ReturnsAsync(counter);
            _mockAlertRepository.Setup(r => r.GetAlertsAsync("testuser"))
                .ReturnsAsync(new List<Alert>());

            // Act
            var cut = Render(b => b
                .OpenComponent<Overlay>(0)
                .AddAttribute(1, "TwitchUserId", "testuser")
                .CloseComponent());

            // Assert
            cut.Find(".error").MarkupMatches("<div class=\"error\">Stream overlay not enabled for this user</div>");
        }

        [Fact]
        public void RendersCounters_WhenStreamOverlayEnabled()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "testuser",
                Features = new FeatureFlags { StreamOverlay = false }, // Start disabled to avoid SignalR
                OverlaySettings = new OverlaySettings
                {
                    Counters = new OverlayCounters
                    {
                        Deaths = true,
                        Swears = true,
                        Screams = true
                    },
                    Theme = new OverlayTheme
                    {
                        BorderColor = "red",
                        TextColor = "white"
                    }
                }
            };
            var counter = new Counter
            {
                UserId = "testuser",
                Deaths = 10,
                Swears = 5,
                Screams = 2
            };

            _mockUserRepository.Setup(r => r.GetUserAsync("testuser"))
                .ReturnsAsync(user);
            _mockCounterRepository.Setup(r => r.GetCountersAsync("testuser"))
                .ReturnsAsync(counter);
            _mockAlertRepository.Setup(r => r.GetAlertsAsync("testuser"))
                .ReturnsAsync(new List<Alert>());

            _mockJSRuntime.Setup(x => x.InvokeAsync<object>("overlayInterop.init", It.IsAny<object[]>()))
                .ReturnsAsync((object)null);

            // Act
            var cut = Render(b => b
                .OpenComponent<Overlay>(0)
                .AddAttribute(1, "TwitchUserId", "testuser")
                .CloseComponent());

            // Verify disabled state first
            cut.Find(".error").MarkupMatches("<div class=\"error\">Stream overlay not enabled for this user</div>");

            // Enable overlay via reflection to bypass OnAfterRenderAsync SignalR check (which only runs on firstRender)
            // Actually, we need to update the user object that the component holds.
            // Since the component holds a reference to the 'user' object we created, we can just modify it.
            user.Features.StreamOverlay = true;

            // Trigger re-render
            cut.Render();

            // Assert
            var counters = cut.FindAll(".counter-item");
            Assert.Equal(3, counters.Count);

            var deaths = cut.Find(".deaths .counter-value");
            Assert.Contains("10", deaths.TextContent);

            var swears = cut.Find(".swears .counter-value");
            Assert.Contains("5", swears.TextContent);

            var screams = cut.Find(".screams .counter-value");
            Assert.Contains("2", screams.TextContent);
        }
    }
}
