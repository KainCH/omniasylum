using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IGameContextRepository
    {
        Task InitializeAsync();
        Task<GameContext?> GetAsync(string userId);
        Task SaveAsync(GameContext context);
        Task ClearAsync(string userId);
    }
}
