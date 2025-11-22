using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace OmniForge.Web.Controllers
{
    [Route("api/counters/series")]
    [ApiController]
    [Authorize]
    public class SeriesController : ControllerBase
    {
        private readonly ISeriesRepository _seriesRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly IOverlayNotifier _overlayNotifier;

        public SeriesController(
            ISeriesRepository seriesRepository,
            ICounterRepository counterRepository,
            IOverlayNotifier overlayNotifier)
        {
            _seriesRepository = seriesRepository;
            _counterRepository = counterRepository;
            _overlayNotifier = overlayNotifier;
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetSeries()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var series = await _seriesRepository.GetSeriesAsync(userId);
            return Ok(new { saves = series });
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveSeries([FromBody] SaveSeriesRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var currentCounters = await _counterRepository.GetCountersAsync(userId);

            var series = new Series
            {
                UserId = userId,
                Name = request.SeriesName,
                Description = request.Description,
                Snapshot = currentCounters,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdated = DateTimeOffset.UtcNow
            };

            await _seriesRepository.CreateSeriesAsync(series);

            return Ok(new { message = "Series saved successfully", series });
        }

        [HttpPost("{seriesId}/load")]
        public async Task<IActionResult> LoadSeries(string seriesId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var series = await _seriesRepository.GetSeriesByIdAsync(userId, seriesId);
            if (series == null) return NotFound("Series not found");

            // Ensure the snapshot belongs to the current user
            series.Snapshot.TwitchUserId = userId;

            await _counterRepository.SaveCountersAsync(series.Snapshot);

            // Notify overlay
            await _overlayNotifier.NotifyCounterUpdateAsync(userId, series.Snapshot);

            return Ok(new { message = "Series loaded successfully" });
        }

        [HttpDelete("{seriesId}")]
        public async Task<IActionResult> DeleteSeries(string seriesId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _seriesRepository.DeleteSeriesAsync(userId, seriesId);
            return Ok(new { message = "Series deleted successfully" });
        }
    }

    public class SaveSeriesRequest
    {
        public string SeriesName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
