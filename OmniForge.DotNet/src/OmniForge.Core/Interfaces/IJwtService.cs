using System.Security.Claims;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IJwtService
    {
        string GenerateToken(User user);
        ClaimsPrincipal? ValidateToken(string token);
    }
}
