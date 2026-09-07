using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamPortal.Models;
using Xunit;

namespace ExamPortal.Tests.Models
{
    public class ResetPasswordViewModelTests
    {
        private static List<ValidationResult> Validate(ResetPasswordViewModel model)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
            return results;
        }

        [Fact]
        public void StrongPassword_NoValidationErrors()
        {
            var model = new ResetPasswordViewModel
            {
                Email = "candidate@example.com",
                Token = "sometoken",
                NewPassword = "Str0ng!Pass",
                ConfirmPassword = "Str0ng!Pass",
            };

            var results = Validate(model);

            Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(ResetPasswordViewModel.NewPassword)));
        }

        [Fact]
        public void CommonPassword_RejectedEvenIfLongEnough()
        {
            // Regression coverage: this used to only check [StringLength(100,
            // MinimumLength = 8)] — a password registration would now reject (common,
            // or missing character variety) could previously still be set via reset.
            var model = new ResetPasswordViewModel
            {
                Email = "candidate@example.com",
                Token = "sometoken",
                NewPassword = "Passw0rd",
                ConfirmPassword = "Passw0rd",
            };

            var results = Validate(model);

            Assert.Contains(results, r => r.MemberNames.Contains(nameof(ResetPasswordViewModel.NewPassword)));
        }

        [Fact]
        public void WeakCharacterVariety_Rejected()
        {
            var model = new ResetPasswordViewModel
            {
                Email = "candidate@example.com",
                Token = "sometoken",
                NewPassword = "alllowercase1", // only 2 character classes
                ConfirmPassword = "alllowercase1",
            };

            var results = Validate(model);

            Assert.Contains(results, r => r.MemberNames.Contains(nameof(ResetPasswordViewModel.NewPassword)));
        }
    }
}
