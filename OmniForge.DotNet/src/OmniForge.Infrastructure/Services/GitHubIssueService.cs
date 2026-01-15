using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Models;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Configuration;

namespace OmniForge.Infrastructure.Services
{
    public class GitHubIssueService : IGitHubIssueService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly IOptionsMonitor<GitHubSettings> _settings;
        private readonly ILogger<GitHubIssueService> _logger;

        public GitHubIssueService(HttpClient httpClient, IOptionsMonitor<GitHubSettings> settings, ILogger<GitHubIssueService> logger)
        {
            _httpClient = httpClient;
            _settings = settings;
            _logger = logger;
        }

        public async Task<GitHubIssueCreateResult> CreateIssueAsync(
            string title,
            string body,
            IReadOnlyCollection<string> labels,
            CancellationToken cancellationToken = default)
        {
            var settings = _settings.CurrentValue;

            if (string.IsNullOrWhiteSpace(settings.RepoOwner) || string.IsNullOrWhiteSpace(settings.RepoName))
            {
                throw new InvalidOperationException("GitHub repository is not configured (GitHub:RepoOwner / GitHub:RepoName). ");
            }

            if (string.IsNullOrWhiteSpace(settings.IssuesToken))
            {
                throw new InvalidOperationException("GitHub issue token is not configured (GitHub:IssuesToken).");
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Title is required", nameof(title));
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                throw new ArgumentException("Body is required", nameof(body));
            }

            // GitHub requires a User-Agent. Also set the newest JSON media type.
            if (!_httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("OmniForge/1.0"))
            {
                // ignore
            }

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _httpClient.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
            _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            // token vs bearer: classic PATs prefer "token", fine-grained tokens prefer "Bearer".
            var scheme = settings.IssuesToken.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase)
                ? "Bearer"
                : "token";

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, settings.IssuesToken);

            if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException("GitHub API base URL is invalid (GitHub:ApiBaseUrl). ");
            }

            _httpClient.BaseAddress = baseUri;

            var payload = new
            {
                title,
                body,
                labels
            };

            var route = $"/repos/{settings.RepoOwner}/{settings.RepoName}/issues";

            try
            {
                using var response = await _httpClient.PostAsJsonAsync(route, payload, JsonOptions, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ GitHub issue create failed: status={Status}, body={Body}", (int)response.StatusCode, LogSanitizer.Sanitize(responseBody));
                    throw new InvalidOperationException("GitHub issue creation failed.");
                }

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                var number = root.TryGetProperty("number", out var numEl) && numEl.ValueKind == JsonValueKind.Number
                    ? numEl.GetInt32()
                    : 0;

                var htmlUrl = root.TryGetProperty("html_url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
                    ? urlEl.GetString() ?? string.Empty
                    : string.Empty;

                if (number <= 0 || string.IsNullOrWhiteSpace(htmlUrl))
                {
                    _logger.LogWarning("⚠️ GitHub create issue succeeded but response missing expected fields: body={Body}", LogSanitizer.Sanitize(responseBody));
                }

                _logger.LogInformation("✅ Created GitHub issue #{Number} for {Owner}/{Repo}", number, settings.RepoOwner, settings.RepoName);

                return new GitHubIssueCreateResult(number, htmlUrl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "❌ Failed creating GitHub issue for {Owner}/{Repo}", settings.RepoOwner, settings.RepoName);
                throw;
            }
        }
    }
}
