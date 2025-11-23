using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/templates")]
    public class TemplateController : ControllerBase
    {
        private readonly ITemplateRepository _templateRepository;
        private readonly IUserRepository _userRepository;
        private readonly IOverlayNotifier _overlayNotifier;

        public TemplateController(
            ITemplateRepository templateRepository,
            IUserRepository userRepository,
            IOverlayNotifier overlayNotifier)
        {
            _templateRepository = templateRepository;
            _userRepository = userRepository;
            _overlayNotifier = overlayNotifier;
        }

        [HttpGet("available")]
        public async Task<IActionResult> GetAvailableTemplates()
        {
            var templates = await _templateRepository.GetAvailableTemplatesAsync();
            return Ok(new
            {
                templates = templates.Select(kvp => new
                {
                    id = kvp.Key,
                    name = kvp.Value.Name,
                    description = kvp.Value.Description,
                    type = kvp.Value.Type,
                    config = kvp.Value.Config
                })
            });
        }

        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentTemplate()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var templateStyle = user.Features.TemplateStyle ?? "asylum_themed";

            if (templateStyle == "custom")
            {
                var customTemplate = await _templateRepository.GetUserCustomTemplateAsync(userId);
                if (customTemplate != null)
                {
                    customTemplate.TemplateStyle = "custom";
                    return Ok(customTemplate);
                }
            }

            var availableTemplates = await _templateRepository.GetAvailableTemplatesAsync();
            if (availableTemplates.TryGetValue(templateStyle, out var template))
            {
                template.TemplateStyle = templateStyle;
                return Ok(template);
            }

            // Fallback
            var fallback = availableTemplates["asylum_themed"];
            fallback.TemplateStyle = "asylum_themed";
            return Ok(fallback);
        }

        [HttpPut("select")]
        public async Task<IActionResult> SelectTemplate([FromBody] SelectTemplateRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrEmpty(request.TemplateStyle))
            {
                return BadRequest(new { error = "Template style is required" });
            }

            var availableTemplates = await _templateRepository.GetAvailableTemplatesAsync();
            if (request.TemplateStyle != "custom" && !availableTemplates.ContainsKey(request.TemplateStyle))
            {
                return BadRequest(new { error = "Invalid template style" });
            }

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            user.Features.TemplateStyle = request.TemplateStyle;
            await _userRepository.SaveUserAsync(user);

            // Get updated template for response
            Template? updatedTemplate = null;
            if (request.TemplateStyle == "custom")
            {
                updatedTemplate = await _templateRepository.GetUserCustomTemplateAsync(userId);
                if (updatedTemplate != null) updatedTemplate.TemplateStyle = "custom";
            }
            else
            {
                updatedTemplate = availableTemplates[request.TemplateStyle];
                updatedTemplate.TemplateStyle = request.TemplateStyle;
            }

            // Notify clients
            if (updatedTemplate != null)
            {
                await _overlayNotifier.NotifyTemplateChangedAsync(userId, request.TemplateStyle, updatedTemplate);
            }

            return Ok(new
            {
                success = true,
                templateStyle = request.TemplateStyle,
                template = updatedTemplate
            });
        }

        [HttpGet("custom")]
        public async Task<IActionResult> GetCustomTemplate()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var customTemplate = await _templateRepository.GetUserCustomTemplateAsync(userId);
            return Ok(customTemplate);
        }

        [HttpPut("custom")]
        public async Task<IActionResult> SaveCustomTemplate([FromBody] SaveCustomTemplateRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrEmpty(request.Name) || request.Config == null)
            {
                return BadRequest(new { error = "Template name and config are required" });
            }

            // Basic validation of config structure (simplified check)
            if (request.Config.Colors == null || request.Config.Fonts == null || request.Config.Animations == null)
            {
                return BadRequest(new { error = "Invalid template config structure" });
            }

            var template = new Template
            {
                Name = request.Name,
                Config = request.Config,
                Type = "custom",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _templateRepository.SaveUserCustomTemplateAsync(userId, template);

            if (request.MakeActive)
            {
                var user = await _userRepository.GetUserAsync(userId);
                if (user != null)
                {
                    user.Features.TemplateStyle = "custom";
                    await _userRepository.SaveUserAsync(user);
                }
            }

            return Ok(new
            {
                success = true,
                template
            });
        }
    }

    public class SelectTemplateRequest
    {
        public string TemplateStyle { get; set; } = string.Empty;
    }

    public class SaveCustomTemplateRequest
    {
        public string Name { get; set; } = string.Empty;
        public TemplateConfig Config { get; set; } = new TemplateConfig();
        public bool MakeActive { get; set; }
    }
}
