using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmniForge.Core.Models
{
    #region IPN (Legacy) Models

    /// <summary>
    /// Parsed IPN (Instant Payment Notification) message from PayPal.
    /// IPN sends form-urlencoded data with payment details.
    /// </summary>
    public class PayPalIpnMessage
    {
        /// <summary>PayPal transaction ID.</summary>
        public string TxnId { get; set; } = string.Empty;

        /// <summary>Payment status (Completed, Pending, Refunded, etc.).</summary>
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary>Payer's email address.</summary>
        public string PayerEmail { get; set; } = string.Empty;

        /// <summary>Payer's first name.</summary>
        public string FirstName { get; set; } = string.Empty;

        /// <summary>Payer's last name.</summary>
        public string LastName { get; set; } = string.Empty;

        /// <summary>Business name (for business accounts).</summary>
        public string PayerBusinessName { get; set; } = string.Empty;

        /// <summary>Gross payment amount.</summary>
        public decimal McGross { get; set; }

        /// <summary>Currency code (USD, EUR, etc.).</summary>
        public string McCurrency { get; set; } = "USD";

        /// <summary>Receiver's PayPal email (streamer's email).</summary>
        public string ReceiverEmail { get; set; } = string.Empty;

        /// <summary>Custom field set by the payment button (can contain user mapping info).</summary>
        public string Custom { get; set; } = string.Empty;

        /// <summary>Item name or memo from the donation.</summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>Optional memo/note from payer.</summary>
        public string Memo { get; set; } = string.Empty;

        /// <summary>Transaction type (web_accept for donations).</summary>
        public string TxnType { get; set; } = string.Empty;

        /// <summary>Original raw IPN data for verification.</summary>
        public string RawIpnData { get; set; } = string.Empty;

        /// <summary>
        /// Get the display name for the donor.
        /// </summary>
        public string GetPayerDisplayName()
        {
            if (!string.IsNullOrEmpty(PayerBusinessName))
                return PayerBusinessName;

            var fullName = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrEmpty(fullName) ? "Anonymous" : fullName;
        }

        /// <summary>
        /// Get the donation message (memo or item name).
        /// </summary>
        public string GetDonationMessage()
        {
            if (!string.IsNullOrEmpty(Memo))
                return Memo;
            if (!string.IsNullOrEmpty(ItemName) && !ItemName.StartsWith("Donation", StringComparison.OrdinalIgnoreCase))
                return ItemName;
            return string.Empty;
        }
    }

    #endregion

    #region Webhook (Modern) Models

    /// <summary>
    /// PayPal Webhook event envelope.
    /// See: https://developer.paypal.com/api/rest/webhooks/
    /// </summary>
    public class PayPalWebhookEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("event_version")]
        public string EventVersion { get; set; } = string.Empty;

        [JsonPropertyName("create_time")]
        public string CreateTime { get; set; } = string.Empty;

        [JsonPropertyName("resource_type")]
        public string ResourceType { get; set; } = string.Empty;

        [JsonPropertyName("resource_version")]
        public string ResourceVersion { get; set; } = string.Empty;

        [JsonPropertyName("event_type")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("resource")]
        public PayPalWebhookResource? Resource { get; set; }

        [JsonPropertyName("links")]
        public List<PayPalLink>? Links { get; set; }
    }

    /// <summary>
    /// The resource object within a webhook event.
    /// Structure varies by event type; this covers PAYMENT.CAPTURE.COMPLETED events.
    /// </summary>
    public class PayPalWebhookResource
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public PayPalAmount? Amount { get; set; }

        [JsonPropertyName("payer")]
        public PayPalPayer? Payer { get; set; }

        [JsonPropertyName("payee")]
        public PayPalPayee? Payee { get; set; }

        [JsonPropertyName("custom_id")]
        public string? CustomId { get; set; }

        [JsonPropertyName("invoice_id")]
        public string? InvoiceId { get; set; }

        [JsonPropertyName("note_to_payee")]
        public string? NoteToPayee { get; set; }

        [JsonPropertyName("soft_descriptor")]
        public string? SoftDescriptor { get; set; }

        [JsonPropertyName("create_time")]
        public string? CreateTime { get; set; }

        [JsonPropertyName("update_time")]
        public string? UpdateTime { get; set; }

        [JsonPropertyName("supplementary_data")]
        public PayPalSupplementaryData? SupplementaryData { get; set; }
    }

    /// <summary>
    /// PayPal amount object.
    /// </summary>
    public class PayPalAmount
    {
        [JsonPropertyName("currency_code")]
        public string CurrencyCode { get; set; } = "USD";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "0.00";

        /// <summary>Parse the value as decimal.</summary>
        public decimal GetDecimalValue()
        {
            return decimal.TryParse(Value, out var result) ? result : 0m;
        }
    }

    /// <summary>
    /// PayPal payer information.
    /// </summary>
    public class PayPalPayer
    {
        [JsonPropertyName("email_address")]
        public string? EmailAddress { get; set; }

        [JsonPropertyName("payer_id")]
        public string? PayerId { get; set; }

        [JsonPropertyName("name")]
        public PayPalName? Name { get; set; }

        [JsonPropertyName("address")]
        public PayPalAddress? Address { get; set; }

        /// <summary>
        /// Get display name for the payer.
        /// </summary>
        public string GetDisplayName()
        {
            if (Name != null)
            {
                var fullName = $"{Name.GivenName} {Name.Surname}".Trim();
                if (!string.IsNullOrEmpty(fullName))
                    return fullName;
            }
            return "Anonymous";
        }
    }

    /// <summary>
    /// PayPal payee (receiver) information.
    /// </summary>
    public class PayPalPayee
    {
        [JsonPropertyName("email_address")]
        public string? EmailAddress { get; set; }

        [JsonPropertyName("merchant_id")]
        public string? MerchantId { get; set; }
    }

    /// <summary>
    /// PayPal name object.
    /// </summary>
    public class PayPalName
    {
        [JsonPropertyName("given_name")]
        public string? GivenName { get; set; }

        [JsonPropertyName("surname")]
        public string? Surname { get; set; }
    }

    /// <summary>
    /// PayPal address object.
    /// </summary>
    public class PayPalAddress
    {
        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }
    }

    /// <summary>
    /// Supplementary data in webhook resource.
    /// </summary>
    public class PayPalSupplementaryData
    {
        [JsonPropertyName("related_ids")]
        public PayPalRelatedIds? RelatedIds { get; set; }
    }

    /// <summary>
    /// Related IDs for linking orders/captures.
    /// </summary>
    public class PayPalRelatedIds
    {
        [JsonPropertyName("order_id")]
        public string? OrderId { get; set; }
    }

    /// <summary>
    /// HATEOAS link in PayPal responses.
    /// </summary>
    public class PayPalLink
    {
        [JsonPropertyName("href")]
        public string Href { get; set; } = string.Empty;

        [JsonPropertyName("rel")]
        public string Rel { get; set; } = string.Empty;

        [JsonPropertyName("method")]
        public string? Method { get; set; }
    }

    #endregion

    #region API Response Models

    /// <summary>
    /// PayPal OAuth token response.
    /// </summary>
    public class PayPalTokenResponse
    {
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("app_id")]
        public string? AppId { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("nonce")]
        public string? Nonce { get; set; }
    }

    /// <summary>
    /// PayPal webhook signature verification request.
    /// </summary>
    public class PayPalWebhookVerifyRequest
    {
        [JsonPropertyName("auth_algo")]
        public string AuthAlgo { get; set; } = string.Empty;

        [JsonPropertyName("cert_url")]
        public string CertUrl { get; set; } = string.Empty;

        [JsonPropertyName("transmission_id")]
        public string TransmissionId { get; set; } = string.Empty;

        [JsonPropertyName("transmission_sig")]
        public string TransmissionSig { get; set; } = string.Empty;

        [JsonPropertyName("transmission_time")]
        public string TransmissionTime { get; set; } = string.Empty;

        [JsonPropertyName("webhook_id")]
        public string WebhookId { get; set; } = string.Empty;

        [JsonPropertyName("webhook_event")]
        public PayPalWebhookEvent? WebhookEvent { get; set; }
    }

    /// <summary>
    /// PayPal webhook signature verification response.
    /// </summary>
    public class PayPalWebhookVerifyResponse
    {
        [JsonPropertyName("verification_status")]
        public string VerificationStatus { get; set; } = string.Empty;

        /// <summary>Check if verification succeeded.</summary>
        public bool IsVerified => VerificationStatus == "SUCCESS";
    }

    /// <summary>
    /// PayPal capture details response (for API lookup).
    /// </summary>
    public class PayPalCaptureDetails
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public PayPalAmount? Amount { get; set; }

        [JsonPropertyName("invoice_id")]
        public string? InvoiceId { get; set; }

        [JsonPropertyName("custom_id")]
        public string? CustomId { get; set; }

        [JsonPropertyName("note_to_payer")]
        public string? NoteToPayer { get; set; }

        [JsonPropertyName("create_time")]
        public string? CreateTime { get; set; }

        [JsonPropertyName("update_time")]
        public string? UpdateTime { get; set; }

        [JsonPropertyName("links")]
        public List<PayPalLink>? Links { get; set; }
    }

    /// <summary>
    /// PayPal order details response (for API lookup).
    /// </summary>
    public class PayPalOrderDetails
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("intent")]
        public string? Intent { get; set; }

        [JsonPropertyName("payer")]
        public PayPalPayer? Payer { get; set; }

        [JsonPropertyName("purchase_units")]
        public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }

        [JsonPropertyName("create_time")]
        public string? CreateTime { get; set; }

        [JsonPropertyName("update_time")]
        public string? UpdateTime { get; set; }

        [JsonPropertyName("links")]
        public List<PayPalLink>? Links { get; set; }
    }

    /// <summary>
    /// PayPal purchase unit in order.
    /// </summary>
    public class PayPalPurchaseUnit
    {
        [JsonPropertyName("reference_id")]
        public string? ReferenceId { get; set; }

        [JsonPropertyName("amount")]
        public PayPalAmount? Amount { get; set; }

        [JsonPropertyName("payee")]
        public PayPalPayee? Payee { get; set; }

        [JsonPropertyName("custom_id")]
        public string? CustomId { get; set; }

        [JsonPropertyName("invoice_id")]
        public string? InvoiceId { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("payments")]
        public PayPalPayments? Payments { get; set; }
    }

    /// <summary>
    /// Payments within a purchase unit.
    /// </summary>
    public class PayPalPayments
    {
        [JsonPropertyName("captures")]
        public List<PayPalCaptureDetails>? Captures { get; set; }
    }

    #endregion

    #region Verification Results

    /// <summary>
    /// Result of PayPal IPN or Webhook verification.
    /// </summary>
    public class PayPalVerificationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public PayPalVerificationMethod Method { get; set; }

        /// <summary>Extracted payer email from verification (for webhook API lookups).</summary>
        public string? PayerEmail { get; set; }

        /// <summary>Extracted payer name from verification.</summary>
        public string? PayerName { get; set; }

        public static PayPalVerificationResult Success(PayPalVerificationMethod method, string? payerEmail = null, string? payerName = null)
            => new() { IsValid = true, Method = method, PayerEmail = payerEmail, PayerName = payerName };

        public static PayPalVerificationResult Failure(string error, PayPalVerificationMethod method)
            => new() { IsValid = false, ErrorMessage = error, Method = method };
    }

    /// <summary>
    /// Method used for PayPal verification.
    /// </summary>
    public enum PayPalVerificationMethod
    {
        /// <summary>Legacy IPN postback verification.</summary>
        IpnPostback,

        /// <summary>Webhook signature verification via PayPal API.</summary>
        WebhookSignature,

        /// <summary>Transaction lookup via PayPal API.</summary>
        ApiLookup
    }

    #endregion
}
