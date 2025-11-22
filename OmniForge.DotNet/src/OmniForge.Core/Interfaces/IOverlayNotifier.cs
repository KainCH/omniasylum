using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IOverlayNotifier
    {
        Task NotifyCounterUpdateAsync(string userId, Counter counter);
    }
}
