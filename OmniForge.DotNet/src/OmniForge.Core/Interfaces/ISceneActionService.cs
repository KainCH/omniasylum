using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface ISceneActionService
    {
        Task HandleSceneChangedAsync(string userId, string newScene, string? previousScene);
    }
}
