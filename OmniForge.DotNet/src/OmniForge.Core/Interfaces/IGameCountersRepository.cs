using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IGameCountersRepository
    {
        Task InitializeAsync();
        Task<Counter?> GetAsync(string userId, string gameId);
        Task SaveAsync(string userId, string gameId, Counter counters);
    }
}
