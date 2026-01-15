using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OmniForge.Web.Models;
using Xunit;

namespace OmniForge.Tests
{
    public class CreateGitHubIssueRequestValidationTests
    {
        [Fact]
        public void Validate_ShouldFail_WhenTitleContainsControlChars()
        {
            var request = new CreateGitHubIssueRequest
            {
                Type = "bug",
                Title = "Bad\u0000Title",
                Description = "This is a valid description with enough length."
            };

            var results = Validate(request);

            Assert.NotEmpty(results);
        }

        [Fact]
        public void Validate_ShouldFail_WhenDescriptionContainsBidiOverride()
        {
            var request = new CreateGitHubIssueRequest
            {
                Type = "feature",
                Title = "A valid title",
                Description = "This contains a bidi override: \u202E and should be rejected."
            };

            var results = Validate(request);

            Assert.NotEmpty(results);
        }

        [Fact]
        public void Validate_ShouldPass_ForNormalText()
        {
            var request = new CreateGitHubIssueRequest
            {
                Type = "bug",
                Title = "Something broke",
                Description = "It crashes when I click the button, please investigate."
            };

            var results = Validate(request);

            Assert.Empty(results);
        }

        private static List<ValidationResult> Validate(CreateGitHubIssueRequest request)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
            return results;
        }
    }
}
