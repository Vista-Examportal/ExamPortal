using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ExamPortal.Models;
using Xunit;

namespace ExamPortal.Tests.Models
{
    // The registration form's phone number must be validated on the ORIGINAL input, before it is
    // normalized to 10 digits. Same Validator-based approach as ResetPasswordViewModelTests; every
    // other field is valid so the model-level Validate() (IValidatableObject) actually runs.
    public class CandidateRegistrationViewModelTests
    {
        private const string CharsMessage =
            "Phone number can contain only digits, spaces, hyphens, parentheses, and an optional +91 prefix.";

        private static CandidateRegistrationViewModel ModelWithPhone(string phone) => new()
        {
            FullName = "Mahender Boddu",
            Email = "candidate@example.com",
            PhoneNumber = phone,
            Password = "Str0ng!Pass",
            ConfirmPassword = "Str0ng!Pass",
        };

        private static List<ValidationResult> Validate(CandidateRegistrationViewModel model)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
            return results;
        }

        private static List<ValidationResult> PhoneErrors(List<ValidationResult> results) =>
            results.Where(r => r.MemberNames.Contains(nameof(CandidateRegistrationViewModel.PhoneNumber))).ToList();

        [Theory]
        [InlineData("98765abc43210")]
        [InlineData("abcdefghij")]
        [InlineData("9876543210x")]
        [InlineData("98765#43210")]
        public void PhoneWithLettersOrSymbols_IsRejected_WithCharacterMessage(string phone)
        {
            var model = ModelWithPhone(phone);

            var errors = PhoneErrors(Validate(model));

            var error = Assert.Single(errors);
            Assert.Equal(CharsMessage, error.ErrorMessage);
            Assert.Equal(phone, model.PhoneNumber); // not normalized/stored when rejected
        }

        [Theory]
        [InlineData("9876543210")]
        [InlineData("+91 98765 43210")]
        [InlineData("+919876543210")]
        [InlineData("(+91) 98765 43210")]
        [InlineData("+91-98765-43210")]
        [InlineData("09876543210")]
        public void SupportedIndianMobileFormats_AreAccepted_AndStoredAsTenDigits(string phone)
        {
            var model = ModelWithPhone(phone);

            var errors = PhoneErrors(Validate(model));

            Assert.Empty(errors);
            Assert.Equal("9876543210", model.PhoneNumber);
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("6666666666")]
        public void OtherwiseInvalidNumbers_KeepTheExistingNumberMessage(string phone)
        {
            var errors = PhoneErrors(Validate(ModelWithPhone(phone)));

            var error = Assert.Single(errors);
            Assert.Equal("Enter a valid 10-digit Indian mobile number.", error.ErrorMessage);
        }
    }
}
