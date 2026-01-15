using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniForge.Core.Models;

namespace OmniForge.Core.Interfaces
{
    public interface IGitHubIssueService
    {
        Task<GitHubIssueCreateResult> CreateIssueAsync(
            string title,
            string body,
            IReadOnlyCollection<string> labels,
            CancellationToken cancellationToken = default);
    }
}
