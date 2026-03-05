using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ISceneRepository
    {
        Task InitializeAsync();
        Task<List<Scene>> GetScenesAsync(string userId);
        Task<Scene?> GetSceneAsync(string userId, string sceneName);
        Task SaveSceneAsync(Scene scene);
        Task DeleteSceneAsync(string userId, string sceneName);
    }
}
