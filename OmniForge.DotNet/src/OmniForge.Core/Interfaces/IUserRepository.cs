using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetUserAsync(string twitchUserId);
        Task SaveUserAsync(User user);
        Task DeleteUserAsync(string twitchUserId);
        /// <summary>
        /// Deletes a user record from Azure Table Storage using the RowKey.
        /// Used for cleaning up orphaned or broken user records where TwitchUserId may be empty.
        /// </summary>
        /// <param name="rowKey">The Azure Table Storage RowKey of the record to delete</param>
        Task DeleteUserRecordByRowKeyAsync(string rowKey);
        Task<IEnumerable<User>> GetAllUsersAsync();
        Task InitializeAsync();
        Task<ChatCommandConfiguration> GetChatCommandsConfigAsync(string userId);
        Task SaveChatCommandsConfigAsync(string userId, ChatCommandConfiguration config);
    }
}
