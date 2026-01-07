using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IBotCredentialRepository
    {
        Task<BotCredentials?> GetAsync();
        Task SaveAsync(BotCredentials credentials);
    }
}
