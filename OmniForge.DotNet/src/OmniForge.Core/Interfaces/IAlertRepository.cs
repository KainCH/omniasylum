using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IAlertRepository
    {
        Task<Alert?> GetAlertAsync(string userId, string alertId);
        Task<IEnumerable<Alert>> GetAlertsAsync(string userId);
        Task SaveAlertAsync(Alert alert);
        Task DeleteAlertAsync(string userId, string alertId);

        Task<Dictionary<string, string>> GetEventMappingsAsync(string userId);
        Task SaveEventMappingsAsync(string userId, Dictionary<string, string> mappings);
    }
}
