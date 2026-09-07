using System.Security.Cryptography;
using System.Text;

namespace ExamPortal.Services
{
    /// <summary>Generates the 6-digit numeric codes used for email/mobile OTPs and one-time login tokens.</summary>
    public static class SecureCodeGenerator
    {
        /// <summary>
        /// Generates a cryptographically secure 6-digit numeric code (100000-999999) for
        /// email/mobile OTPs and one-time login tokens. Uses RandomNumberGenerator rather than
        /// Random.Shared because a one-time login token in particular can substitute entirely
        /// for a password, so its value must not be predictable.
        /// </summary>
        public static string GenerateNumericCode() =>
            RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        /// <summary>
        /// Generates a cryptographically secure, URL-safe token for higher-stakes one-time links
        /// (currently: password reset). Uses RandomNumberGenerator rather than Random.Shared because
        /// this token grants account takeover if guessed, unlike a 6-digit OTP that is rate-limited
        /// and paired with a known email address.
        /// </summary>
        public static string GenerateUrlSafeToken(int byteLength = 32) =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        /// <summary>
        /// One-way SHA-256 hash of a token for storage. We never persist the raw reset token —
        /// only its hash — so that a database leak alone cannot be used to reset a password.
        /// </summary>
        public static string HashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
