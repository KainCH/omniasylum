using System.ComponentModel.DataAnnotations;
using OmniForge.Web.Validation;

namespace OmniForge.Web.Models
{
    public class CreateGitHubIssueRequest
    {
        [Required]
        [MaxLength(20)]
        public string Type { get; set; } = "bug";

        [Required]
        [MinLength(5)]
        [MaxLength(120)]
        [SafeText]
        public string Title { get; set; } = "";

        [Required]
        [MinLength(10)]
        [MaxLength(4000)]
        [SafeText]
        public string Description { get; set; } = "";

        [MaxLength(4000)]
        [SafeText]
        public string? StepsToReproduce { get; set; }

        [MaxLength(4000)]
        [SafeText]
        public string? ExpectedBehavior { get; set; }

        [MaxLength(4000)]
        [SafeText]
        public string? ActualBehavior { get; set; }

        [MaxLength(4000)]
        [SafeText]
        public string? AdditionalInfo { get; set; }
    }
}
