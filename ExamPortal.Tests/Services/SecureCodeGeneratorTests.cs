using System.Linq;
using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class SecureCodeGeneratorTests
    {
        [Fact]
        public void GenerateNumericCode_IsSixDigits()
        {
            var code = SecureCodeGenerator.GenerateNumericCode();

            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.True(char.IsDigit(c)));
        }

        [Fact]
        public void GenerateNumericCode_IsWithinExpectedRange()
        {
            // RandomNumberGenerator.GetInt32(100000, 1000000) is upper-exclusive, so the
            // valid range is 100000-999999 — anything outside that (e.g. a leading zero
            // making it fewer than 6 digits) would be a real bug for an OTP field.
            var code = SecureCodeGenerator.GenerateNumericCode();
            var value = int.Parse(code);

            Assert.InRange(value, 100000, 999999);
        }

        [Fact]
        public void GenerateNumericCode_ProducesVaryingValues()
        {
            // Not a strict randomness/entropy test — just a sanity check that this isn't
            // somehow returning a constant. With a 900,000-value range, 25 draws all
            // landing on the same number would indicate something is badly broken.
            var codes = Enumerable.Range(0, 25).Select(_ => SecureCodeGenerator.GenerateNumericCode()).Distinct();

            Assert.True(codes.Count() > 1);
        }

        [Fact]
        public void GenerateUrlSafeToken_ContainsNoUnsafeCharacters()
        {
            var token = SecureCodeGenerator.GenerateUrlSafeToken();

            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
        }

        [Fact]
        public void GenerateUrlSafeToken_IsNotEmpty()
        {
            Assert.NotEmpty(SecureCodeGenerator.GenerateUrlSafeToken());
        }

        [Fact]
        public void GenerateUrlSafeToken_LongerByteLengthProducesLongerToken()
        {
            var shortToken = SecureCodeGenerator.GenerateUrlSafeToken(8);
            var longToken = SecureCodeGenerator.GenerateUrlSafeToken(64);

            Assert.True(longToken.Length > shortToken.Length);
        }

        [Fact]
        public void GenerateUrlSafeToken_ProducesDifferentValuesEachCall()
        {
            var first = SecureCodeGenerator.GenerateUrlSafeToken();
            var second = SecureCodeGenerator.GenerateUrlSafeToken();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void HashToken_EmptyString_MatchesKnownSha256Vector()
        {
            // Canonical, widely-published SHA-256 hash of the empty string — verifies
            // HashToken is really doing SHA-256 (Convert.ToHexString is uppercase, hence
            // the case-insensitive comparison rather than assuming casing).
            const string expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85";

            var actual = SecureCodeGenerator.HashToken("");

            Assert.Equal(expected, actual, ignoreCase: true);
        }

        [Fact]
        public void HashToken_KnownInput_MatchesKnownSha256Vector()
        {
            // Canonical NIST test vector for SHA-256("abc").
            const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

            var actual = SecureCodeGenerator.HashToken("abc");

            Assert.Equal(expected, actual, ignoreCase: true);
        }

        [Fact]
        public void HashToken_IsDeterministic()
        {
            // The whole point of hashing a reset token before storage is that the same
            // input always hashes to the same value, so it can be looked up again later.
            var first = SecureCodeGenerator.HashToken("some-reset-token-value");
            var second = SecureCodeGenerator.HashToken("some-reset-token-value");

            Assert.Equal(first, second);
        }

        [Fact]
        public void HashToken_DifferentInputs_ProduceDifferentHashes()
        {
            var a = SecureCodeGenerator.HashToken("token-a");
            var b = SecureCodeGenerator.HashToken("token-b");

            Assert.NotEqual(a, b);
        }
    }
}
