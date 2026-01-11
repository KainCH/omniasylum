using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ICounterLibraryRepository
    {
        Task InitializeAsync();
        Task<IEnumerable<CounterLibraryItem>> ListAsync();
        Task<CounterLibraryItem?> GetAsync(string counterId);
        Task UpsertAsync(CounterLibraryItem item);
    }
}
