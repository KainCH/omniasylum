using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Pages;
using OmniForge.Web.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using Xunit;

namespace OmniForge.Tests.Components.Pages
{
    public class OverlayTests : BunitContext
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IAlertRepository> _mockAlertRepository;
        private readonly Mock<IHubConnectionFactory> _mockHubConnectionFactory;

        public OverlayTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockAlertRepository = new Mock<IAlertRepository>();
            _mockHubConnectionFactory = new Mock<IHubConnectionFactory>();

            Services.AddSingleton(_mockUserRepository.Object);
            Services.AddSingleton(_mockCounterRepository.Object);
            Services.AddSingleton(_mockAlertRepository.Object);
            Services.AddSingleton(_mockHubConnectionFactory.Object);

            // Setup default mock HubConnection
            var mockHubConnection = new Mock<IHubConnection>();

            mockHubConnection.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            mockHubConnection.Setup(h => h.DisposeAsync())
                .Returns(ValueTask.CompletedTask);

            _mockHubConnectionFactory.Setup(f => f.CreateConnection(It.IsAny<Uri>()))
                .Returns(mockHubConnection.Object);
        }

        [Fact]
        public void RendersLoading_WhenUserOrCounterIsNull()
        {
            // Arrange
            _mockUserRepository.Setup(r => r.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync((User?)null);

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<Overlay>(0);
                b.AddAttribute(1, "TwitchUserId", "testuser");
                b.CloseComponent();
            });

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
            var counter = new Counter { TwitchUserId = "testuser" };

            _mockUserRepository.Setup(r => r.GetUserAsync("testuser"))
                .ReturnsAsync(user);
            _mockCounterRepository.Setup(r => r.GetCountersAsync("testuser"))
                .ReturnsAsync(counter);
            _mockAlertRepository.Setup(r => r.GetAlertsAsync("testuser"))
                .ReturnsAsync(new List<Alert>());

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<Overlay>(0);
                b.AddAttribute(1, "TwitchUserId", "testuser");
                b.CloseComponent();
            });

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
                Features = new FeatureFlags { StreamOverlay = true },
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
                TwitchUserId = "testuser",
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

            JSInterop.SetupVoid("overlayInterop.init");

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<Overlay>(0);
                b.AddAttribute(1, "TwitchUserId", "testuser");
                b.CloseComponent();
            });

            // Assert
            Assert.Empty(cut.FindAll(".error"));
            var counters = cut.FindAll(".counter-item");
            Assert.Equal(3, counters.Count);

            var deaths = cut.Find(".deaths .counter-value");
            Assert.Contains("10", deaths.TextContent);

            var swears = cut.Find(".swears .counter-value");
            Assert.Contains("5", swears.TextContent);

            var screams = cut.Find(".screams .counter-value");
            Assert.Contains("2", screams.TextContent);
        }

        [Fact]
        public void RendersCorrectPosition_TopRight()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "testuser",
                Features = new FeatureFlags { StreamOverlay = true },
                OverlaySettings = new OverlaySettings
                {
                    Position = "top-right",
                    Counters = new OverlayCounters { Deaths = true },
                    Theme = new OverlayTheme()
                }
            };
            var counter = new Counter { TwitchUserId = "testuser" };

            _mockUserRepository.Setup(r => r.GetUserAsync("testuser")).ReturnsAsync(user);
            _mockCounterRepository.Setup(r => r.GetCountersAsync("testuser")).ReturnsAsync(counter);
            _mockAlertRepository.Setup(r => r.GetAlertsAsync("testuser")).ReturnsAsync(new List<Alert>());
            JSInterop.SetupVoid("overlayInterop.init");

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<Overlay>(0);
                b.AddAttribute(1, "TwitchUserId", "testuser");
                b.CloseComponent();
            });

            // Assert
            if (cut.FindAll(".error").Any())
            {
                throw new Exception($"Overlay is in error state: {cut.Find(".error").OuterHtml}");
            }
            var overlayDiv = cut.Find(".counter-overlay");
            var style = overlayDiv.GetAttribute("style");
            Assert.Contains("top: 20px", style);
            Assert.Contains("right: 20px", style);
        }

        [Fact]
        public void RendersCorrectPosition_BottomLeft()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "testuser",
                Features = new FeatureFlags { StreamOverlay = true },
                OverlaySettings = new OverlaySettings
                {
                    Position = "bottom-left",
                    Counters = new OverlayCounters { Deaths = true },
                    Theme = new OverlayTheme()
                }
            };
            var counter = new Counter { TwitchUserId = "testuser" };

            _mockUserRepository.Setup(r => r.GetUserAsync("testuser")).ReturnsAsync(user);
            _mockCounterRepository.Setup(r => r.GetCountersAsync("testuser")).ReturnsAsync(counter);
            _mockAlertRepository.Setup(r => r.GetAlertsAsync("testuser")).ReturnsAsync(new List<Alert>());
            JSInterop.SetupVoid("overlayInterop.init");

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<Overlay>(0);
                b.AddAttribute(1, "TwitchUserId", "testuser");
                b.CloseComponent();
            });

            // Assert
            var overlayDiv = cut.Find(".counter-overlay");
            var style = overlayDiv.GetAttribute("style");
            Assert.Contains("bottom: 20px", style);
            Assert.Contains("left: 20px", style);
        }

        [Fact]
        public void RendersOnlyEnabledCounters()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "testuser",
                Features = new FeatureFlags { StreamOverlay = true },
                OverlaySettings = new OverlaySettings
                {
                    Counters = new OverlayCounters
                    {
                        Deaths = true,
                        Swears = false,
                        Screams = true
                    },
                    Theme = new OverlayTheme()
                }
            };
            var counter = new Counter { TwitchUserId = "testuser" };

            _mockUserRepository.Setup(r => r.GetUserAsync("testuser")).ReturnsAsync(user);
            _mockCounterRepository.Setup(r => r.GetCountersAsync("testuser")).ReturnsAsync(counter);
            _mockAlertRepository.Setup(r => r.GetAlertsAsync("testuser")).ReturnsAsync(new List<Alert>());
            JSInterop.SetupVoid("overlayInterop.init");

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<Overlay>(0);
                b.AddAttribute(1, "TwitchUserId", "testuser");
                b.CloseComponent();
            });

            // Assert
            Assert.NotEmpty(cut.FindAll(".deaths"));
            Assert.Empty(cut.FindAll(".swears"));
            Assert.NotEmpty(cut.FindAll(".screams"));
        }

        [Fact]
        public void UpdatesCounter_WhenSignalRMessageReceived()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "testuser",
                Features = new FeatureFlags { StreamOverlay = true },
                OverlaySettings = new OverlaySettings
                {
                    Counters = new OverlayCounters { Deaths = true },
                    Theme = new OverlayTheme()
                }
            };
            var initialCounter = new Counter { TwitchUserId = "testuser", Deaths = 10 };

            _mockUserRepository.Setup(r => r.GetUserAsync(It.IsAny<string>())).ReturnsAsync(user);
            _mockCounterRepository.Setup(r => r.GetCountersAsync(It.IsAny<string>())).ReturnsAsync(initialCounter);
            _mockAlertRepository.Setup(r => r.GetAlertsAsync(It.IsAny<string>())).ReturnsAsync(new List<Alert>());
            JSInterop.SetupVoid("overlayInterop.init").SetVoidResult();
            var module = JSInterop.SetupModule("./js/overlay-websocket.js");
            module.SetupVoid("connect", _ => true).SetVoidResult();

            var cut = Render<Overlay>(parameters => parameters
                .Add(p => p.TwitchUserId, "testuser")
            );

            // Act
            // Simulate WebSocket message via JS Interop
            var updatedCounter = new Counter { TwitchUserId = "testuser", Deaths = 11 };
            // cut.Instance is the Overlay component
            cut.InvokeAsync(() => cut.Instance.OnCounterUpdate(updatedCounter));

            // Assert
            var deaths = cut.Find(".deaths .counter-value");
            Assert.Contains("11", deaths.TextContent);
        }
    }
}
