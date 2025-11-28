using OmniForge.Core.Entities;

namespace OmniForge.Web.Services
{
    /// <summary>
    /// Service interface for admin operations that enforces proper authorization.
    /// This ensures all admin operations go through proper role-based access control.
    /// </summary>
    public interface IAdminService
    {
        /// <summary>
        /// Deletes a user with proper admin authorization checks.
        /// </summary>
        /// <param name="userId">The user ID to delete</param>
        /// <param name="currentUser">The current authenticated user making the request</param>
        /// <returns>A result indicating success or failure with message</returns>
        Task<AdminOperationResult> DeleteUserAsync(string userId, User currentUser);

        /// <summary>
        /// Validates if the current user can delete the target user.
        /// </summary>
        /// <param name="targetUserId">The user ID to check for deletion</param>
        /// <param name="currentUser">The current authenticated user</param>
        /// <returns>True if deletion is allowed, false otherwise</returns>
        bool CanDeleteUser(string targetUserId, User currentUser);
    }

    public class AdminOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public static AdminOperationResult Ok(string message = "Operation completed successfully")
            => new() { Success = true, Message = message };

        public static AdminOperationResult Fail(string message)
            => new() { Success = false, Message = message };
    }
}
