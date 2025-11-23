using OmniForge.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface ISeriesRepository
    {
        Task InitializeAsync();
        Task<IEnumerable<Series>> GetSeriesAsync(string userId);
        Task<Series?> GetSeriesByIdAsync(string userId, string seriesId);
        Task CreateSeriesAsync(Series series);
        Task UpdateSeriesAsync(Series series);
        Task DeleteSeriesAsync(string userId, string seriesId);
    }
}
