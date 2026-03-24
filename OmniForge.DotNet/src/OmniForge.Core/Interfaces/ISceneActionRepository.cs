using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ISceneActionRepository
    {
        Task InitializeAsync();
        Task<SceneAction?> GetAsync(string userId, string sceneName);
        Task<List<SceneAction>> GetAllAsync(string userId);
        Task SaveAsync(SceneAction sceneAction);
        Task DeleteAsync(string userId, string sceneName);
    }
}
