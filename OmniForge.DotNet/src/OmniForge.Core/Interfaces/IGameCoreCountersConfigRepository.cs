using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IGameCoreCountersConfigRepository
    {
        Task InitializeAsync();
        Task<GameCoreCountersConfig?> GetAsync(string userId, string gameId);
        Task SaveAsync(string userId, string gameId, GameCoreCountersConfig config);
    }
}
