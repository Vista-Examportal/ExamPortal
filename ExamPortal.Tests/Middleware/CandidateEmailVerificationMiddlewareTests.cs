using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ExamPortal.Data;
using ExamPortal.Middleware;
using ExamPortal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExamPortal.Tests.Middleware
{
    // CandidateEmailVerificationMiddleware is the gate that stops a signed-in-but-not-
    // yet-OTP-verified candidate from reaching the dashboard or any other candidate
    // portal page (see the class's own doc comment for the full rationale). These tests
    // build it against a real EF Core in-memory AppDbContext — the same pattern used
    // throughout ExamPortal.Tests.Services — rather than mocking IsEmailVerified lookups,
    // since the whole point of reading fresh from the database (instead of trusting a
    // claim on the auth cookie) is what's under test here.
    public class CandidateEmailVerificationMiddlewareTests
    {
        private static AppDbContext BuildContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static User SeedCandidate(AppDbContext db, bool isEmailVerified)
        {
            var user = new User
            {
                Username = "VWT202600001",
                Email = "candidate@example.com",
                PasswordHash = "not-a-real-hash",
                FullName = "Test Candidate",
                Role = PortalRoles.Candidate,
                IsEmailVerified = isEmailVerified,
            };
            db.Users.Add(user);
            db.SaveChanges();
            return user;
        }

        private static (CandidateEmailVerificationMiddleware Middleware, DefaultHttpContext Context, Func<bool> WasNextCalled) Build(
            string path, ClaimsPrincipal? user = null)
        {
            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new CandidateEmailVerificationMiddleware(next, NullLogger<CandidateEmailVerificationMiddleware>.Instance);

            var context = new DefaultHttpContext();
            context.Request.Path = path;
            if (user != null) context.User = user;

            return (middleware, context, () => nextCalled);
        }

        private static ClaimsPrincipal CandidatePrincipal(int userId)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, PortalRoles.Candidate),
            }, authenticationType: "TestAuth");
            return new ClaimsPrincipal(identity);
        }

        private static ClaimsPrincipal StaffPrincipal(int userId, string role)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
            }, authenticationType: "TestAuth");
            return new ClaimsPrincipal(identity);
        }

        [Fact]
        public async Task UnverifiedCandidate_RequestingDashboard_IsBlockedAndRedirectedToVerify()
        {
            using var db = BuildContext();
            var candidate = SeedCandidate(db, isEmailVerified: false);
            var (middleware, context, wasNextCalled) = Build("/Home/Index", CandidatePrincipal(candidate.Id));

            await middleware.InvokeAsync(context, db);

            Assert.False(wasNextCalled());
            Assert.Equal("/Account/VerifyEmailOtp", context.Response.Headers.Location.ToString());
        }

        [Fact]
        public async Task UnverifiedCandidate_RequestingExamPage_IsBlocked()
        {
            using var db = BuildContext();
            var candidate = SeedCandidate(db, isEmailVerified: false);
            var (middleware, context, wasNextCalled) = Build("/Exam/Take", CandidatePrincipal(candidate.Id));

            await middleware.InvokeAsync(context, db);

            Assert.False(wasNextCalled());
        }

        [Theory]
        [InlineData("/Account/VerifyEmailOtp")]
        [InlineData("/Account/ResendEmailOtp")]
        [InlineData("/Account/Logout")]
        public async Task UnverifiedCandidate_RequestingAllowedPath_IsNotBlocked(string path)
        {
            using var db = BuildContext();
            var candidate = SeedCandidate(db, isEmailVerified: false);
            var (middleware, context, wasNextCalled) = Build(path, CandidatePrincipal(candidate.Id));

            await middleware.InvokeAsync(context, db);

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task UnverifiedCandidate_AllowedPathMatchIsCaseInsensitive()
        {
            using var db = BuildContext();
            var candidate = SeedCandidate(db, isEmailVerified: false);
            var (middleware, context, wasNextCalled) = Build("/account/verifyemailotp", CandidatePrincipal(candidate.Id));

            await middleware.InvokeAsync(context, db);

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task VerifiedCandidate_RequestingDashboard_IsNotBlocked()
        {
            using var db = BuildContext();
            var candidate = SeedCandidate(db, isEmailVerified: true);
            var (middleware, context, wasNextCalled) = Build("/Home/Index", CandidatePrincipal(candidate.Id));

            await middleware.InvokeAsync(context, db);

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task UnauthenticatedRequest_IsNotBlocked()
        {
            using var db = BuildContext();
            var (middleware, context, wasNextCalled) = Build("/Home/Index", user: null);

            await middleware.InvokeAsync(context, db);

            Assert.True(wasNextCalled());
        }

        [Theory]
        [InlineData(PortalRoles.Admin)]
        [InlineData(PortalRoles.Recruiter)]
        [InlineData(PortalRoles.Hr)]
        public async Task NonCandidateRole_IsNeverBlocked_EvenIfUnverifiedInDatabase(string role)
        {
            using var db = BuildContext();
            // Same user row an unverified candidate would have — but signed in under a
            // staff role, which this gate must never touch.
            var staffUser = new User
            {
                Username = "staff1",
                Email = "staff@example.com",
                PasswordHash = "not-a-real-hash",
                FullName = "Staff Member",
                Role = role,
                IsEmailVerified = false,
            };
            db.Users.Add(staffUser);
            db.SaveChanges();
            var (middleware, context, wasNextCalled) = Build("/Admin/Index", StaffPrincipal(staffUser.Id, role));

            await middleware.InvokeAsync(context, db);

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task VerifiedCandidate_WithStaleSessionPredatingThisMiddleware_IsNotBlocked()
        {
            // Regression coverage: the gate must read IsEmailVerified from the database on
            // every request rather than trusting a claim on the auth cookie, precisely so an
            // already-verified candidate's pre-existing session (issued before this
            // middleware existed, and so carrying no such claim) is never wrongly blocked.
            using var db = BuildContext();
            var candidate = SeedCandidate(db, isEmailVerified: true);
            // A minimal principal with only NameIdentifier + Role — no extra claims —
            // standing in for an old auth cookie issued by pre-fix code.
            var (middleware, context, wasNextCalled) = Build("/Home/Index", CandidatePrincipal(candidate.Id));

            await middleware.InvokeAsync(context, db);

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task UnknownOrDeletedUserId_FailsClosed()
        {
            using var db = BuildContext();
            var (middleware, context, wasNextCalled) = Build("/Home/Index", CandidatePrincipal(userId: 999));

            await middleware.InvokeAsync(context, db);

            Assert.False(wasNextCalled());
        }
    }
}
