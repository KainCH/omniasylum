using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Components.Modals
{
    public class PermissionManagerModalTests : BunitContext
    {
        private readonly Mock<IUserRepository> _mockUserRepository;

        public PermissionManagerModalTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            Services.AddSingleton(_mockUserRepository.Object);
        }

        [Fact]
        public void DoesNotRenderModal_WhenShowIsFalse()
        {
            // Act
            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, false));

            // Assert
            Assert.Empty(cut.FindAll(".modal"));
        }

        [Fact]
        public void RendersModal_WhenShowIsTrue()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "User1", Role = "admin" },
                new User { TwitchUserId = "2", DisplayName = "User2", Role = "streamer" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Assert
            Assert.NotEmpty(cut.FindAll(".modal"));
            Assert.Contains("Permission Manager", cut.Markup);
        }

        [Fact]
        public void DisplaysAllUsers_InDropdown()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "User1", Role = "admin" },
                new User { TwitchUserId = "2", DisplayName = "User2", Role = "streamer" },
                new User { TwitchUserId = "3", DisplayName = "User3", Role = "moderator" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Assert
            var select = cut.Find("select.form-select");
            var options = cut.FindAll("option");
            Assert.Contains(options, o => o.TextContent.Contains("User1"));
            Assert.Contains(options, o => o.TextContent.Contains("User2"));
            Assert.Contains(options, o => o.TextContent.Contains("User3"));
        }

        [Fact]
        public void UsesProvidedUsers_WhenUsersParameterSet()
        {
            // Arrange
            var providedUsers = new List<User>
            {
                new User { TwitchUserId = "10", DisplayName = "ProvidedUser", Role = "admin" }
            };

            // Act
            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.Users, providedUsers));

            // Assert - should use provided users, not call repository
            _mockUserRepository.Verify(r => r.GetAllUsersAsync(), Times.Never);
            Assert.Contains("ProvidedUser", cut.Markup);
        }

        [Fact]
        public async Task CloseButton_ShouldInvokeShowChanged()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "User1", Role = "admin" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            bool showChangedValue = true;
            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.ShowChanged, EventCallback.Factory.Create<bool>(this, (v) => showChangedValue = v)));

            // Act
            var closeButton = cut.Find(".btn-close");
            await cut.InvokeAsync(() => closeButton.Click());

            // Assert
            Assert.False(showChangedValue);
        }

        [Fact]
        public async Task CloseFooterButton_ShouldInvokeShowChanged()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "User1", Role = "admin" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            bool showChangedValue = true;
            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.ShowChanged, EventCallback.Factory.Create<bool>(this, (v) => showChangedValue = v)));

            // Act
            var closeButton = cut.Find(".modal-footer .btn-secondary");
            await cut.InvokeAsync(() => closeButton.Click());

            // Assert
            Assert.False(showChangedValue);
        }

        [Fact]
        public void ShowsManagedStreamers_WhenManagerSelected()
        {
            // Arrange
            var users = new List<User>
            {
                new User
                {
                    TwitchUserId = "1",
                    DisplayName = "Manager1",
                    Role = "admin",
                    ManagedStreamers = new List<string> { "2" }
                },
                new User { TwitchUserId = "2", DisplayName = "Streamer2", Role = "streamer" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Act - select a manager
            var select = cut.Find("select.form-select");
            select.Change("1");

            // Assert
            cut.WaitForState(() => cut.Markup.Contains("Managed Streamers for Manager1"));
            Assert.Contains("Streamer2", cut.Markup);
        }

        [Fact]
        public void ShowsNoManagedStreamersMessage_WhenEmpty()
        {
            // Arrange
            var users = new List<User>
            {
                new User
                {
                    TwitchUserId = "1",
                    DisplayName = "Manager1",
                    Role = "admin",
                    ManagedStreamers = null!
                }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Act - select a manager
            var select = cut.Find("select.form-select");
            select.Change("1");

            // Assert
            cut.WaitForState(() => cut.Markup.Contains("No managed streamers"));
        }

        [Fact]
        public async Task RevokeButton_ShouldRemoveStreamer()
        {
            // Arrange
            var manager = new User
            {
                TwitchUserId = "1",
                DisplayName = "Manager1",
                Role = "admin",
                ManagedStreamers = new List<string> { "2" }
            };
            var users = new List<User>
            {
                manager,
                new User { TwitchUserId = "2", DisplayName = "Streamer2", Role = "streamer" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Select manager
            var select = cut.Find("select.form-select");
            select.Change("1");
            cut.WaitForState(() => cut.Markup.Contains("Revoke"));

            // Act - click revoke
            var revokeButton = cut.Find(".btn-danger");
            await cut.InvokeAsync(() => revokeButton.Click());

            // Assert
            _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => !u.ManagedStreamers!.Contains("2"))), Times.Once);
        }

        [Fact]
        public async Task GrantAccessButton_ShouldAddStreamer()
        {
            // Arrange
            var manager = new User
            {
                TwitchUserId = "1",
                DisplayName = "Manager1",
                Role = "admin",
                ManagedStreamers = new List<string>()
            };
            var users = new List<User>
            {
                manager,
                new User { TwitchUserId = "2", DisplayName = "Streamer2", Role = "streamer" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Select manager
            var managerSelect = cut.Find("select.form-select");
            managerSelect.Change("1");
            cut.WaitForState(() => cut.Markup.Contains("Grant Access"));

            // Select streamer to add
            var streamerSelect = cut.FindAll("select.form-select").Last();
            streamerSelect.Change("2");

            // Act - click grant access
            var grantButton = cut.Find(".btn-success");
            await cut.InvokeAsync(() => grantButton.Click());

            // Assert
            _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u => u.ManagedStreamers!.Contains("2"))), Times.Once);
        }

        [Fact]
        public void GrantAccessButton_ShouldBeDisabled_WhenNoStreamerSelected()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "Manager1", Role = "admin", ManagedStreamers = new List<string>() },
                new User { TwitchUserId = "2", DisplayName = "Streamer2", Role = "streamer" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Select manager
            var managerSelect = cut.Find("select.form-select");
            managerSelect.Change("1");
            cut.WaitForState(() => cut.Markup.Contains("Grant Access"));

            // Assert - button should be disabled
            var grantButton = cut.Find(".btn-success");
            Assert.True(grantButton.HasAttribute("disabled"));
        }

        [Fact]
        public void ExcludesManager_FromStreamerDropdown()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "Manager1", Role = "admin", ManagedStreamers = new List<string>() },
                new User { TwitchUserId = "2", DisplayName = "Streamer2", Role = "streamer" },
                new User { TwitchUserId = "3", DisplayName = "Streamer3", Role = "streamer" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Select manager
            var managerSelect = cut.Find("select.form-select");
            managerSelect.Change("1");
            cut.WaitForState(() => cut.Markup.Contains("Grant Access"));

            // Assert - the streamer dropdown should not contain Manager1
            var streamerSelect = cut.FindAll("select.form-select").Last();
            var streamerOptions = streamerSelect.QuerySelectorAll("option");
            Assert.DoesNotContain(streamerOptions, o => o.GetAttribute("value") == "1");
        }

        [Fact]
        public void ExcludesAlreadyManagedStreamers_FromDropdown()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "Manager1", Role = "admin", ManagedStreamers = new List<string> { "2" } },
                new User { TwitchUserId = "2", DisplayName = "Streamer2", Role = "streamer" },
                new User { TwitchUserId = "3", DisplayName = "Streamer3", Role = "streamer" }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<PermissionManagerModal>(parameters => parameters
                .Add(p => p.Show, true));

            // Select manager
            var managerSelect = cut.Find("select.form-select");
            managerSelect.Change("1");
            cut.WaitForState(() => cut.Markup.Contains("Grant Access"));

            // Assert - the streamer dropdown should not contain already managed Streamer2
            var streamerSelect = cut.FindAll("select.form-select").Last();
            var streamerOptions = streamerSelect.QuerySelectorAll("option");
            Assert.DoesNotContain(streamerOptions, o => o.GetAttribute("value") == "2");
            Assert.Contains(streamerOptions, o => o.GetAttribute("value") == "3");
        }
    }
}
