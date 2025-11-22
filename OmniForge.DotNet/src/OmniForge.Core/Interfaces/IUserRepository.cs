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
        Task<IEnumerable<User>> GetAllUsersAsync();
    }
}
