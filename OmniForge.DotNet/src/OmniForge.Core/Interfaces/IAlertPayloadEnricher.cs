using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IAlertPayloadEnricher
    {
        /// <summary>
        /// Enriches an alert payload with DB-configured alert template data (sound, effects,
        /// colors, text prompt with placeholder substitution).
        /// Returns the enriched payload, or a dictionary with { "suppress": true } when the
        /// alert exists but is disabled, or the original <paramref name="baseData"/> when no
        /// matching alert template is configured.
        /// </summary>
        Task<object> EnrichPayloadAsync(string userId, string alertType, object baseData);

        /// <summary>
        /// Returns true when the enriched payload signals that the alert is suppressed
        /// (a matching alert template exists in the DB but is disabled by the user).
        /// </summary>
        bool IsSuppressed(object payload);
    }
}
