using System.ComponentModel.DataAnnotations;

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
        public string Title { get; set; } = "";

        [Required]
        [MinLength(10)]
        [MaxLength(4000)]
        public string Description { get; set; } = "";

        [MaxLength(4000)]
        public string? StepsToReproduce { get; set; }

        [MaxLength(4000)]
        public string? ExpectedBehavior { get; set; }

        [MaxLength(4000)]
        public string? ActualBehavior { get; set; }

        [MaxLength(4000)]
        public string? AdditionalInfo { get; set; }
    }
}
