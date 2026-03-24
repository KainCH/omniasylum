using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ILicenseService
    {
        LicenseTier GetEffectiveTier(User user);
        bool HasFeatureAccess(User user, string featureName);
        bool IsLicenseActive(User user);
    }
}
