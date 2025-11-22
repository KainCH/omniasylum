using System;
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

        public ChatCommandController(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetChatCommands()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);
            return Ok(config);
        }

        [HttpPut]
        public async Task<IActionResult> SaveChatCommands([FromBody] ChatCommandConfiguration config)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _userRepository.SaveChatCommandsConfigAsync(userId, config);
            return Ok(config);
        }

        [HttpPost]
        public async Task<IActionResult> AddChatCommand([FromBody] AddCommandRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var config = await _userRepository.GetChatCommandsConfigAsync(userId);

            if (config.Commands.ContainsKey(request.Command))
            {
                return BadRequest("Command already exists");
            }

            config.Commands[request.Command] = request.Config;
            await _userRepository.SaveChatCommandsConfigAsync(userId, config);

            return Ok(config);
        }

        [HttpGet("defaults")]
        public IActionResult GetDefaults()
        {
            var defaults = new ChatCommandConfiguration
            {
                Commands = new System.Collections.Generic.Dictionary<string, ChatCommandDefinition>
                {
                    { "!discord", new ChatCommandDefinition { Response = "Join our Discord: https://discord.gg/example", Permission = "everyone", Cooldown = 30 } },
                    { "!lurk", new ChatCommandDefinition { Response = "Thank you for lurking!", Permission = "everyone", Cooldown = 60 } }
                }
            };
            return Ok(defaults);
        }
    }

    public class AddCommandRequest
    {
        public string Command { get; set; } = string.Empty;
        public ChatCommandDefinition Config { get; set; } = new();
    }
}
