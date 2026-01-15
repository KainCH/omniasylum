using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Utilities;
using OmniForge.Web.Models;
using OmniForge.Web.Services;

namespace OmniForge.Web.Controllers
{
    [Route("api/feedback")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    public class FeedbackController : ControllerBase
    {
        private readonly IFeedbackIssueService _feedbackIssueService;
        private readonly ILogger<FeedbackController> _logger;

        public FeedbackController(IFeedbackIssueService feedbackIssueService, ILogger<FeedbackController> logger)
        {
            _feedbackIssueService = feedbackIssueService;
            _logger = logger;
        }

        [HttpPost("issues")]
        public async Task<IActionResult> CreateGitHubIssue([FromBody] CreateGitHubIssueRequest request, CancellationToken cancellationToken)
        {
            var userId = User.FindFirst("userId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            try
            {
                var result = await _feedbackIssueService.CreateIssueAsync(request, User, cancellationToken);

                return Ok(new
                {
                    number = result.Number,
                    url = result.HtmlUrl
                });
            }
            catch (ArgumentException ex) when (ex.ParamName == nameof(CreateGitHubIssueRequest.Type))
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed creating GitHub issue for user {UserId}", LogSanitizer.Sanitize(userId));
                return StatusCode(500, new { error = "Failed to submit report. Please try again later." });
            }
        }
    }
}
