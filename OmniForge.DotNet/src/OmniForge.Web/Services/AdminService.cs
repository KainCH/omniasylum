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
    }
}
