using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ICounterRepository
    {
        Task InitializeAsync();
        Task<Counter> GetCountersAsync(string userId);
        Task SaveCountersAsync(Counter counter);
        Task<Counter> IncrementCounterAsync(string userId, string counterType, int amount = 1);
        Task<Counter> DecrementCounterAsync(string userId, string counterType, int amount = 1);
        Task<Counter> ResetCounterAsync(string userId, string counterType);
        Task<CustomCounterConfiguration> GetCustomCountersConfigAsync(string userId);
        Task SaveCustomCountersConfigAsync(string userId, CustomCounterConfiguration config);
    }
}
