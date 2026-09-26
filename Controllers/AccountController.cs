using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExamPortal.Controllers
{
    // Split across AccountController.cs (this file: shared fields/constructor and
    // small cross-cutting helpers used by more than one flow) plus
    // AccountController.Auth.cs, .Registration.cs, .PasswordReset.cs, and .OfferResponse.cs.
    // All are the same partial class — same fields, same DB context — split purely for
    // file-size readability.
    public partial class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly FileStorageService _fileStorage;
        private readonly NotificationService _notifications;
        private readonly OfferLetterPdfService _offerPdf;
        private readonly CandidateWorkflowService _candidateWorkflow;
        private readonly CandidateRegistrationService _candidateRegistration;
        private readonly DisposableEmailService _disposableEmail;
        private readonly ILogger<AccountController> _logger;
        private readonly AuditService _audit;
        private readonly GoogleSignInStatus _googleSignIn;

        public AccountController(
            AppDbContext db,
            FileStorageService fileStorage,
            NotificationService notifications,
            OfferLetterPdfService offerPdf,
            CandidateWorkflowService candidateWorkflow,
            CandidateRegistrationService candidateRegistration,
            DisposableEmailService disposableEmail,
            ILogger<AccountController> logger,
            AuditService audit,
            GoogleSignInStatus googleSignIn)
        {
            _db = db;
            _fileStorage = fileStorage;
            _notifications = notifications;
            _offerPdf = offerPdf;
            _candidateWorkflow = candidateWorkflow;
            _candidateRegistration = candidateRegistration;
            _disposableEmail = disposableEmail;
            _logger = logger;
            _audit = audit;
            _googleSignIn = googleSignIn;
        }

        // Read-only account summary for staff roles (Recruiter/HR/Admin), who —
        // unlike candidates — don't have a profile-completion flow of their own.
        // Deliberately read-only: there's no existing change-password or
        // profile-edit flow for staff accounts to hook into, so this doesn't
        // add one; it just gives the sidebar's "Profile" item somewhere real
        // to land, showing the same account fields shown elsewhere (Settings →
        // Create Staff User, Users & Roles).
        [Authorize]
        public IActionResult Profile()
        {
            var user = _db.Users.AsNoTracking().FirstOrDefault(u => u.Id == User.GetUserId());
            if (user == null) return RedirectToAction("Login");
            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        /// <param name="rememberMe">When true, issues a persistent cookie that survives
        /// browser close and lasts 30 days instead of the default 30-minute
        /// inactivity-timeout session (see opt.ExpireTimeSpan in Program.cs). Defaults
        /// to false so every other caller
        /// (Google sign-in, AssessmentAuth magic links) keeps existing
        /// behavior unchanged.</param>
        private async Task SignInUserAsync(User user, bool rememberMe = false)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.GivenName, user.FullName),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role),
                new("CandidateId", user.CandidateId)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var properties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null
            };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
        }

        /// <summary>
        /// Issues the short-lived, non-authenticating pending-email-verification cookie
        /// (AuthSchemes.PendingEmailVerification) instead of the real login cookie — used
        /// by Register (a brand-new signup) and Login (an existing candidate who hasn't
        /// verified yet) so VerifyEmailOtp/ResendEmailOtp can recover which candidate is
        /// verifying without granting [Authorize]-level access. Carries only the
        /// candidate's id — no Role claim, no other identity data — and is cleared as soon
        /// as VerifyEmailOtp succeeds (see AccountController.Registration.cs).
        /// </summary>
        private async Task SignInPendingVerificationAsync(User candidate)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, candidate.Id.ToString()) };
            var identity = new ClaimsIdentity(claims, AuthSchemes.PendingEmailVerification);
            await HttpContext.SignInAsync(AuthSchemes.PendingEmailVerification, new ClaimsPrincipal(identity));
        }

        private IActionResult RedirectSignedInUser()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId)) return RedirectToAction("Login");
            var user = _db.Users.Find(userId);
            return user == null ? RedirectToAction("Login") : RedirectToPortal(user);
        }

        private IActionResult RedirectToPortal(User user)
        {
            if (user.Role == PortalRoles.Candidate)
            {
                var nextAction = _candidateWorkflow.GetNextAction(user);
                if (!string.IsNullOrWhiteSpace(nextAction))
                    return RedirectToAction(nextAction);
                return RedirectToAction("Index", "Home");
            }

            return user.Role switch
            {
                PortalRoles.Admin => RedirectToAction("Index", "Admin"),
                PortalRoles.Recruiter => RedirectToAction("Index", "Recruiter"),
                PortalRoles.Hr => RedirectToAction("Index", "Hr"),
                _ => RedirectToAction("Login")
            };
        }

    }
}