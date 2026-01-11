using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ICounterRequestRepository
    {
        Task InitializeAsync();
        Task CreateAsync(CounterRequest request);
        Task<CounterRequest?> GetAsync(string requestId);
        Task<IEnumerable<CounterRequest>> ListAsync(string? status = null);
        Task UpdateStatusAsync(string requestId, string status, string? adminNotes = null);
    }
}
