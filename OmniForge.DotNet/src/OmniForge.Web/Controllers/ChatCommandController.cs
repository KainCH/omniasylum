using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/chat-commands")]
    public class ChatCommandController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly IOverlayNotifier _overlayNotifier; // Assuming we use this for socket updates

        public ChatCommandController(
            IUserRepository userRepository,
            ICounterRepository counterRepository,
            IOverlayNotifier overlayNotifier)
        {
            _userRepository = userRepository;
            _counterRepository = counterRepository;
            _overlayNotifier = overlayNotifier;
        }

        [HttpGet]
        public async Task<IActionResult> GetChatCommands()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);
            return Ok(config.Commands); // Node returns the commands object directly
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetChatCommandSettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);
            return Ok(new
            {
                maxIncrementAmount = config.MaxIncrementAmount,
                commands = config.Commands
            });
        }

        [HttpGet("defaults")]
        public IActionResult GetDefaults()
        {
            // Match Node.js defaults
            var defaults = new Dictionary<string, ChatCommandDefinition>
            {
                { "!deaths", new ChatCommandDefinition { Response = "Current death count: {{deaths}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
                { "!swears", new ChatCommandDefinition { Response = "Current swear count: {{swears}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
                { "!screams", new ChatCommandDefinition { Response = "Current scream count: {{screams}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
                { "!stats", new ChatCommandDefinition { Response = "Deaths: {{deaths}}, Swears: {{swears}}, Screams: {{screams}}, Bits: {{bits}}", Permission = "everyone", Cooldown = 10, Enabled = true } },
                { "!death+", new ChatCommandDefinition { Action = "increment", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!death-", new ChatCommandDefinition { Action = "decrement", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!d+", new ChatCommandDefinition { Action = "increment", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!d-", new ChatCommandDefinition { Action = "decrement", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!swear+", new ChatCommandDefinition { Action = "increment", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!swear-", new ChatCommandDefinition { Action = "decrement", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!sw+", new ChatCommandDefinition { Action = "increment", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!sw-", new ChatCommandDefinition { Action = "decrement", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!scream+", new ChatCommandDefinition { Action = "increment", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!scream-", new ChatCommandDefinition { Action = "decrement", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!sc+", new ChatCommandDefinition { Action = "increment", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!sc-", new ChatCommandDefinition { Action = "decrement", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
                { "!resetcounters", new ChatCommandDefinition { Action = "reset", Permission = "broadcaster", Cooldown = 10, Enabled = true } }
            };
            return Ok(defaults);
        }

        [HttpPut]
        public async Task<IActionResult> SaveChatCommands([FromBody] SaveChatCommandsRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var commands = request.Commands;
            if (commands == null) return BadRequest(new { error = "Commands configuration is required" });

            // Validate
            foreach (var kvp in commands)
            {
                var command = kvp.Key;
                var config = kvp.Value;

                if (!command.StartsWith("!"))
                    return BadRequest(new { error = $"Command {command} must start with !" });

                var validPermissions = new[] { "everyone", "subscriber", "moderator", "broadcaster" };
                if (!validPermissions.Contains(config.Permission))
                    return BadRequest(new { error = $"Command {command} has invalid permission level" });

                if (config.Cooldown < 0)
                    return BadRequest(new { error = $"Command {command} has invalid cooldown" });
            }

            var configWrapper = new ChatCommandConfiguration
            {
                Commands = commands,
                MaxIncrementAmount = request.MaxIncrementAmount ?? 1
            };

            // If MaxIncrementAmount was not provided, try to preserve existing value
            if (!request.MaxIncrementAmount.HasValue)
            {
                var existingConfig = await _userRepository.GetChatCommandsConfigAsync(userId);
                if (existingConfig != null)
                {
                    configWrapper.MaxIncrementAmount = existingConfig.MaxIncrementAmount;
                }
            }

            await _userRepository.SaveChatCommandsConfigAsync(userId, configWrapper);

            // Notify bot (using custom alert for now as a generic message channel, or we need a specific method)
            await _overlayNotifier.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", new { commands });

            return Ok(new { success = true, commands });
        }

        /* Replaced method below */

        [HttpPost]
        public async Task<IActionResult> AddChatCommand([FromBody] AddCommandRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrEmpty(request.Command) || !request.Command.StartsWith("!"))
                return BadRequest(new { error = "Command must start with !" });

            if (request.Config == null || string.IsNullOrEmpty(request.Config.Response))
                return BadRequest(new { error = "Command config with response is required" });

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);

            if (config.Commands.ContainsKey(request.Command))
                return BadRequest(new { error = "Command already exists" });

            var newCommand = new ChatCommandDefinition
            {
                Response = request.Config.Response,
                Permission = request.Config.Permission ?? "everyone",
                Cooldown = request.Config.Cooldown,
                Enabled = request.Config.Enabled,
                Custom = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            config.Commands[request.Command] = newCommand;
            await _userRepository.SaveChatCommandsConfigAsync(userId, config);

            await _overlayNotifier.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", new { commands = config.Commands });

            return Ok(new { success = true, command = request.Command, config = newCommand });
        }

        [HttpPut("{command}")]
        public async Task<IActionResult> UpdateChatCommand(string command, [FromBody] UpdateCommandRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!command.StartsWith("!"))
                return BadRequest(new { error = "Command must start with !" });

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);

            if (!config.Commands.ContainsKey(command))
                return NotFound(new { error = "Command not found" });

            var currentConfig = config.Commands[command];

            // Update properties
            if (request.Config != null)
            {
                if (request.Config.Response != null) currentConfig.Response = request.Config.Response;
                if (request.Config.Permission != null) currentConfig.Permission = request.Config.Permission;
                if (request.Config.Cooldown.HasValue) currentConfig.Cooldown = request.Config.Cooldown.Value;
                currentConfig.Enabled = request.Config.Enabled;
                currentConfig.UpdatedAt = DateTimeOffset.UtcNow;
            }

            config.Commands[command] = currentConfig;
            await _userRepository.SaveChatCommandsConfigAsync(userId, config);

            await _overlayNotifier.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", new { commands = config.Commands });

            return Ok(new { success = true, command, config = currentConfig });
        }

        [HttpDelete("{command}")]
        public async Task<IActionResult> DeleteChatCommand(string command)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!command.StartsWith("!"))
                return BadRequest(new { error = "Command must start with !" });

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);

            if (!config.Commands.ContainsKey(command))
                return NotFound(new { error = "Command not found" });

            if (!config.Commands[command].Custom)
                return BadRequest(new { error = "Cannot delete core commands" });

            config.Commands.Remove(command);
            await _userRepository.SaveChatCommandsConfigAsync(userId, config);

            await _overlayNotifier.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", new { commands = config.Commands });

            return Ok(new { success = true, command, deleted = true });
        }

        [HttpPost("{command}/test")]
        public async Task<IActionResult> TestChatCommand(string command)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);

            if (!config.Commands.ContainsKey(command))
                return NotFound(new { error = "Command not found" });

            var commandConfig = config.Commands[command];

            if (!commandConfig.Enabled)
                return BadRequest(new { error = "Command is disabled" });

            var counters = await _counterRepository.GetCountersAsync(userId);
            var response = commandConfig.Response ?? "Command executed";

            // Replace template variables
            // Simple regex replacement
            response = Regex.Replace(response, @"\{\{(\w+)\}\}", match =>
            {
                var key = match.Groups[1].Value.ToLowerInvariant();

                if (counters == null) return "0";

                // Check standard counters
                if (key == "deaths") return counters.Deaths.ToString();
                if (key == "swears") return counters.Swears.ToString();
                if (key == "screams") return counters.Screams.ToString();
                if (key == "bits") return counters.Bits.ToString();

                // Check custom counters
                if (counters.CustomCounters != null && counters.CustomCounters.ContainsKey(key))
                {
                    return counters.CustomCounters[key].ToString();
                }

                return match.Value;
            });

            return Ok(new
            {
                success = true,
                command,
                response,
                config = commandConfig,
                testMode = true
            });
        }
    }

    public class SaveChatCommandsRequest
    {
        public Dictionary<string, ChatCommandDefinition> Commands { get; set; } = new();
        public int? MaxIncrementAmount { get; set; }
    }

    public class AddCommandRequest
    {
        public string Command { get; set; } = string.Empty;
        public ChatCommandDefinition Config { get; set; } = new();
    }

    public class UpdateCommandRequest
    {
        public UpdateCommandConfigDto? Config { get; set; }
    }

    /// <summary>
    /// DTO for updating chat command config with nullable properties for partial updates.
    /// </summary>
    public class UpdateCommandConfigDto
    {
        public string? Response { get; set; }
        public string? Permission { get; set; }
        public int? Cooldown { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
