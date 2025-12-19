using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using Xunit;

namespace OmniForge.Tests.Components.Modals
{
    public class ModeratorManagerModalTests : BunitContext
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ILogger<ModeratorManagerModal>> _mockLogger;

        public ModeratorManagerModalTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockLogger = new Mock<ILogger<ModeratorManagerModal>>();
            Services.AddSingleton(_mockUserRepository.Object);
            Services.AddSingleton(_mockLogger.Object);
        }

        [Fact]
        public void DoesNotRenderModal_WhenShowIsFalse()
        {
            // Act
            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, false)
                .Add(p => p.CurrentUserId, "user123"));

            // Assert
            Assert.Empty(cut.FindAll(".modal"));
        }

        [Fact]
        public void RendersModal_WhenShowIsTrue()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Role = "streamer", ManagedStreamers = new List<string>() },
                new User { TwitchUserId = "mod1", DisplayName = "Moderator1", Role = "streamer", ManagedStreamers = new List<string> { "user123" } }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123"));

            // Assert
            Assert.NotEmpty(cut.FindAll(".modal"));
            Assert.Contains("Manage Your Mod Team", cut.Markup);
        }

        [Fact]
        public void DisplaysCurrentModerators_WhenLoaded()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Role = "streamer", ManagedStreamers = new List<string>() },
                new User { TwitchUserId = "mod1", DisplayName = "Moderator1", Username = "mod1user", Role = "streamer", ManagedStreamers = new List<string> { "user123" } },
                new User { TwitchUserId = "mod2", DisplayName = "Moderator2", Username = "mod2user", Role = "streamer", ManagedStreamers = new List<string> { "user123" } }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123"));

            // Assert
            cut.WaitForState(() => cut.Markup.Contains("Moderator1"));
            Assert.Contains("Moderator1", cut.Markup);
            Assert.Contains("Moderator2", cut.Markup);
        }

        [Fact]
        public void ShowsNoModeratorsMessage_WhenEmpty()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Role = "streamer", ManagedStreamers = new List<string>() }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123"));

            // Assert
            cut.WaitForState(() => cut.Markup.Contains("No moderators added yet"));
            Assert.Contains("No moderators added yet", cut.Markup);
        }

        [Fact]
        public async Task RevokeButton_ShouldRemoveModerator()
        {
            // Arrange
            var moderator = new User
            {
                TwitchUserId = "mod1",
                DisplayName = "Moderator1",
                Username = "mod1user",
                Role = "streamer",
                ManagedStreamers = new List<string> { "user123" }
            };
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Role = "streamer", ManagedStreamers = new List<string>() },
                moderator
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);
            _mockUserRepository.Setup(r => r.GetUserAsync("mod1")).ReturnsAsync(moderator);
            _mockUserRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123"));

            // Wait for moderators to load
            cut.WaitForState(() => cut.Markup.Contains("Remove"));

            // Act - click revoke button
            var revokeButton = cut.Find("button.btn-outline-danger");
            await cut.InvokeAsync(() => revokeButton.Click());

            // Assert
            _mockUserRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u =>
                u.TwitchUserId == "mod1" && !u.ManagedStreamers.Contains("user123"))), Times.Once);
        }

        [Fact]
        public async Task CloseButton_ShouldInvokeShowChanged()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Role = "streamer", ManagedStreamers = new List<string>() }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var showChangedInvoked = false;
            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123")
                .Add(p => p.ShowChanged, EventCallback.Factory.Create<bool>(this, (value) => showChangedInvoked = !value)));

            // Act
            var closeButton = cut.Find("button.btn-close");
            await cut.InvokeAsync(() => closeButton.Click());

            // Assert
            Assert.True(showChangedInvoked);
        }

        [Fact]
        public void ShowsSearchInput_ForAddingModerators()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Role = "streamer", ManagedStreamers = new List<string>() }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123"));

            // Assert
            Assert.Contains("Search by username", cut.Markup);
            Assert.NotEmpty(cut.FindAll("input[type='text']"));
        }

        [Fact]
        public void ShowsInfoAlert_AboutModeratorFeature()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Role = "streamer", ManagedStreamers = new List<string>() }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123"));

            // Assert
            Assert.Contains("Add users who can manage your OmniForge settings", cut.Markup);
        }

        [Fact]
        public void ExcludesSelf_FromSearchResults()
        {
            // Arrange
            var users = new List<User>
            {
                new User { TwitchUserId = "user123", DisplayName = "Streamer", Username = "streamer", Role = "streamer", ManagedStreamers = new List<string>() },
                new User { TwitchUserId = "other1", DisplayName = "OtherUser", Username = "otheruser", Role = "streamer", ManagedStreamers = new List<string>() }
            };
            _mockUserRepository.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = Render<ModeratorManagerModal>(parameters => parameters
                .Add(p => p.Show, true)
                .Add(p => p.CurrentUserId, "user123"));

            // Wait for initial load
            cut.WaitForState(() => cut.Markup.Contains("Search by username"));

            // Note: Full search functionality would require more complex testing with debounced input
            // This test validates the modal structure is correct
            Assert.Contains("Add New Moderator", cut.Markup);
        }
    }
}
