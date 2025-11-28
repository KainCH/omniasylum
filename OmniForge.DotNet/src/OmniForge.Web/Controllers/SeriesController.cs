using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System;
using System.Linq;
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

        /// <summary>
        /// List all series saves for the authenticated user
        /// </summary>
        [HttpGet("list")]
        public async Task<IActionResult> GetSeries()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var seriesList = await _seriesRepository.GetSeriesAsync(userId);

            // Map to frontend-expected format with seriesId instead of Id
            var saves = seriesList.Select(s => new
            {
                seriesId = s.Id,
                seriesName = s.Name,
                description = s.Description,
                deaths = s.Snapshot.Deaths,
                swears = s.Snapshot.Swears,
                screams = s.Snapshot.Screams,
                bits = s.Snapshot.Bits,
                customCounters = s.Snapshot.CustomCounters,
                savedAt = s.LastUpdated,
                createdAt = s.CreatedAt,
                isActive = s.IsActive
            });

            return Ok(new { count = saves.Count(), saves });
        }

        /// <summary>
        /// Save current counter state as a new series or update existing
        /// If seriesId is provided, updates that series; otherwise creates new
        /// </summary>
        [HttpPost("save")]
        public async Task<IActionResult> SaveSeries([FromBody] SaveSeriesRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.SeriesName))
                return BadRequest(new { error = "Series name is required" });

            if (request.SeriesName.Length > 100)
                return BadRequest(new { error = "Series name must be 100 characters or less" });

            var currentCounters = await _counterRepository.GetCountersAsync(userId);
            var snapshot = currentCounters ?? new Counter { TwitchUserId = userId };

            Series series;
            bool isUpdate = false;

            // Check if we're updating an existing series
            if (!string.IsNullOrEmpty(request.SeriesId))
            {
                var existingSeries = await _seriesRepository.GetSeriesByIdAsync(userId, request.SeriesId);
                if (existingSeries == null)
                    return NotFound(new { error = "Series save not found" });

                // Update existing series with current counter values
                existingSeries.Name = request.SeriesName.Trim();
                existingSeries.Description = request.Description?.Trim() ?? string.Empty;
                existingSeries.Snapshot = snapshot;
                existingSeries.LastUpdated = DateTimeOffset.UtcNow;

                await _seriesRepository.UpdateSeriesAsync(existingSeries);
                series = existingSeries;
                isUpdate = true;
            }
            else
            {
                // Create new series
                series = new Series
                {
                    UserId = userId,
                    Name = request.SeriesName.Trim(),
                    Description = request.Description?.Trim() ?? string.Empty,
                    Snapshot = snapshot,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdated = DateTimeOffset.UtcNow,
                    IsActive = false
                };

                await _seriesRepository.CreateSeriesAsync(series);
            }

            return Ok(new
            {
                message = isUpdate ? "Series updated successfully" : "Series saved successfully",
                save = new
                {
                    seriesId = series.Id,
                    seriesName = series.Name,
                    description = series.Description,
                    deaths = series.Snapshot.Deaths,
                    swears = series.Snapshot.Swears,
                    screams = series.Snapshot.Screams,
                    bits = series.Snapshot.Bits,
                    savedAt = series.LastUpdated
                }
            });
        }

        /// <summary>
        /// Load a series save state - replaces current counters with saved values
        /// </summary>
        [HttpPost("load")]
        public async Task<IActionResult> LoadSeries([FromBody] LoadSeriesRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrEmpty(request.SeriesId))
                return BadRequest(new { error = "Series ID is required" });

            var series = await _seriesRepository.GetSeriesByIdAsync(userId, request.SeriesId);
            if (series == null) return NotFound(new { error = "Series save not found" });

            // Create a new counter with the snapshot values, ensuring user ID is set
            var restoredCounter = new Counter
            {
                TwitchUserId = userId,
                Deaths = series.Snapshot.Deaths,
                Swears = series.Snapshot.Swears,
                Screams = series.Snapshot.Screams,
                Bits = series.Snapshot.Bits,
                CustomCounters = series.Snapshot.CustomCounters ?? new System.Collections.Generic.Dictionary<string, int>(),
                LastUpdated = DateTimeOffset.UtcNow,
                StreamStarted = null, // Don't restore stream state
                LastNotifiedStreamId = null
            };

            // Save the restored counters (this overwrites current values)
            await _counterRepository.SaveCountersAsync(restoredCounter);

            // Notify overlay of the counter update
            await _overlayNotifier.NotifyCounterUpdateAsync(userId, restoredCounter);

            // Mark this series as active (optional tracking)
            series.IsActive = true;
            series.LastUpdated = DateTimeOffset.UtcNow;
            await _seriesRepository.UpdateSeriesAsync(series);

            return Ok(new
            {
                message = "Series loaded successfully",
                counters = new
                {
                    deaths = restoredCounter.Deaths,
                    swears = restoredCounter.Swears,
                    screams = restoredCounter.Screams,
                    bits = restoredCounter.Bits,
                    lastUpdated = restoredCounter.LastUpdated
                },
                seriesInfo = new
                {
                    seriesName = series.Name,
                    description = series.Description,
                    savedAt = series.LastUpdated
                }
            });
        }

        /// <summary>
        /// Update an existing series with current counter values (overwrite)
        /// </summary>
        [HttpPut("{seriesId}")]
        public async Task<IActionResult> UpdateSeries(string seriesId, [FromBody] UpdateSeriesRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var existingSeries = await _seriesRepository.GetSeriesByIdAsync(userId, seriesId);
            if (existingSeries == null)
                return NotFound(new { error = "Series save not found" });

            // Get current counter values to save as the new snapshot
            var currentCounters = await _counterRepository.GetCountersAsync(userId);

            // Update the series
            if (!string.IsNullOrWhiteSpace(request.SeriesName))
                existingSeries.Name = request.SeriesName.Trim();

            if (request.Description != null)
                existingSeries.Description = request.Description.Trim();

            // Update snapshot with current counters
            existingSeries.Snapshot = currentCounters ?? new Counter { TwitchUserId = userId };
            existingSeries.LastUpdated = DateTimeOffset.UtcNow;

            await _seriesRepository.UpdateSeriesAsync(existingSeries);

            return Ok(new
            {
                message = "Series updated successfully",
                save = new
                {
                    seriesId = existingSeries.Id,
                    seriesName = existingSeries.Name,
                    description = existingSeries.Description,
                    deaths = existingSeries.Snapshot.Deaths,
                    swears = existingSeries.Snapshot.Swears,
                    screams = existingSeries.Snapshot.Screams,
                    bits = existingSeries.Snapshot.Bits,
                    savedAt = existingSeries.LastUpdated
                }
            });
        }

        /// <summary>
        /// Delete a series save
        /// </summary>
        [HttpDelete("{seriesId}")]
        public async Task<IActionResult> DeleteSeries(string seriesId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Verify series exists before deleting
            var existingSeries = await _seriesRepository.GetSeriesByIdAsync(userId, seriesId);
            if (existingSeries == null)
                return NotFound(new { error = "Series save not found" });

            await _seriesRepository.DeleteSeriesAsync(userId, seriesId);

            return Ok(new { message = "Series save deleted successfully" });
        }
    }

    public class SaveSeriesRequest
    {
        public string? SeriesId { get; set; }
        public string SeriesName { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class LoadSeriesRequest
    {
        public string SeriesId { get; set; } = string.Empty;
    }

    public class UpdateSeriesRequest
    {
        public string? SeriesName { get; set; }
        public string? Description { get; set; }
    }
}
