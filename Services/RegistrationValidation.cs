using System.Text.RegularExpressions;

namespace ExamPortal.Services
{
    /// <summary>
    /// Pure, dependency-free validation/normalization rules for the candidate
    /// registration form (Full Name, Phone Number, Password) — see
    /// CandidateRegistrationViewModel.Validate (Models/PortalModels.cs), where these
    /// are the final, authoritative checks. The client-side mirror in
    /// wwwroot/js/app.js's registration-validation module is UX-only — keep both in
    /// sync if these rules ever change, but never treat the client copy as the
    /// source of truth.
    ///
    /// Disposable-email domain checking lives separately in DisposableEmailService
    /// (it needs configuration, so isn't a good fit for a static helper); duplicate
    /// email/phone checks stay in AccountController.Registration.cs since they need
    /// database access.
    /// </summary>
    public static class RegistrationValidation
    {
        // Starts and ends with a letter (any script); allows letters, spaces,
        // hyphens, apostrophes, and periods in between. Rejects anything
        // starting/ending in punctuation or digits outright — "123456", "!!!!!!",
        // and "@@@@@" all fail here before RepeatedPunctuationPattern even runs.
        private static readonly Regex FullNamePattern = new(@"^[\p{L}][\p{L}\s.'-]*[\p{L}.]$", RegexOptions.Compiled);

        // Blocks any run of 3+ consecutive space/period/apostrophe/hyphen characters
        // (e.g. "Mary----Jane", "A....", "a   b   c") without rejecting a single
        // legitimate use of any of them ("Mary-Jane", "O'Connor", "A. Kumar").
        private static readonly Regex RepeatedPunctuationPattern = new(@"[\s.'-]{3,}", RegexOptions.Compiled);

        private static readonly Regex CollapseSpaces = new(@" {2,}", RegexOptions.Compiled);

        private static readonly Regex IndianMobilePattern = new(@"^[6-9]\d{9}$", RegexOptions.Compiled);

        /// <summary>Trims and collapses runs of spaces down to one — does not touch
        /// hyphens/apostrophes/periods, which are meaningful in names like
        /// "Mary-Jane" or "O'Connor" and must never be silently rewritten.</summary>
        public static string NormalizeFullName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "";
            return CollapseSpaces.Replace(fullName.Trim(), " ");
        }

        /// <summary>True for names like "Mahender Boddu", "A. Kumar", "Mary-Jane", and
        /// "O'Connor". Expects an already-normalized value (see NormalizeFullName).</summary>
        public static bool IsValidFullName(string normalizedFullName)
        {
            if (string.IsNullOrWhiteSpace(normalizedFullName)) return false;
            if (!FullNamePattern.IsMatch(normalizedFullName)) return false;
            if (RepeatedPunctuationPattern.IsMatch(normalizedFullName)) return false;
            // At least 2 actual letters — guards against single-letter-plus-punctuation
            // junk technically matching the pattern above (e.g. a lone "A").
            var letterCount = 0;
            foreach (var c in normalizedFullName)
                if (char.IsLetter(c)) letterCount++;
            return letterCount >= 2;
        }

        /// <summary>Strips spaces, hyphens, and parentheses, and an optional leading
        /// country code (+91 / 91) or trunk 0, so "+91 98765 43210", "091-98765-43210",
        /// and "9876543210" all normalize to the same 10-digit value before
        /// validation/storage.</summary>
        public static string NormalizePhoneNumber(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber)) return "";
            var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
            if (digitsOnly.Length == 12 && digitsOnly.StartsWith("91")) digitsOnly = digitsOnly[2..];
            else if (digitsOnly.Length == 11 && digitsOnly.StartsWith("0")) digitsOnly = digitsOnly[1..];
            return digitsOnly;
        }

        /// <summary>Valid Indian mobile numbers are exactly 10 digits starting with
        /// 6-9. Expects an already-normalized value (see NormalizePhoneNumber). Also
        /// rejects an all-identical-digit number (e.g. "6666666666", "9999999999") —
        /// technically matches the format but is never a real number; "0000000000"
        /// and "1111111111" are already excluded by the leading-digit rule alone.</summary>
        public static bool IsValidIndianMobileNumber(string normalizedPhoneNumber)
        {
            if (!IndianMobilePattern.IsMatch(normalizedPhoneNumber)) return false;
            return normalizedPhoneNumber.Distinct().Count() > 1;
        }

        // Not remotely exhaustive — a full breach-password corpus belongs in an
        // external, k-anonymity-style check (e.g. HaveIBeenPwned's range API), which
        // this project doesn't currently call (see DisposableEmailService's doc
        // comment for the same reasoning re: network dependencies on the
        // registration path). This list exists to catch the most obvious, most
        // commonly-typed weak passwords server-side, on top of the character-class
        // requirement below.
        private static readonly HashSet<string> CommonWeakPasswords = new(StringComparer.OrdinalIgnoreCase)
        {
            "password", "password1", "password123", "12345678", "123456789", "1234567890",
            "qwerty123", "qwertyuiop", "letmein", "welcome1", "welcome123", "admin123",
            "iloveyou1", "monkey123", "dragon123", "abc12345", "football1", "baseball1",
            "trustno1", "sunshine1", "princess1", "1q2w3e4r", "passw0rd", "changeme1",
            "abcd1234", "test1234", "qazwsx123", "zaq12wsx", "asdfghjk1", "1234abcd",
        };

        /// <summary>Requires 8+ characters (matches the model's existing [MinLength(8)])
        /// plus at least 3 of the 4 character classes (lowercase/uppercase/digit/
        /// special) — roughly the "Fair" tier on the client-side strength meter in
        /// app.js, so the server never rejects something the meter visually approved.
        /// Also blocks a short list of extremely common passwords.</summary>
        public static bool IsPasswordStrongEnough(string password, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(password)) { error = "Password is required."; return false; }
            if (password.Length < 8) { error = "Password must be at least 8 characters."; return false; }
            if (password.Length > 128) { error = "Password must be 128 characters or fewer."; return false; }

            var classes = 0;
            if (password.Any(char.IsLower)) classes++;
            if (password.Any(char.IsUpper)) classes++;
            if (password.Any(char.IsDigit)) classes++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
            if (classes < 3)
            {
                error = "Password must include at least 3 of the following: lowercase letters, uppercase letters, numbers, and special characters.";
                return false;
            }

            if (CommonWeakPasswords.Contains(password))
            {
                error = "This password is too common. Please choose a stronger password.";
                return false;
            }

            return true;
        }
    }
}
