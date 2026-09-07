using System;
using System.Security.Claims;
using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class ClaimsPrincipalExtensionsTests
    {
        private static ClaimsPrincipal BuildPrincipal(string? nameIdentifier)
        {
            var claims = new System.Collections.Generic.List<Claim>();
            if (nameIdentifier != null)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));

            var identity = new ClaimsIdentity(claims, "TestAuthType");
            return new ClaimsPrincipal(identity);
        }

        [Fact]
        public void GetUserId_ValidNumericClaim_ReturnsParsedId()
        {
            var principal = BuildPrincipal("42");

            var id = principal.GetUserId();

            Assert.Equal(42, id);
        }

        [Fact]
        public void GetUserId_MissingClaim_ThrowsArgumentNullException()
        {
            // Only valid to call from an [Authorize]-protected action where the claim is
            // guaranteed present — this documents/pins that calling it without one throws
            // loudly rather than silently returning 0, which would be far more dangerous
            // (e.g. accidentally operating on "user 0").
            var principal = BuildPrincipal(null);

            Assert.Throws<ArgumentNullException>(() => principal.GetUserId());
        }

        [Fact]
        public void GetUserId_NonNumericClaim_ThrowsFormatException()
        {
            var principal = BuildPrincipal("not-a-number");

            Assert.Throws<FormatException>(() => principal.GetUserId());
        }
    }
}
