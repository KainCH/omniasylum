using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ITemplateRepository
    {
        Task InitializeAsync();
        Task<Dictionary<string, Template>> GetAvailableTemplatesAsync();
        Task<Template?> GetUserCustomTemplateAsync(string userId);
        Task SaveUserCustomTemplateAsync(string userId, Template template);
    }
}
