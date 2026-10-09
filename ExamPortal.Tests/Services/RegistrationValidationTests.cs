using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class RegistrationValidationTests
    {
        // ── Full Name ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Mahender Boddu")]
        [InlineData("A. Kumar")]
        [InlineData("Mary-Jane")]
        [InlineData("O'Connor")]
        [InlineData("Jo")]
        public void IsValidFullName_AcceptsRealNames(string name)
        {
            var normalized = RegistrationValidation.NormalizeFullName(name);
            Assert.True(RegistrationValidation.IsValidFullName(normalized));
        }

        [Theory]
        [InlineData("123456")]
        [InlineData("!!!!!!")]
        [InlineData("@@@@@")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("A")] // single letter — below the 2-letter minimum
        [InlineData("Mary----Jane")] // unreasonable repeated punctuation
        [InlineData("A....")]
        public void IsValidFullName_RejectsJunk(string name)
        {
            var normalized = RegistrationValidation.NormalizeFullName(name);
            Assert.False(RegistrationValidation.IsValidFullName(normalized));
        }

        [Fact]
        public void NormalizeFullName_TrimsAndCollapsesSpacesWithoutTouchingPunctuation()
        {
            Assert.Equal("John Smith", RegistrationValidation.NormalizeFullName("  John    Smith  "));
            Assert.Equal("O'Connor", RegistrationValidation.NormalizeFullName("O'Connor"));
            Assert.Equal("Mary-Jane", RegistrationValidation.NormalizeFullName("Mary-Jane"));
        }

        // ── Phone Number ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("9876543210", "9876543210")]
        [InlineData("+91 98765 43210", "9876543210")]
        [InlineData("09876543210", "9876543210")] // leading trunk 0
        public void NormalizePhoneNumber_StripsFormattingAndPrefixes(string input, string expected)
        {
            Assert.Equal(expected, RegistrationValidation.NormalizePhoneNumber(input));
        }

        [Theory]
        [InlineData("9876543210")]
        [InlineData("6000000001")]
        public void IsValidIndianMobileNumber_AcceptsRealNumbers(string normalized)
        {
            Assert.True(RegistrationValidation.IsValidIndianMobileNumber(normalized));
        }

        [Theory]
        [InlineData("0000000000")]
        [InlineData("1111111111")]
        [InlineData("6666666666")] // valid leading digit, but all-identical — rejected as obviously fake
        [InlineData("12345")]
        [InlineData("99999999999999")]
        [InlineData("")]
        public void IsValidIndianMobileNumber_RejectsInvalid(string normalized)
        {
            Assert.False(RegistrationValidation.IsValidIndianMobileNumber(normalized));
        }

        [Fact]
        public void NormalizePhoneNumber_TenDigitAllSameDigit_StillRejectedAfterNormalization()
        {
            var normalized = RegistrationValidation.NormalizePhoneNumber("9999999999");
            Assert.False(RegistrationValidation.IsValidIndianMobileNumber(normalized));
        }

        // ── Phone Number: allowed characters (checked on the ORIGINAL input) ──────

        [Theory]
        [InlineData("9876543210")]
        [InlineData("+91 98765 43210")]
        [InlineData("+919876543210")]
        [InlineData("(+91) 98765 43210")]
        [InlineData("+91-98765-43210")]
        [InlineData("09876543210")]
        [InlineData("(098) 7654-3210")]
        [InlineData("  9876543210  ")]
        [InlineData("")]   // blank is left to the Required / valid-number checks
        [InlineData(null)]
        public void HasOnlyAllowedPhoneCharacters_AcceptsDigitsAndPhoneFormatting(string? input)
        {
            Assert.True(RegistrationValidation.HasOnlyAllowedPhoneCharacters(input));
        }

        [Theory]
        [InlineData("98765abc43210")]      // letters in the middle — used to normalize to a "valid" number
        [InlineData("abcdefghij")]
        [InlineData("9876543210x")]
        [InlineData("98765#43210")]
        [InlineData("98765.43210")]
        [InlineData("98765/43210")]
        [InlineData("9876+543210")]        // "+" anywhere except as the +91 prefix
        [InlineData("++91 9876543210")]
        [InlineData("+44 9876543210")]     // only a +91 prefix is supported
        [InlineData("+91 98765 43210 ext 5")]
        [InlineData("९८७६५४३२१०")]          // non-ASCII (Devanagari) digits
        [InlineData("<script>")]
        public void HasOnlyAllowedPhoneCharacters_RejectsLettersAndOtherCharacters(string input)
        {
            Assert.False(RegistrationValidation.HasOnlyAllowedPhoneCharacters(input));
        }

        [Fact]
        public void NormalizePhoneNumber_AloneWouldAcceptLetters_WhichIsWhyTheCharacterCheckMustRunFirst()
        {
            // Documents the original bug: normalization silently drops non-digits, so on its own
            // it turns this into a valid-looking number. CandidateRegistrationViewModel.Validate
            // therefore calls HasOnlyAllowedPhoneCharacters on the raw input before normalizing.
            var normalized = RegistrationValidation.NormalizePhoneNumber("98765abc43210");
            Assert.True(RegistrationValidation.IsValidIndianMobileNumber(normalized));
            Assert.False(RegistrationValidation.HasOnlyAllowedPhoneCharacters("98765abc43210"));
        }

        // ── Password ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Str0ng!Pass")]
        [InlineData("C0mplex#2026")]
        public void IsPasswordStrongEnough_AcceptsStrongPasswords(string password)
        {
            Assert.True(RegistrationValidation.IsPasswordStrongEnough(password, out var error));
            Assert.Equal("", error);
        }

        [Theory]
        [InlineData("short1A")]      // under 8 chars
        [InlineData("alllowercase1")] // only 2 classes (lower + digit)
        [InlineData("password123")]  // common password, even though 3 classes... actually only 2 classes here too
        [InlineData("")]
        public void IsPasswordStrongEnough_RejectsWeakPasswords(string password)
        {
            Assert.False(RegistrationValidation.IsPasswordStrongEnough(password, out var error));
            Assert.NotEqual("", error);
        }

        [Fact]
        public void IsPasswordStrongEnough_RejectsCommonPasswordEvenIfComplexEnough()
        {
            // "Passw0rd" — meets the character-class bar (upper, lower, digit) but is
            // still on the common-password blocklist (case-insensitive).
            Assert.False(RegistrationValidation.IsPasswordStrongEnough("Passw0rd", out var error));
            Assert.Contains("too common", error);
        }

        [Fact]
        public void IsPasswordStrongEnough_RejectsExcessivelyLongPassword()
        {
            var tooLong = new string('a', 129) + "A1!";
            Assert.False(RegistrationValidation.IsPasswordStrongEnough(tooLong, out var error));
        }
    }
}
