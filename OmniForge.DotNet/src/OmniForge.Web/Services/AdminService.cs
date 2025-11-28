using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Services
{
    /// <summary>
    /// Service implementation for admin operations with proper authorization.
    /// All admin operations go through this service to ensure consistent authorization.
    /// </summary>
    public class AdminService : IAdminService
    {
        private readonly IUserRepository _userRepository;
        private readonly ILogger<AdminService> _logger;

        public AdminService(IUserRepository userRepository, ILogger<AdminService> logger)
        {
            _userRepository = userRepository;
            _logger = logger;
        }

        /// <inheritdoc />
        public bool CanDeleteUser(string targetUserId, User currentUser)
        {
            // Must be an admin
            if (currentUser.Role != "admin")
            {
                return false;
            }

            // Cannot delete yourself
            if (currentUser.TwitchUserId == targetUserId)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public async Task<AdminOperationResult> DeleteUserAsync(string userId, User currentUser)
        {
            // Verify current user is an admin
            if (currentUser.Role != "admin")
            {
                _logger.LogWarning("Non-admin user {Username} attempted to delete user {UserId}",
                    currentUser.Username, userId);
                return AdminOperationResult.Fail("Only administrators can delete users");
            }

            // Cannot delete yourself
            if (currentUser.TwitchUserId == userId)
            {
                _logger.LogWarning("Admin {Username} attempted to delete their own account", currentUser.Username);
                return AdminOperationResult.Fail("Cannot delete your own account");
            }

            // Get the target user to check their role
            var targetUser = await _userRepository.GetUserAsync(userId);

            // If user doesn't exist, allow deletion (cleanup orphaned data)
            if (targetUser == null)
            {
                _logger.LogInformation("Deleting non-existent/orphaned user record with ID {UserId}", userId);
                await _userRepository.DeleteUserAsync(userId);
                return AdminOperationResult.Ok("Orphaned user record deleted successfully");
            }

            // Cannot delete other admin accounts
            if (targetUser.Role == "admin")
            {
                _logger.LogWarning("Admin {Username} attempted to delete another admin {TargetUsername}",
                    currentUser.Username, targetUser.Username);
                return AdminOperationResult.Fail("Cannot delete admin accounts");
            }

            // Perform the deletion
            _logger.LogInformation("Admin {AdminUsername} deleting user {Username} ({UserId})",
                currentUser.Username, targetUser.Username, userId);

            await _userRepository.DeleteUserAsync(userId);

            return AdminOperationResult.Ok($"User {targetUser.DisplayName} deleted successfully");
        }

        /// <inheritdoc />
        /// <inheritdoc />
        public async Task<AdminOperationResult> DeleteUserRecordByRowKeyAsync(string rowKey, User currentUser)
        {
            // Verify current user is an admin
            if (currentUser.Role != "admin")
            {
                _logger.LogWarning("Non-admin user {Username} attempted to delete broken user record with RowKey {RowKey}",
                    currentUser.Username, rowKey);
                return AdminOperationResult.Fail("Only administrators can delete user records");
            }

            // Cannot delete your own record by RowKey
            if (currentUser.TwitchUserId == rowKey || currentUser.RowKey == rowKey)
            {
                _logger.LogWarning("Admin {Username} attempted to delete their own account via RowKey", currentUser.Username);
                return AdminOperationResult.Fail("Cannot delete your own account");
            }

            _logger.LogInformation("Admin {AdminUsername} deleting broken user record with RowKey {RowKey}",
                currentUser.Username, rowKey);

            try
            {
                await _userRepository.DeleteUserRecordByRowKeyAsync(rowKey);
                return AdminOperationResult.Ok("Broken user record deleted successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete user record with RowKey {RowKey}", rowKey);
                return AdminOperationResult.Fail($"Failed to delete user record: {ex.Message}");
            }
        }
    }
}
