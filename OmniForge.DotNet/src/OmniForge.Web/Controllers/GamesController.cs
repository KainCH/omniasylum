using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Web.Controllers
{
    [Route("api/games")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    public class GamesController : ControllerBase
    {
        private readonly ITwitchApiService _twitchApiService;
        private readonly IGameLibraryRepository _gameLibraryRepository;
        private readonly IGameContextRepository _gameContextRepository;
        private readonly ILogger<GamesController> _logger;

        public GamesController(
            ITwitchApiService twitchApiService,
            IGameLibraryRepository gameLibraryRepository,
            IGameContextRepository gameContextRepository,
            ILogger<GamesController> logger)
        {
            _twitchApiService = twitchApiService;
            _gameLibraryRepository = gameLibraryRepository;
            _gameContextRepository = gameContextRepository;
            _logger = logger;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                return BadRequest(new { error = "Search query must be at least 2 characters" });
            }

            try
            {
                var results = await _twitchApiService.SearchCategoriesAsync(userId, query.Trim(), first: 20);
                return Ok(new { count = results.Count, results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching Twitch categories for user {UserId}", LogSanitizer.Sanitize(userId));
                return StatusCode(500, new { error = "Failed to search Twitch categories" });
            }
        }

        [HttpGet("library")]
        public async Task<IActionResult> ListLibrary()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var items = await _gameLibraryRepository.ListAsync(userId);
            return Ok(new { count = items.Count, games = items });
        }

        [HttpPost("library/add")]
        public async Task<IActionResult> AddToLibrary([FromBody] AddGameRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.GameId) || string.IsNullOrWhiteSpace(request.GameName))
            {
                return BadRequest(new { error = "gameId and gameName are required" });
            }

            var now = DateTimeOffset.UtcNow;
            await _gameLibraryRepository.UpsertAsync(new GameLibraryItem
            {
                UserId = userId,
                GameId = request.GameId.Trim(),
                GameName = request.GameName.Trim(),
                BoxArtUrl = request.BoxArtUrl?.Trim() ?? string.Empty,
                CreatedAt = now,
                LastSeenAt = now
            });

            return Ok(new { message = "Game added to library" });
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveGame()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var ctx = await _gameContextRepository.GetAsync(userId);
            return Ok(new
            {
                activeGameId = ctx?.ActiveGameId,
                activeGameName = ctx?.ActiveGameName,
                updatedAt = ctx?.UpdatedAt
            });
        }
    }

    public class AddGameRequest
    {
        public string GameId { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string? BoxArtUrl { get; set; }
    }
}
