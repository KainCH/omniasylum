using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Models;
using OmniForge.Core.Utilities;
using OmniForge.Web.Models;

namespace OmniForge.Web.Services
{
    public class FeedbackIssueService : IFeedbackIssueService
    {
        private readonly IGitHubIssueService _gitHubIssueService;
        private readonly ILogger<FeedbackIssueService> _logger;

        public FeedbackIssueService(IGitHubIssueService gitHubIssueService, ILogger<FeedbackIssueService> logger)
        {
            _gitHubIssueService = gitHubIssueService;
            _logger = logger;
        }

        public async Task<GitHubIssueCreateResult> CreateIssueAsync(CreateGitHubIssueRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            var userId = user.FindFirst("userId")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new UnauthorizedAccessException("User is not authenticated");
            }

            var username = user.Identity?.Name;
            var displayName = user.FindFirst("displayName")?.Value;
            var type = (request.Type ?? string.Empty).Trim().ToLowerInvariant();

            if (type != "bug" && type != "feature")
            {
                throw new ArgumentException("Type must be 'bug' or 'feature'", nameof(request.Type));
            }

            var labels = new List<string>
            {
                type == "bug" ? "bug" : "feature",
                "from-app"
            };

            var titlePrefix = type == "bug" ? "[Bug]" : "[Feature]";
            var title = $"{titlePrefix} {request.Title.Trim()}";

            var submittedBy = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : (!string.IsNullOrWhiteSpace(username) ? username : userId);

            var body = BuildIssueBody(request, submittedBy);

            try
            {
                return await _gitHubIssueService.CreateIssueAsync(title, body, labels, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed creating GitHub issue from feedback submission");
                throw;
            }
        }

        private static string BuildIssueBody(CreateGitHubIssueRequest request, string submittedBy)
        {
            var now = DateTimeOffset.UtcNow;

            var nl = "\n";
            var body = "";
            body += $"**Type:** {request.Type.Trim()}" + nl;
            body += $"**Submitted by:** {submittedBy}" + nl;
            body += $"**Submitted at (UTC):** {now:O}" + nl + nl;

            body += "## Description" + nl;
            body += request.Description.Trim() + nl + nl;

            if (!string.IsNullOrWhiteSpace(request.StepsToReproduce))
            {
                body += "## Steps to reproduce" + nl;
                body += request.StepsToReproduce.Trim() + nl + nl;
            }

            if (!string.IsNullOrWhiteSpace(request.ExpectedBehavior))
            {
                body += "## Expected behavior" + nl;
                body += request.ExpectedBehavior.Trim() + nl + nl;
            }

            if (!string.IsNullOrWhiteSpace(request.ActualBehavior))
            {
                body += "## Actual behavior" + nl;
                body += request.ActualBehavior.Trim() + nl + nl;
            }

            if (!string.IsNullOrWhiteSpace(request.AdditionalInfo))
            {
                body += "## Additional info" + nl;
                body += request.AdditionalInfo.Trim() + nl + nl;
            }

            return body;
        }
    }
}
