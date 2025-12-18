namespace OmniForge.Core.Configuration
{
    /// <summary>
    /// Global PayPal API configuration settings.
    /// Stored in appsettings.json or Azure Key Vault.
    /// </summary>
    public class PayPalSettings
    {
        public const string SectionName = "PayPal";

        /// <summary>
        /// PayPal Client ID from developer dashboard.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// PayPal Client Secret from developer dashboard.
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Whether to use PayPal Sandbox (true) or Live (false).
        /// </summary>
        public bool UseSandbox { get; set; } = true;

        /// <summary>
        /// PayPal API base URL.
        /// </summary>
        public string ApiBaseUrl => UseSandbox
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";

        /// <summary>
        /// PayPal IPN verification URL.
        /// </summary>
        public string IpnVerifyUrl => UseSandbox
            ? "https://ipnpb.sandbox.paypal.com/cgi-bin/webscr"
            : "https://ipnpb.paypal.com/cgi-bin/webscr";

        /// <summary>
        /// Default webhook ID (can be overridden per-user).
        /// </summary>
        public string DefaultWebhookId { get; set; } = string.Empty;
    }
}
