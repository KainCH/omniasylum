using System;

namespace OmniForge.Infrastructure.Configuration
{
    public class GitHubSettings
    {
        public string ApiBaseUrl { get; set; } = "https://api.github.com";

        /// <summary>
        /// GitHub token used to create issues. Store this in Azure Key Vault.
        /// </summary>
        public string IssuesToken { get; set; } = string.Empty;

        public string RepoOwner { get; set; } = "KainCH";

        public string RepoName { get; set; } = "omniasylum";
    }
}
