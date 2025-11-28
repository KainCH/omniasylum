using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests.Services;

public class AdminServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<ILogger<AdminService>> _mockLogger;
    private readonly AdminService _adminService;

    public AdminServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockLogger = new Mock<ILogger<AdminService>>();
        _adminService = new AdminService(_mockUserRepository.Object, _mockLogger.Object);
    }

    #region CanDeleteUser Tests

    [Fact]
    public void CanDeleteUser_ReturnsFalse_WhenUserIsNotAdmin()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Role = "streamer" };

        // Act
        var result = _adminService.CanDeleteUser("456", currentUser);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanDeleteUser_ReturnsFalse_WhenDeletingSelf()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Role = "admin" };

        // Act
        var result = _adminService.CanDeleteUser("123", currentUser);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanDeleteUser_ReturnsTrue_WhenAdminDeletingOtherUser()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Role = "admin" };

        // Act
        var result = _adminService.CanDeleteUser("456", currentUser);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region DeleteUserAsync Tests

    [Fact]
    public async Task DeleteUserAsync_ReturnsFail_WhenNotAdmin()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "user1", Role = "streamer" };

        // Act
        var result = await _adminService.DeleteUserAsync("456", currentUser);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Only administrators can delete users", result.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsFail_WhenDeletingSelf()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin" };

        // Act
        var result = await _adminService.DeleteUserAsync("123", currentUser);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Cannot delete your own account", result.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsFail_WhenDeletingAnotherAdmin()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin" };
        var targetUser = new User { TwitchUserId = "456", Username = "admin2", Role = "admin" };

        _mockUserRepository.Setup(x => x.GetUserAsync("456")).ReturnsAsync(targetUser);

        // Act
        var result = await _adminService.DeleteUserAsync("456", currentUser);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Cannot delete admin accounts", result.Message);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsSuccess_WhenDeletingRegularUser()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin" };
        var targetUser = new User { TwitchUserId = "456", Username = "user1", DisplayName = "User One", Role = "streamer" };

        _mockUserRepository.Setup(x => x.GetUserAsync("456")).ReturnsAsync(targetUser);

        // Act
        var result = await _adminService.DeleteUserAsync("456", currentUser);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("User One", result.Message);
        _mockUserRepository.Verify(x => x.DeleteUserAsync("456"), Times.Once);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsSuccess_WhenDeletingOrphanedRecord()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin" };

        _mockUserRepository.Setup(x => x.GetUserAsync("456")).ReturnsAsync((User?)null);

        // Act
        var result = await _adminService.DeleteUserAsync("456", currentUser);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Orphaned", result.Message);
        _mockUserRepository.Verify(x => x.DeleteUserAsync("456"), Times.Once);
    }

    #endregion

    #region DeleteUserRecordByRowKeyAsync Tests

    [Fact]
    public async Task DeleteUserRecordByRowKeyAsync_ReturnsFail_WhenNotAdmin()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "user1", Role = "streamer" };

        // Act
        var result = await _adminService.DeleteUserRecordByRowKeyAsync("rowkey-456", currentUser);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Only administrators can delete user records", result.Message);
    }

    [Fact]
    public async Task DeleteUserRecordByRowKeyAsync_ReturnsFail_WhenDeletingSelfByTwitchUserId()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin", RowKey = "rowkey-123" };

        // Act
        var result = await _adminService.DeleteUserRecordByRowKeyAsync("123", currentUser);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Cannot delete your own account", result.Message);
    }

    [Fact]
    public async Task DeleteUserRecordByRowKeyAsync_ReturnsFail_WhenDeletingSelfByRowKey()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin", RowKey = "rowkey-123" };

        // Act
        var result = await _adminService.DeleteUserRecordByRowKeyAsync("rowkey-123", currentUser);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Cannot delete your own account", result.Message);
    }

    [Fact]
    public async Task DeleteUserRecordByRowKeyAsync_ReturnsSuccess_WhenDeletingBrokenUser()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin", RowKey = "rowkey-123" };

        // Act
        var result = await _adminService.DeleteUserRecordByRowKeyAsync("rowkey-456", currentUser);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Broken user record deleted", result.Message);
        _mockUserRepository.Verify(x => x.DeleteUserRecordByRowKeyAsync("rowkey-456"), Times.Once);
    }

    [Fact]
    public async Task DeleteUserRecordByRowKeyAsync_ReturnsFail_WhenExceptionThrown()
    {
        // Arrange
        var currentUser = new User { TwitchUserId = "123", Username = "admin1", Role = "admin", RowKey = "rowkey-123" };

        _mockUserRepository
            .Setup(x => x.DeleteUserRecordByRowKeyAsync("rowkey-456"))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _adminService.DeleteUserRecordByRowKeyAsync("rowkey-456", currentUser);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Database error", result.Message);
    }

    #endregion
}
