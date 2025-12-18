using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IAlertEventRouter
    {
        Task RouteAsync(string userId, string eventKey, string defaultAlertType, object data);
    }
}
