using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Constants;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/alerts")]
    public class AlertController : ControllerBase
    {
        private readonly IAlertRepository _alertRepository;
        private readonly IUserRepository _userRepository;

        public AlertController(IAlertRepository alertRepository, IUserRepository userRepository)
        {
            _alertRepository = alertRepository;
            _userRepository = userRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetAlerts()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            if (!user.Features.StreamAlerts)
            {
                return Forbid("Stream alerts feature not enabled");
            }

            // Initialize default alerts if user has none
            var existingAlerts = await _alertRepository.GetAlertsAsync(userId);
            if (!existingAlerts.Any())
            {
                var defaultTemplates = AlertTemplates.GetDefaultTemplates();
                foreach (var template in defaultTemplates)
                {
                    template.UserId = userId;
                    template.Id = $"{userId}_{template.Id}";
                    await _alertRepository.SaveAlertAsync(template);
                }
                existingAlerts = await _alertRepository.GetAlertsAsync(userId);
            }

            var defaultTemplatesList = AlertTemplates.GetDefaultTemplates();

            // Combine custom alerts and default templates (for UI reference)
            // The logic in JS was:
            // const allAlerts = [
            //   ...defaultTemplates.map(template => ({ ...template, isDefault: true, enabled: true, userId: req.user.userId })),
            //   ...customAlerts.filter(alert => !alert.isDefault)
            // ];
            // But here we already saved defaults to DB if missing.
            // If user modified defaults, they are in DB.
            // If user deleted defaults, they might be missing.
            // The JS logic seems to always return default templates as "available" even if not in DB?
            // "Get user's custom alerts" -> from DB.
            // "Get default alert templates" -> static list.
            // "Combine": defaults (as base) + custom (overrides?).
            // Actually JS logic:
            // allAlerts = defaultTemplates (mapped) + customAlerts (filtered !isDefault).
            // This implies "default" alerts in DB are ignored in the "allAlerts" list if they are marked isDefault?
            // Wait, if I save them to DB, they are "custom" in a way, but `IsDefault` flag is true.
            // JS: `customAlerts.filter(alert => !alert.isDefault)` removes DB alerts that are marked default.
            // And replaces them with fresh default templates.
            // This means changes to default alerts are NOT returned in `allAlerts`?
            // That seems wrong if the user can edit them.
            // Let's check if user can edit default alerts.
            // If they edit, does it become `isDefault: false`?

            // Let's stick to returning what's in DB + templates for reference.

            return Ok(new
            {
                alerts = existingAlerts,
                customAlerts = existingAlerts.Where(a => !a.IsDefault),
                defaultTemplates = defaultTemplatesList,
                count = existingAlerts.Count()
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateAlert([FromBody] CreateAlertRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null || !user.Features.StreamAlerts) return Forbid();

            if (string.IsNullOrEmpty(request.Type) || string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.TextPrompt))
            {
                return BadRequest(new { error = "Type, name, and text prompt are required" });
            }

            var validTypes = new[] { "follow", "subscription", "resub", "bits", "raid", "giftsub", "hypetrain", "custom" };
            if (!validTypes.Contains(request.Type))
            {
                return BadRequest(new { error = "Invalid alert type" });
            }

            var alertId = $"{userId}_{Guid.NewGuid()}";
            var alert = new Alert
            {
                Id = alertId,
                UserId = userId,
                Type = request.Type,
                Name = request.Name,
                VisualCue = request.VisualCue ?? "",
                Sound = request.Sound ?? "",
                SoundDescription = request.SoundDescription ?? "",
                TextPrompt = request.TextPrompt,
                Duration = request.Duration,
                BackgroundColor = request.BackgroundColor ?? "#1a0d0d",
                TextColor = request.TextColor ?? "#ffffff",
                BorderColor = request.BorderColor ?? "#666666",
                IsEnabled = true,
                IsDefault = false,
                Effects = request.Effects ?? "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _alertRepository.SaveAlertAsync(alert);

            return Ok(new { message = "Alert created successfully", alertId, alert });
        }

        [HttpPut("{alertId}")]
        public async Task<IActionResult> UpdateAlert(string alertId, [FromBody] UpdateAlertRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var alert = await _alertRepository.GetAlertAsync(userId, alertId);
            if (alert == null)
            {
                return NotFound(new { error = "Alert not found" });
            }

            alert.Name = request.Name ?? alert.Name;
            alert.VisualCue = request.VisualCue ?? alert.VisualCue;
            alert.Sound = request.Sound ?? alert.Sound;
            alert.SoundDescription = request.SoundDescription ?? alert.SoundDescription;
            alert.TextPrompt = request.TextPrompt ?? alert.TextPrompt;
            alert.Duration = request.Duration > 0 ? request.Duration : alert.Duration;
            alert.BackgroundColor = request.BackgroundColor ?? alert.BackgroundColor;
            alert.TextColor = request.TextColor ?? alert.TextColor;
            alert.BorderColor = request.BorderColor ?? alert.BorderColor;
            alert.Effects = request.Effects ?? alert.Effects;
            alert.IsEnabled = request.IsEnabled ?? alert.IsEnabled;
            alert.UpdatedAt = DateTimeOffset.UtcNow;

            await _alertRepository.SaveAlertAsync(alert);

            return Ok(new { message = "Alert updated successfully", alert });
        }

        [HttpDelete("{alertId}")]
        public async Task<IActionResult> DeleteAlert(string alertId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var alert = await _alertRepository.GetAlertAsync(userId, alertId);
            if (alert == null)
            {
                return NotFound(new { error = "Alert not found" });
            }

            if (alert.IsDefault)
            {
                return BadRequest(new { error = "Cannot delete default alerts" });
            }

            await _alertRepository.DeleteAlertAsync(userId, alertId);

            return Ok(new { message = "Alert deleted successfully" });
        }

        [HttpGet("event-mappings")]
        public async Task<IActionResult> GetEventMappings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var mappings = await _alertRepository.GetEventMappingsAsync(userId);
            var defaultMappings = AlertTemplates.GetDefaultEventMappings();
            var availableEvents = AlertTemplates.GetAllAvailableEvents();

            // If no mappings exist, return defaults (but don't save them yet, or maybe we should?)
            // JS logic: "Initialize default event mappings" is called in initializeUserAlerts.
            // So they should exist.
            // But if they don't, we can return defaults.
            if (!mappings.Any())
            {
                mappings = defaultMappings;
            }

            // Get available alert types
            var alerts = await _alertRepository.GetAlertsAsync(userId);
            var availableAlertTypes = new List<string> { "none" };
            availableAlertTypes.AddRange(alerts.Select(a => a.Type).Distinct());

            return Ok(new
            {
                mappings,
                defaultMappings,
                availableEvents,
                availableAlertTypes,
                disableOption = "none"
            });
        }

        [HttpPost("event-mappings/reset")]
        public async Task<IActionResult> ResetEventMappings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null || !user.Features.StreamAlerts) return Forbid();

            var defaultMappings = AlertTemplates.GetDefaultEventMappings();
            await _alertRepository.SaveEventMappingsAsync(userId, defaultMappings);

            return Ok(new
            {
                message = "Event mappings reset to defaults successfully",
                mappings = defaultMappings
            });
        }

        [HttpPut("event-mappings")]
        public async Task<IActionResult> UpdateEventMappings([FromBody] Dictionary<string, string> mappings)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _alertRepository.SaveEventMappingsAsync(userId, mappings);
            return Ok(new { message = "Event mappings updated successfully", mappings });
        }
    }

    public class CreateAlertRequest
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? VisualCue { get; set; }
        public string? Sound { get; set; }
        public string? SoundDescription { get; set; }
        public string TextPrompt { get; set; } = string.Empty;
        public int Duration { get; set; } = 4000;
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
        public string? BorderColor { get; set; }
        public string? Effects { get; set; }
    }

    public class UpdateAlertRequest
    {
        public string? Name { get; set; }
        public string? VisualCue { get; set; }
        public string? Sound { get; set; }
        public string? SoundDescription { get; set; }
        public string? TextPrompt { get; set; }
        public int Duration { get; set; }
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
        public string? BorderColor { get; set; }
        public string? Effects { get; set; }
        public bool? IsEnabled { get; set; }
    }
}
