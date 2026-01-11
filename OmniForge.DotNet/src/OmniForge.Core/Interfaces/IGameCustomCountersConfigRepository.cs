using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IGameCustomCountersConfigRepository
    {
        Task InitializeAsync();
        Task<CustomCounterConfiguration?> GetAsync(string userId, string gameId);
        Task SaveAsync(string userId, string gameId, CustomCounterConfiguration config);
    }
}
