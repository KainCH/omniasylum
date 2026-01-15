using System;
using System.ComponentModel.DataAnnotations;

namespace OmniForge.Web.Validation
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SafeTextAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is null)
            {
                return ValidationResult.Success;
            }

            if (value is not string text)
            {
                return ValidationResult.Success;
            }

            foreach (var ch in text)
            {
                // Allow common whitespace controls, reject other control chars.
                if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
                {
                    return new ValidationResult($"{validationContext.DisplayName} contains invalid characters.");
                }

                // Reject bidi override/isolate controls commonly used for spoofing.
                if (ch is '\u202A' or '\u202B' or '\u202D' or '\u202E' or '\u202C'
                    or '\u2066' or '\u2067' or '\u2068' or '\u2069')
                {
                    return new ValidationResult($"{validationContext.DisplayName} contains invalid characters.");
                }
            }

            return ValidationResult.Success;
        }
    }
}
