using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IGameLibraryRepository
    {
        Task InitializeAsync();
        Task UpsertAsync(GameLibraryItem item);
        Task<GameLibraryItem?> GetAsync(string userId, string gameId);
        Task<IReadOnlyList<GameLibraryItem>> ListAsync(string userId, int take = 200);
    }
}
