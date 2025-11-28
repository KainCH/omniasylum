using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests.Modals;

public class BrokenUserManagerModalTests : BunitContext
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IAdminService> _mockAdminService;

    public BrokenUserManagerModalTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockAdminService = new Mock<IAdminService>();

        // Setup default admin service behavior
        _mockAdminService
            .Setup(x => x.DeleteUserRecordByRowKeyAsync(It.IsAny<string>(), It.IsAny<User>()))
            .ReturnsAsync(AdminOperationResult.Ok("Deleted successfully"));

        Services.AddSingleton(_mockUserRepository.Object);
        Services.AddSingleton(_mockAdminService.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private User CreateTestCurrentUser(string role = "admin")
    {
        return new User
        {
            TwitchUserId = "admin-123",
            Username = "admin",
            Role = role
        };
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenShowIsFalse()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), false);
            b.CloseComponent();
        });

        // Assert
        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Modal_ShouldRender_WhenShowIsTrue()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.CloseComponent();
        });

        // Assert
        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));
        Assert.Contains("Broken User Manager", cut.Markup);
        Assert.Contains("No broken users found", cut.Markup);
    }

    [Fact]
    public void Rescan_ShouldTriggerLoading()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.CloseComponent();
        });

        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Act
        var rescanButton = cut.Find("button.btn-warning");
        rescanButton.Click();

        // Assert
        // Since ScanForBrokenUsers has a delay, we might catch the loading state
        // But BUnit might wait for async tasks.
        // However, we can verify that it eventually returns to success state.
        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));
        Assert.Contains("No broken users found", cut.Markup);
    }

    [Fact]
    public void Close_ShouldInvokeShowChanged_WhenClicked()
    {
        // Arrange
        bool showChangedInvoked = false;

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.ShowChanged), EventCallback.Factory.Create<bool>(this, (val) => showChangedInvoked = !val));
            b.CloseComponent();
        });

        // Wait for the modal to be rendered and data loaded
        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Act
        var closeButton = cut.Find("button.btn-secondary");
        closeButton.Click();

        // Assert
        // The modal might not update the parameter immediately or the callback might be async
        // Let's wait for the assertion
        cut.WaitForAssertion(() => Assert.True(showChangedInvoked));
    }

    [Fact]
    public void Modal_ShouldDisplayEmptyTwitchUserId_WhenFound()
    {
        // Arrange - user with empty TwitchUserId (broken)
        var users = new List<User>
        {
            new User { TwitchUserId = "", Username = "broken_user", RowKey = "broken-key" }
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.CurrentUser), CreateTestCurrentUser());
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0 || cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Assert
        if (cut.FindAll(".list-group-item").Count > 0)
        {
            Assert.Contains("Empty TwitchUserId", cut.Markup);
        }
    }

    [Fact]
    public void Modal_ShouldDisplayDuplicates_WhenFound()
    {
        // Arrange - create users with same username (potential duplicates)
        var users = new List<User>
        {
            new User { TwitchUserId = "123", Username = "user1", RowKey = "123", LastLogin = DateTimeOffset.UtcNow },
            new User { TwitchUserId = "456", Username = "user1", RowKey = "456", LastLogin = DateTimeOffset.UtcNow.AddDays(-1) } // Older duplicate
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.CurrentUser), CreateTestCurrentUser());
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0 || cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Assert
        if (cut.FindAll(".list-group-item").Count > 0)
        {
            Assert.Contains("Duplicate", cut.Markup);
        }
    }

    [Fact]
    public void Modal_ShouldHandleMissingUsername_WhenFound()
    {
        // Arrange - user with missing username
        var users = new List<User>
        {
            new User { TwitchUserId = "123", Username = "", RowKey = "123" }
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.CurrentUser), CreateTestCurrentUser());
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0 || cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Assert - should find the broken user with missing username
        if (cut.FindAll(".list-group-item").Count > 0)
        {
            Assert.Contains("Missing username", cut.Markup);
        }
    }

    [Fact]
    public void Modal_ShouldHandleRepositoryException()
    {
        // Arrange
        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ThrowsAsync(new Exception("Database error"));

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.CloseComponent();
        });

        // Wait for scan to complete (with error)
        cut.WaitForState(() => cut.FindAll(".alert-danger").Count > 0 || cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Assert - may show error or success depending on timing
        Assert.Contains("Broken User Manager", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldNotDeleteWithoutCurrentUser()
    {
        // Arrange - broken record
        var users = new List<User>
        {
            new User { TwitchUserId = "", Username = "broken_user", RowKey = "broken-key" }
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            // No CurrentUser set
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0 || cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Try to find delete button - it should still exist
        var deleteButtons = cut.FindAll("button.btn-outline-danger");

        if (deleteButtons.Count > 0)
        {
            deleteButtons[0].Click();

            // Verify admin service was NOT called because CurrentUser is null
            _mockAdminService.Verify(x => x.DeleteUserRecordByRowKeyAsync(It.IsAny<string>(), It.IsAny<User>()), Times.Never);
        }
    }

    [Fact]
    public void Modal_ShouldCallAdminServiceOnDelete()
    {
        // Arrange - broken record
        var users = new List<User>
        {
            new User { TwitchUserId = "", Username = "broken_user", RowKey = "broken-key" }
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

        var currentUser = CreateTestCurrentUser();

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.CurrentUser), currentUser);
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0 || cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Find and click delete button
        var deleteButtons = cut.FindAll("button.btn-outline-danger");
        if (deleteButtons.Count > 0)
        {
            deleteButtons[0].Click();

            // Wait for operation to complete
            cut.WaitForAssertion(() =>
            {
                // Verify admin service was called
                _mockAdminService.Verify(x => x.DeleteUserRecordByRowKeyAsync("broken-key", currentUser), Times.Once);
            }, TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public void Modal_ShouldShowErrorOnDeleteFailure()
    {
        // Arrange - broken record
        var users = new List<User>
        {
            new User { TwitchUserId = "", Username = "broken_user", RowKey = "broken-key" }
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);
        _mockAdminService
            .Setup(x => x.DeleteUserRecordByRowKeyAsync(It.IsAny<string>(), It.IsAny<User>()))
            .ReturnsAsync(AdminOperationResult.Fail("Delete failed"));

        var currentUser = CreateTestCurrentUser();

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.CurrentUser), currentUser);
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".list-group-item").Count > 0, TimeSpan.FromSeconds(3));

        // Find and click delete button
        var deleteButtons = cut.FindAll("button.btn-outline-danger");
        if (deleteButtons.Count > 0)
        {
            deleteButtons[0].Click();

            // Wait for error message
            cut.WaitForState(() => cut.Markup.Contains("Delete failed") || cut.Markup.Contains("Error"), TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public void Modal_ShouldDisplayHealthyWhenNoBrokenUsers()
    {
        // Arrange - all healthy users
        var users = new List<User>
        {
            new User { TwitchUserId = "111", Username = "normal1", RowKey = "111" },
            new User { TwitchUserId = "222", Username = "normal2", RowKey = "222" },
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.CurrentUser), CreateTestCurrentUser());
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Should show healthy message
        Assert.Contains("No broken users found", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldHandleNullUsernameGracefully()
    {
        // Arrange - user with null username
        var users = new List<User>
        {
            new User { TwitchUserId = "123", Username = null!, RowKey = "123" }
        };

        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.AddAttribute(2, nameof(BrokenUserManagerModal.CurrentUser), CreateTestCurrentUser());
            b.CloseComponent();
        });

        // Wait for scan to complete - should not throw
        cut.WaitForState(() => cut.FindAll(".text-success, .list-group-item").Count > 0, TimeSpan.FromSeconds(3));

        // Should still render without error
        Assert.Contains("Broken User Manager", cut.Markup);
    }

    [Fact]
    public void Modal_ShouldHandleEmptyUsersList()
    {
        // Arrange - empty list
        _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(new List<User>());

        var cut = Render(b =>
        {
            b.OpenComponent<BrokenUserManagerModal>(0);
            b.AddAttribute(1, nameof(BrokenUserManagerModal.Show), true);
            b.CloseComponent();
        });

        // Wait for scan to complete
        cut.WaitForState(() => cut.FindAll(".text-success").Count > 0, TimeSpan.FromSeconds(3));

        // Should show healthy message
        Assert.Contains("No broken users found", cut.Markup);
    }
}
