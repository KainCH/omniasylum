using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IGameCounterSetupService
    {
        Task AddLibraryCounterToGameAsync(string userId, string gameId, string counterId);
    }
}
