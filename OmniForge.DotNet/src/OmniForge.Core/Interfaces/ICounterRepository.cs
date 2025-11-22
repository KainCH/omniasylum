using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ICounterRepository
    {
        Task<Counter> GetCountersAsync(string twitchUserId);
        Task SaveCountersAsync(Counter counter);
    }
}
