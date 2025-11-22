using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ICounterRepository
    {
        Task<Counter> GetCountersAsync(string userId);
        Task SaveCountersAsync(Counter counter);
        Task<Counter> IncrementCounterAsync(string userId, string counterType);
        Task<Counter> DecrementCounterAsync(string userId, string counterType);
    }
}
