using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace OmniForge.Web.Configuration
{
    public class LegacyKeyVaultSecretManager : KeyVaultSecretManager
    {
        public override string GetKey(KeyVaultSecret secret)
        {
            var secretName = secret.Name;

            return secretName switch
            {
                "TWITCH-CLIENT-ID" => "Twitch:ClientId",
                "TWITCH-CLIENT-SECRET" => "Twitch:ClientSecret",
                "JWT-SECRET" => "Jwt:Secret",
                _ => base.GetKey(secret)
            };
        }
    }
}
