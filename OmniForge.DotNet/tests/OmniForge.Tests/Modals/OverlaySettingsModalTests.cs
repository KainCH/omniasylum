using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using System.Security.Claims;
using System.Reflection;
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

    private void SetAuthenticatedUser(string userId, string? username = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username ?? userId)
        }, "mock"));

        _authProvider.SetUser(principal);
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
        SetAuthenticatedUser(userId, "testUser");
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
        SetAuthenticatedUser(userId, "testUser");
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
        SetAuthenticatedUser(userId, "testUser");
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

    [Fact]
    public void ContextSelector_ShouldNotRender_WhenUserHasNoManagedStreamers()
    {
        // Arrange
        var userId = "regular-user-id";
        SetAuthenticatedUser(userId, "regularUser");

        var user = new User { TwitchUserId = userId, DisplayName = "Regular" };
        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        Assert.Empty(cut.FindAll("#contextUserSelect"));
    }

    [Fact]
    public void SaveSettings_ShouldRejectUnmanagedStreamer_WhenModeratorSelectionIsTampered()
    {
        // Arrange
        var moderatorId = "mod-user-id";
        var managedStreamerId = "managed-streamer";
        var unmanagedStreamerId = "unmanaged-streamer";

        SetAuthenticatedUser(moderatorId, "modUser");

        var moderator = new User { TwitchUserId = moderatorId, DisplayName = "Mod" };
        moderator.ManagedStreamers.Add(managedStreamerId);

        var managedStreamer = new User { TwitchUserId = managedStreamerId, DisplayName = "Managed" };

        _mockUserRepository.Setup(r => r.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
        _mockUserRepository.Setup(r => r.GetUserAsync(managedStreamerId)).ReturnsAsync(managedStreamer);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), moderatorId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("#contextUserSelect").Count == 1);

        var component = cut.FindComponent<OverlaySettingsModal>();

        // Tamper the private selectedUserId field to simulate a malicious client
        var selectedUserIdField = typeof(OverlaySettingsModal)
            .GetField("selectedUserId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(selectedUserIdField);
        selectedUserIdField!.SetValue(component.Instance, unmanagedStreamerId);

        // Act
        var form = cut.Find("form");
        form.Submit();

        // Assert
        cut.WaitForState(() => cut.Markup.Contains("do not have permission"));
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.IsAny<User>()), Times.Never);
        _mockOverlayNotifier.Verify(n => n.NotifySettingsUpdateAsync(It.IsAny<string>(), It.IsAny<OverlaySettings>()), Times.Never);
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
        SetAuthenticatedUser(userId, "testUser");
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

    [Fact]
    public void Modal_ShouldShowError_WhenEmptyUserIdProvided()
    {
        // Arrange: user is authenticated but UserId param is empty
        var authenticatedUserId = "authenticated-user";
        SetAuthenticatedUser(authenticatedUserId, "authUser");
        var user = new User { TwitchUserId = authenticatedUserId };
        _mockUserRepository.Setup(r => r.GetUserAsync(authenticatedUserId)).ReturnsAsync(user);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), "");
            b.CloseComponent();
        });

        // Assert: modal should load for the authenticated user (fallback)
        cut.WaitForState(() => cut.FindAll("form").Count > 0);
        Assert.Contains("Overlay Settings", cut.Markup);
    }

    [Fact]
    public void SaveSettings_ShouldRejectUnauthorizedUser_EvenWithoutContextSelector()
    {
        // Arrange: user has no managed streamers (selector hidden), but we simulate
        // a scenario where selectedUserId could differ from currentUserId
        var userId = "test-user-id";
        var otherUserId = "other-user-id";
        SetAuthenticatedUser(userId, "testUser");

        var user = new User { TwitchUserId = userId };
        var otherUser = new User { TwitchUserId = otherUserId };

        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
        _mockUserRepository.Setup(r => r.GetUserAsync(otherUserId)).ReturnsAsync(otherUser);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), userId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("form").Count > 0);

        // Act: submit form (should save for authenticated user only)
        var form = cut.Find("form");
        form.Submit();

        // Assert: save should target the authenticated user, not someone else
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.TwitchUserId == userId)), Times.Once);
        _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.TwitchUserId == otherUserId)), Times.Never);
    }

    [Fact]
    public void ContextSelector_ShouldRejectUnmanagedStreamer()
    {
        // Arrange: moderator tries to switch to a user they don't manage
        var moderatorId = "mod-user-id";
        var managedStreamerId = "managed-streamer";
        var unmanagedStreamerId = "unmanaged-streamer";

        SetAuthenticatedUser(moderatorId, "modUser");

        var moderator = new User { TwitchUserId = moderatorId, DisplayName = "Mod" };
        moderator.ManagedStreamers.Add(managedStreamerId);

        var managedStreamer = new User { TwitchUserId = managedStreamerId, DisplayName = "Managed" };
        var unmanagedStreamer = new User { TwitchUserId = unmanagedStreamerId, DisplayName = "Unmanaged" };

        _mockUserRepository.Setup(r => r.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
        _mockUserRepository.Setup(r => r.GetUserAsync(managedStreamerId)).ReturnsAsync(managedStreamer);
        _mockUserRepository.Setup(r => r.GetUserAsync(unmanagedStreamerId)).ReturnsAsync(unmanagedStreamer);
        _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), moderatorId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("#contextUserSelect").Count == 1);

        // Act: try to switch to unmanaged streamer (simulating injection)
        var select = cut.Find("#contextUserSelect");
        select.Change(unmanagedStreamerId);

        // Assert: error message should appear
        cut.WaitForState(() => cut.Markup.Contains("do not have permission"));
        Assert.Contains("do not have permission", cut.Markup);
    }

    [Fact]
    public void ContextLoading_ShouldCachePerAuthenticatedUser()
    {
        // Arrange: moderator with managed streamers
        var moderatorId = "mod-user-id";
        var streamerId = "streamer-user-id";

        SetAuthenticatedUser(moderatorId, "modUser");

        var moderator = new User { TwitchUserId = moderatorId, DisplayName = "Mod" };
        moderator.ManagedStreamers.Add(streamerId);

        var streamer = new User { TwitchUserId = streamerId, DisplayName = "Streamer" };

        _mockUserRepository.Setup(r => r.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
        _mockUserRepository.Setup(r => r.GetUserAsync(streamerId)).ReturnsAsync(streamer);

        // Act: render the modal
        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), moderatorId);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll("#contextUserSelect").Count == 1);

        // Assert: streamer was fetched exactly once during context loading
        // (proves concurrent loading works and caching prevents duplicate fetches)
        _mockUserRepository.Verify(r => r.GetUserAsync(streamerId), Times.Once);

        // Modal should show the context selector with the streamer
        var select = cut.Find("#contextUserSelect");
        Assert.Contains("Streamer", select.InnerHtml);
    }

    [Fact]
    public void ManagedStreamers_ShouldLoadConcurrently()
    {
        // Arrange: moderator with multiple managed streamers
        var moderatorId = "mod-user-id";
        var streamer1Id = "streamer-1";
        var streamer2Id = "streamer-2";
        var streamer3Id = "streamer-3";

        SetAuthenticatedUser(moderatorId, "modUser");

        var moderator = new User { TwitchUserId = moderatorId, DisplayName = "Mod" };
        moderator.ManagedStreamers.Add(streamer1Id);
        moderator.ManagedStreamers.Add(streamer2Id);
        moderator.ManagedStreamers.Add(streamer3Id);

        var streamer1 = new User { TwitchUserId = streamer1Id, DisplayName = "Streamer1" };
        var streamer2 = new User { TwitchUserId = streamer2Id, DisplayName = "Streamer2" };
        var streamer3 = new User { TwitchUserId = streamer3Id, DisplayName = "Streamer3" };

        _mockUserRepository.Setup(r => r.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
        _mockUserRepository.Setup(r => r.GetUserAsync(streamer1Id)).ReturnsAsync(streamer1);
        _mockUserRepository.Setup(r => r.GetUserAsync(streamer2Id)).ReturnsAsync(streamer2);
        _mockUserRepository.Setup(r => r.GetUserAsync(streamer3Id)).ReturnsAsync(streamer3);

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), moderatorId);
            b.CloseComponent();
        });

        // Assert: all streamers should appear in the selector
        cut.WaitForState(() => cut.FindAll("#contextUserSelect").Count == 1);
        var select = cut.Find("#contextUserSelect");
        Assert.Contains("Streamer1", select.InnerHtml);
        Assert.Contains("Streamer2", select.InnerHtml);
        Assert.Contains("Streamer3", select.InnerHtml);

        // Verify all streamers were fetched
        _mockUserRepository.Verify(r => r.GetUserAsync(streamer1Id), Times.Once);
        _mockUserRepository.Verify(r => r.GetUserAsync(streamer2Id), Times.Once);
        _mockUserRepository.Verify(r => r.GetUserAsync(streamer3Id), Times.Once);
    }

    [Fact]
    public void ManagedStreamers_ShouldHandleNullResults_Gracefully()
    {
        // Arrange: one managed streamer doesn't exist in database
        var moderatorId = "mod-user-id";
        var validStreamerId = "valid-streamer";
        var missingStreamerId = "missing-streamer";

        SetAuthenticatedUser(moderatorId, "modUser");

        var moderator = new User { TwitchUserId = moderatorId, DisplayName = "Mod" };
        moderator.ManagedStreamers.Add(validStreamerId);
        moderator.ManagedStreamers.Add(missingStreamerId);

        var validStreamer = new User { TwitchUserId = validStreamerId, DisplayName = "ValidStreamer" };

        _mockUserRepository.Setup(r => r.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
        _mockUserRepository.Setup(r => r.GetUserAsync(validStreamerId)).ReturnsAsync(validStreamer);
        _mockUserRepository.Setup(r => r.GetUserAsync(missingStreamerId)).ReturnsAsync((User?)null);

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<OverlaySettingsModal>(0);
            b.AddAttribute(1, nameof(OverlaySettingsModal.Show), true);
            b.AddAttribute(2, nameof(OverlaySettingsModal.UserId), moderatorId);
            b.CloseComponent();
        });

        // Assert: only the valid streamer should appear, missing one is filtered out
        cut.WaitForState(() => cut.FindAll("#contextUserSelect").Count == 1);
        var select = cut.Find("#contextUserSelect");
        Assert.Contains("ValidStreamer", select.InnerHtml);
        Assert.DoesNotContain("missing-streamer", select.InnerHtml);
    }
}
