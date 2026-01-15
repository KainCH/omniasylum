using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using OmniForge.Core.Models;
using OmniForge.Web.Models;

namespace OmniForge.Web.Services
{
    public interface IFeedbackIssueService
    {
        Task<GitHubIssueCreateResult> CreateIssueAsync(CreateGitHubIssueRequest request, ClaimsPrincipal user, CancellationToken cancellationToken);
    }
}
