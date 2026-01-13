using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using System.Security.Claims;
using Xunit;

namespace OmniForge.Tests.Modals;

public class OverlaySettingsModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
    private readonly Mock<IGameContextRepository> _mockGameContextRepository;
    private readonly Mock<IGameCoreCountersConfigRepository> _mockGameCoreCountersConfigRepository;
    private readonly MockAuthenticationStateProvider _authProvider;

    public OverlaySettingsModalTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockOverlayNotifier = new Mock<IOverlayNotifier>();
        _mockGameContextRepository = new Mock<IGameContextRepository>();
        _mockGameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();
        _authProvider = new MockAuthenticationStateProvider();

        _mockOverlayNotifier
            .Setup(n => n.NotifySettingsUpdateAsync(It.IsAny<string>(), It.IsAny<OverlaySettings>()))
            .Returns(Task.CompletedTask);

        _mockGameContextRepository
            .Setup(r => r.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((GameContext?)null);

        Services.AddSingleton(_mockUserRepository.Object);
        Services.AddSingleton(_mockOverlayNotifier.Object);
        Services.AddSingleton(_mockGameContextRepository.Object);
        Services.AddSingleton(_mockGameCoreCountersConfigRepository.Object);
        Services.AddSingleton<AuthenticationStateProvider>(_authProvider);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), false);
            b.CloseComponent();
        });

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Modal_ShouldRender_WhenShowIsTrue()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        user.OverlaySettings.Position = "top-right";
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        Assert.Contains("Overlay Settings", cut.Markup);
        Assert.Contains("top-right", cut.Find("select.form-select").GetAttribute("value"));
    }

    [Fact]
    public void SaveSettings_ShouldCallUpdateUser_WhenFormIsSubmitted()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Act
        var positionSelect = cut.Find("select.form-select");
        positionSelect.Change("bottom-left");

        var form = cut.Find("form");
        form.Submit();

        // Assert
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.OverlaySettings.Position == "bottom-left")), Times.Once);
    }

    [Fact]
    public void VisibleCounters_ShouldBeReadOnlyAndReflectCurrentSettings()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        user.OverlaySettings.Counters.Deaths = false;
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Assert
        var deathsCheckbox = cut.Find("#showDeaths");
        Assert.Equal("checkbox", deathsCheckbox.GetAttribute("type"));
        Assert.NotNull(deathsCheckbox.GetAttribute("disabled"));
        Assert.True(string.IsNullOrEmpty(deathsCheckbox.GetAttribute("checked")));

        // Saving the modal should not overwrite counter visibility.
        var form = cut.Find("form");
        form.Submit();
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => !u.OverlaySettings.Counters.Deaths)), Times.Once);
    }

    [Fact]
    public void ContextSelector_ShouldRender_ForModeratorWithManagedStreamers()
    {
        // Arrange
        var moderatorId = "mod-user-id";
        var streamerId = "streamer-user-id";

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, moderatorId),
            new Claim(ClaimTypes.Name, "modUser")
        }, "mock"));
        _authProvider.SetUser(principal);

        var moderator = new User
        {
            TwitchUserId = moderatorId,
            DisplayName = "Mod"
        };
        moderator.ManagedStreamers.Add(streamerId);

        var streamer = new User
        {
            TwitchUserId = streamerId,
            DisplayName = "Streamer"
        };

        _mockUserRepository.Setup(r => r.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
        _mockUserRepository.Setup(r => r.GetUserAsync(streamerId)).ReturnsAsync(streamer);

        // Initial load uses the UserId param (moderator)
        _mockUserRepository.Setup(r => r.GetUserAsync(It.Is<string>(id => id != streamerId && id != moderatorId)))
            .ReturnsAsync((User?)null);

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), moderatorId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("#contextUserSelect").Count == 1);
        var select = cut.Find("#contextUserSelect");
        Assert.Contains("Myself", select.InnerHtml);
        Assert.Contains("Streamer", select.InnerHtml);
    }

    [Fact]
    public void SaveSettings_ShouldUpdateStreamer_WhenModeratorSelectsStreamerContext()
    {
        // Arrange
        var moderatorId = "mod-user-id";
        var streamerId = "streamer-user-id";

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, moderatorId),
            new Claim(ClaimTypes.Name, "modUser")
        }, "mock"));
        _authProvider.SetUser(principal);

        var moderator = new User { TwitchUserId = moderatorId, DisplayName = "Mod" };
        moderator.ManagedStreamers.Add(streamerId);

        var streamer = new User { TwitchUserId = streamerId, DisplayName = "Streamer" };

        _mockUserRepository.Setup(r => r.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
        _mockUserRepository.Setup(r => r.GetUserAsync(streamerId)).ReturnsAsync(streamer);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), moderatorId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("#contextUserSelect").Count == 1);

        // Act: switch context to streamer, then submit
        var select = cut.Find("#contextUserSelect");
        select.Change(streamerId);

        cut.WaitForState(() => !cut.Markup.Contains("Loading..."));

        var form = cut.Find("form");
        form.Submit();

        // Assert: save should target the streamer user
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.TwitchUserId == streamerId)), Times.Once);
        _mockOverlayNotifier.Verify(n => n.NotifySettingsUpdateAsync(streamerId, It.IsAny<OverlaySettings>()), Times.Once);
    }

    public class MockAuthenticationStateProvider : AuthenticationStateProvider
    {
        private AuthenticationState _authState;

        public MockAuthenticationStateProvider()
        {
            _authState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public void SetUser(ClaimsPrincipal user)
        {
            _authState = new AuthenticationState(user);
            NotifyAuthenticationStateChanged(Task.FromResult(_authState));
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(_authState);
        }
    }

    [Fact]
    public void Close_ShouldInvokeShowChanged_WhenClicked()
    {
        // Arrange
        var userId = "test-user-id";
        var user = new User { TwitchUserId = userId };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.AddAttribute(3, nameof(OverlaySettingsModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Act
        var closeButton = cut.Find("button.btn-close");
        closeButton.Click();

        // Assert
        Assert.True(showChangedInvoked);
    }
}
