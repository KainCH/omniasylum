using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IBroadcastProfileRepository
    {
        Task InitializeAsync();
        Task<BroadcastProfile?> GetAsync(string userId, string profileId);
        Task<List<BroadcastProfile>> GetAllAsync(string userId);
        Task SaveAsync(BroadcastProfile profile);
        Task DeleteAsync(string userId, string profileId);
    }
}
