using System.Security.Claims;
using ExamPortal.Data;
using ExamPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamPortal.Middleware
{
    /// <summary>
    /// Blocks a signed-in-but-not-yet-email-verified candidate from reaching the
    /// dashboard or any other candidate portal page.
    ///
    /// Registration signs the candidate in immediately (AccountController.Registration.cs
    /// Register() calls SignInUserAsync right after QueueEmailOtp), before they've entered
    /// their OTP, and an existing unverified account behaves the same way after a normal
    /// Login. [Authorize(Roles = PortalRoles.Candidate)] on HomeController/ExamController/etc.
    /// only proves "this is a signed-in candidate" — it says nothing about
    /// User.IsEmailVerified — so without this gate, a candidate who never opens the OTP
    /// email can navigate straight to e.g. /Home/Index or /Exam/Take and skip verification
    /// entirely. Checked against the existing routing/middleware before adding this: no
    /// existing middleware or filter enforces verification, and RedirectToPortal
    /// (Controllers/AccountController.cs) only steers a candidate towards VerifyEmailOtp at
    /// the moment they log in — it can't stop a later direct navigation within the same
    /// session.
    ///
    /// Reads User.IsEmailVerified fresh from the database on every gated request rather
    /// than trusting a claim on the auth cookie. That keeps this correct for sessions that
    /// were signed in before this middleware existed (an already-verified candidate with an
    /// old cookie is never wrongly blocked) and for the moment verification itself succeeds
    /// (VerifyEmailOtp re-signs the candidate in immediately after, but even a request that
    /// raced ahead of that would see the up-to-date database value here). The cost is one
    /// indexed lookup by primary key, and only for authenticated candidates on paths outside
    /// the allow-list below — not on every request.
    ///
    /// Registered in Program.cs via app.UseCandidateEmailVerificationGate(), after
    /// UseAuthentication() (needs HttpContext.User populated) and before UseAuthorization(),
    /// so an unverified candidate is redirected before the request ever reaches an
    /// [Authorize]-protected action. Matches on the raw request path — the same approach
    /// IpAllowListMiddleware already uses — rather than endpoint metadata, so protection
    /// doesn't depend on routing/attribute details of whichever controller handles the
    /// request.
    ///
    /// Static assets (css/js/images under wwwroot) never reach this middleware at all:
    /// app.UseStaticFiles() runs earlier in the pipeline and short-circuits those requests
    /// before authentication (and this gate) ever runs.
    /// </summary>
    public class CandidateEmailVerificationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CandidateEmailVerificationMiddleware> _logger;

        /// <summary>
        /// Exact-path allow-list of what an unverified-but-signed-in candidate may still
        /// reach: the OTP verification page itself, the resend action, and logout. Everything
        /// else in the candidate portal is blocked until IsEmailVerified flips to true. Kept
        /// as an exact match (not a prefix) so this can't accidentally be widened by adding a
        /// new /Account/... action later.
        /// </summary>
        private static readonly string[] AllowedPaths =
        {
            "/Account/VerifyEmailOtp",
            "/Account/ResendEmailOtp",
            "/Account/Logout",
        };

        public CandidateEmailVerificationMiddleware(RequestDelegate next, ILogger<CandidateEmailVerificationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            var user = context.User;

            // Anonymous requests and every non-candidate role (Admin/Recruiter/HR, or
            // whatever [Authorize] itself will reject) are none of this gate's business.
            if (user.Identity?.IsAuthenticated != true || !user.IsInRole(PortalRoles.Candidate))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? "";
            if (AllowedPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
            {
                // No parseable identity claim — nothing this gate can act on; let
                // downstream auth handle it (it will reject an incomplete principal anyway).
                await _next(context);
                return;
            }

            var isVerified = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (bool?)u.IsEmailVerified)
                .FirstOrDefaultAsync();

            // isVerified is null only if the Id from the cookie no longer matches any user
            // (deleted account) — fail closed, same as an unverified candidate, rather than
            // let a stale/invalid identity through.
            if (isVerified == true)
            {
                await _next(context);
                return;
            }

            _logger.LogInformation(
                "Blocked unverified candidate {UserId} from {Path}; redirecting to email verification.",
                userId, path);

            context.Response.Redirect("/Account/VerifyEmailOtp");
        }
    }

    public static class CandidateEmailVerificationMiddlewareExtensions
    {
        public static IApplicationBuilder UseCandidateEmailVerificationGate(this IApplicationBuilder app)
        {
            return app.UseMiddleware<CandidateEmailVerificationMiddleware>();
        }
    }
}
