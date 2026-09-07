using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace ExamPortal.Controllers
{
    // Google Sign-In — Candidates only. Same partial class as the rest of
    // AccountController.*.cs. Recruiter/HR/Admin accounts are never created or matched
    // here; see RejectIfNotCandidate below for the explicit refusal.
    //
    // Flow: GoogleLogin (Challenge) -> Google -> GET /signin-google (handled by the
    // ASP.NET Core Google OAuth handler itself, not an action here; it validates the
    // OAuth "state" for CSRF protection, then signs the result into the short-lived
    // AuthSchemes.GoogleExternal cookie and redirects to RedirectUri) -> GoogleCallback
    // (this file) reads that cookie once, resolves/creates the Candidate account, signs
    // into the app's real cookie via the existing SignInUserAsync, and clears the
    // short-lived cookie.
    public partial class AccountController
    {
        [HttpGet]
        [EnableRateLimiting("AuthSensitive")]
        public IActionResult GoogleLogin()
        {
            if (!_googleSignIn.IsConfigured)
            {
                TempData["Error"] = "Google sign-in isn't set up on this environment yet. Please sign in with your Candidate ID and password.";
                return RedirectToAction("Login");
            }

            var redirectUrl = Url.Action("GoogleCallback", "Account");
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> GoogleCallback(string? remoteError = null)
        {
            // Clear the short-lived external cookie no matter how this request ends —
            // it should never outlive a single callback.
            async Task<IActionResult> Finish(IActionResult result)
            {
                await HttpContext.SignOutAsync(AuthSchemes.GoogleExternal);
                return result;
            }

            if (!string.IsNullOrWhiteSpace(remoteError))
            {
                TempData["Error"] = "Google sign-in was cancelled or didn't complete.";
                return await Finish(RedirectToAction("Login"));
            }

            // This AuthenticateAsync call is what actually validates the OAuth "state"
            // parameter (CSRF protection for the whole redirect flow) — it's handled
            // inside the Google/OAuth middleware before this action ever runs; if that
            // validation had failed, the request would never reach here successfully.
            var externalResult = await HttpContext.AuthenticateAsync(AuthSchemes.GoogleExternal);
            if (!externalResult.Succeeded || externalResult.Principal == null)
            {
                TempData["Error"] = "Google sign-in failed. Please try again.";
                return await Finish(RedirectToAction("Login"));
            }

            var principal = externalResult.Principal;
            var googleId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var email = (principal.FindFirstValue(ClaimTypes.Email) ?? "").Trim().ToLowerInvariant();
            var name = principal.FindFirstValue(ClaimTypes.Name) ?? email;
            var picture = principal.FindFirstValue("urn:google:picture") ?? "";
            var emailVerifiedClaim = principal.FindFirstValue("urn:google:email_verified");

            if (string.IsNullOrWhiteSpace(googleId) || string.IsNullOrWhiteSpace(email))
            {
                TempData["Error"] = "Google didn't share the account details we need (email). Please try again or sign in with your Candidate ID and password.";
                return await Finish(RedirectToAction("Login"));
            }

            // Google normally always reports this as true for OAuth sign-in, but check
            // explicitly rather than assume — an unverified email is not a safe key to
            // link or create an account against.
            if (emailVerifiedClaim == "false")
            {
                TempData["Error"] = "Your Google account's email address isn't verified with Google, so we can't use it to sign in here.";
                return await Finish(RedirectToAction("Login"));
            }

            // 1) Already linked — most reliable match, since email can change.
            var user = _db.Users.FirstOrDefault(u => u.GoogleId == googleId && u.GoogleId != "");

            // 2) Not linked yet, but a local account exists with this email.
            if (user == null)
            {
                var existingByEmail = _db.Users.FirstOrDefault(u => u.Email.ToLower() == email);
                if (existingByEmail != null)
                {
                    var rejection = RejectIfNotCandidate(existingByEmail);
                    if (rejection != null)
                        return await Finish(rejection);

                    // Safe account linking: same verified email, no duplicate created.
                    existingByEmail.GoogleId = googleId;
                    if (existingByEmail.AuthProvider == AuthProviders.Local)
                        existingByEmail.AuthProvider = AuthProviders.Google;
                    if (!existingByEmail.IsEmailVerified) existingByEmail.IsEmailVerified = true;
                    _db.SaveChanges();
                    user = existingByEmail;
                }
            }

            // 3) First time we've seen this person at all — create a new Candidate.
            if (user == null)
            {
                user = _candidateRegistration.CreateGoogleCandidate(name, email, googleId, picture);
                _db.SaveChanges();
            }

            var rejectExisting = RejectIfNotCandidate(user);
            if (rejectExisting != null)
                return await Finish(rejectExisting);

            await SignInUserAsync(user);
            return await Finish(RedirectToPortal(user));
        }

        /// <summary>The actual enforcement point for "Google sign-in is Candidates only":
        /// refuses to complete sign-in for any account that isn't Role == Candidate,
        /// regardless of how a matching Google email was found. Recruiter/HR/Admin
        /// accounts are never created here, and an existing staff account can never be
        /// linked to a Google identity via this flow.</summary>
        private IActionResult? RejectIfNotCandidate(User user)
        {
            if (user.Role == PortalRoles.Candidate) return null;

            TempData["Error"] = "Google sign-in is only available for candidate accounts. Staff accounts must sign in with their portal username and password.";
            return RedirectToAction("Login");
        }
    }
}
