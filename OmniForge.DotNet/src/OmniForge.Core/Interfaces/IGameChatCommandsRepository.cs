using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IGameChatCommandsRepository
    {
        Task InitializeAsync();
        Task<ChatCommandConfiguration?> GetAsync(string userId, string gameId);
        Task SaveAsync(string userId, string gameId, ChatCommandConfiguration config);
    }
}
