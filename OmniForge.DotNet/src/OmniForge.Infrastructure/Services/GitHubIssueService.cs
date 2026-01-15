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
        private const string DoNotStoreTokenPlaceholder = "DO_NOT_STORE_GITHUB_PAT_HERE_USE_KEY_VAULT_OR_ENVIRONMENT_VARIABLE";

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
                throw new InvalidOperationException("GitHub repository is not configured (GitHub:RepoOwner / GitHub:RepoName).");
            }

            if (string.IsNullOrWhiteSpace(settings.IssuesToken) || string.Equals(settings.IssuesToken, DoNotStoreTokenPlaceholder, StringComparison.OrdinalIgnoreCase))
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

            if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException("GitHub API base URL is invalid (GitHub:ApiBaseUrl).");
            }

            var payload = new
            {
                title,
                body,
                labels
            };

            var route = $"/repos/{settings.RepoOwner}/{settings.RepoName}/issues";

            // token vs bearer: classic PATs prefer "token", fine-grained tokens prefer "Bearer".
            var scheme = settings.IssuesToken.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase)
                ? "Bearer"
                : "token";

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, route))
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };

            // Avoid mutating shared HttpClient headers (HttpClient can be reused concurrently).
            request.Headers.UserAgent.TryParseAdd("OmniForge/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme, settings.IssuesToken);

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // Avoid logging full response bodies at Error level.
                    _logger.LogError("❌ GitHub issue create failed: status={Status}", (int)response.StatusCode);

                    if (_logger.IsEnabled(LogLevel.Debug) && !string.IsNullOrWhiteSpace(responseBody))
                    {
                        var snippet = responseBody.Length <= 512 ? responseBody : responseBody.Substring(0, 512);
                        _logger.LogDebug("GitHub issue create response body (snippet): {Body}", LogSanitizer.Sanitize(snippet));
                    }

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
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogWarning("⚠️ GitHub create issue succeeded but response missing expected fields.");
                        _logger.LogDebug("GitHub create issue response body (snippet): {Body}", LogSanitizer.Sanitize(responseBody.Length <= 512 ? responseBody : responseBody.Substring(0, 512)));
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ GitHub create issue succeeded but response missing expected fields.");
                    }
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
