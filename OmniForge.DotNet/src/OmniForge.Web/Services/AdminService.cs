using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

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
            if (string.IsNullOrWhiteSpace(userId))
            {
                return AdminOperationResult.Fail("User ID is required");
            }

            var safeUserId = userId!;

            // Verify current user is an admin
            if (currentUser.Role != "admin")
            {
                _logger.LogWarning("Non-admin user {Username} attempted to delete user {UserId}",
                    (currentUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return AdminOperationResult.Fail("Only administrators can delete users");
            }

            // Cannot delete yourself
            if (currentUser.TwitchUserId == safeUserId)
            {
                _logger.LogWarning("Admin {Username} attempted to delete their own account", (currentUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return AdminOperationResult.Fail("Cannot delete your own account");
            }

            // Get the target user to check their role
            var targetUser = await _userRepository.GetUserAsync(safeUserId);

            // If user doesn't exist, allow deletion (cleanup orphaned data)
            if (targetUser == null)
            {
                _logger.LogInformation("Deleting non-existent/orphaned user record with ID {UserId}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                await _userRepository.DeleteUserAsync(safeUserId!);
                return AdminOperationResult.Ok("Orphaned user record deleted successfully");
            }

            // Cannot delete other admin accounts
            if (targetUser.Role == "admin")
            {
                _logger.LogWarning("Admin {Username} attempted to delete another admin {TargetUsername}",
                    (currentUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (targetUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return AdminOperationResult.Fail("Cannot delete admin accounts");
            }

            // Perform the deletion
            _logger.LogInformation("Admin {AdminUsername} deleting user {Username} ({UserId})",
                (currentUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (targetUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));

            await _userRepository.DeleteUserAsync(safeUserId!);

            return AdminOperationResult.Ok($"User {targetUser.DisplayName} deleted successfully");
        }

        /// <inheritdoc />
        /// <inheritdoc />
        public async Task<AdminOperationResult> DeleteUserRecordByRowKeyAsync(string rowKey, User currentUser)
        {
            if (string.IsNullOrWhiteSpace(rowKey))
            {
                return AdminOperationResult.Fail("RowKey is required");
            }

            var safeRowKey = rowKey!;

            // Verify current user is an admin
            if (currentUser.Role != "admin")
            {
                _logger.LogWarning("Non-admin user {Username} attempted to delete broken user record with RowKey {RowKey}",
                    (currentUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (safeRowKey ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return AdminOperationResult.Fail("Only administrators can delete user records");
            }

            // Cannot delete your own record by RowKey
            if (currentUser.TwitchUserId == safeRowKey || currentUser.RowKey == safeRowKey)
            {
                _logger.LogWarning("Admin {Username} attempted to delete their own account via RowKey", (currentUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return AdminOperationResult.Fail("Cannot delete your own account");
            }

            _logger.LogInformation("Admin {AdminUsername} deleting broken user record with RowKey {RowKey}",
                (currentUser.Username ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (safeRowKey ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));

            try
            {
                await _userRepository.DeleteUserRecordByRowKeyAsync(safeRowKey!);
                return AdminOperationResult.Ok("Broken user record deleted successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete user record with RowKey {RowKey}", (safeRowKey ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return AdminOperationResult.Fail($"Failed to delete user record: {ex.Message}");
            }
        }
    }
}
