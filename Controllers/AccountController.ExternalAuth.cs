using ExamPortal.Models;
using ExamPortal.Services;
using Google.Apis.Auth;
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
    // Two entry points share the same account-resolution logic (SignInWithGoogleAccountAsync
    // below), and only differ in how they obtain a validated Google identity:
    //
    //  - GoogleLogin(string credential) [POST] — the current entry point, used by
    //    Views/Shared/_GoogleSignInButton.cshtml's Google Identity Services (GIS) button.
    //    GIS runs entirely client-side (https://accounts.google.com/gsi/client) and hands
    //    the button's JS callback a signed Google ID token ("credential"); the button's
    //    script POSTs that token here (with the page's existing antiforgery token, same
    //    pattern as Views/Exam/Take.cshtml's AJAX calls). This action verifies it locally
    //    with GoogleJsonWebSignature.ValidateAsync — no redirect to Google, no
    //    /signin-google callback round-trip.
    //
    //  - GoogleLogin() [GET] + GoogleCallback — the original OAuth redirect flow
    //    (Challenge -> Google -> /signin-google, handled by the ASP.NET Core Google OAuth
    //    handler itself -> GoogleCallback reads the short-lived AuthSchemes.GoogleExternal
    //    cookie once). Left in place, unused by the GIS button but still valid, harmless
    //    dead code — removing it isn't required for this to compile or work, and the
    //    Google OAuth handler + AuthSchemes.GoogleExternal cookie scheme in Program.cs are
    //    unrelated authentication this task said not to touch.
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

        /// <summary>
        /// Entry point for the Google Identity Services (GIS) button
        /// (Views/Shared/_GoogleSignInButton.cshtml): receives the signed Google ID token
        /// ("credential") GIS's client-side callback POSTs here, verifies it server-side,
        /// then hands the verified identity to the same account-resolution logic
        /// GoogleCallback uses. No redirect to Google and no /signin-google round-trip —
        /// the whole exchange is this one POST.
        /// </summary>
        [HttpPost]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> GoogleLogin(string credential)
        {
            if (!_googleSignIn.IsConfigured)
            {
                TempData["Error"] = "Google sign-in isn't set up on this environment yet. Please sign in with your Candidate ID and password.";
                return RedirectToAction("Login");
            }

            if (string.IsNullOrWhiteSpace(credential))
            {
                TempData["Error"] = "Google sign-in failed. Please try again.";
                return RedirectToAction("Login");
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                // ValidateAsync itself verifies the token's signature (against Google's
                // published public keys) and issuer (accounts.google.com /
                // https://accounts.google.com); Audience below additionally requires it was
                // issued for *this* app's Client ID, not some other Google-integrated site.
                var validationSettings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleAuthOptions.ClientId }
                };
                payload = await GoogleJsonWebSignature.ValidateAsync(credential, validationSettings);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "GoogleLogin: Google ID token failed validation.");
                TempData["Error"] = "Google sign-in failed. Please try again.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                // Covers e.g. a transient failure fetching Google's public keys — same
                // user-facing outcome as any other Google sign-in failure below.
                _logger.LogWarning(ex, "GoogleLogin: unexpected error validating Google ID token.");
                TempData["Error"] = "Google sign-in failed. Please try again.";
                return RedirectToAction("Login");
            }

            var googleId = payload.Subject ?? "";
            var email = (payload.Email ?? "").Trim().ToLowerInvariant();
            var name = payload.Name ?? email;
            var picture = payload.Picture ?? "";

            if (string.IsNullOrWhiteSpace(googleId) || string.IsNullOrWhiteSpace(email))
            {
                TempData["Error"] = "Google didn't share the account details we need (email). Please try again or sign in with your Candidate ID and password.";
                return RedirectToAction("Login");
            }

            // Same explicit check GoogleCallback below has always made rather than assuming
            // — an unverified email is not a safe key to link or create an account against.
            if (!payload.EmailVerified)
            {
                TempData["Error"] = "Your Google account's email address isn't verified with Google, so we can't use it to sign in here.";
                return RedirectToAction("Login");
            }

            return await SignInWithGoogleAccountAsync(googleId, email, name, picture);
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

            return await Finish(await SignInWithGoogleAccountAsync(googleId, email, name, picture));
        }

        /// <summary>
        /// The actual Google account-resolution/sign-in logic, shared by both entry points
        /// above (previously duplicated only in GoogleCallback) — same lookup order and
        /// rules for either: 1) already linked by GoogleId, 2) link an existing local
        /// account matched by email, 3) first time seen -> create a new Candidate. Always
        /// finishes with the same Candidate-only enforcement, SignInUserAsync, and
        /// RedirectToPortal used everywhere else in the app.
        /// </summary>
        private async Task<IActionResult> SignInWithGoogleAccountAsync(string googleId, string email, string name, string picture)
        {
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
                        return rejection;

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
                return rejectExisting;

            await SignInUserAsync(user);
            return RedirectToPortal(user);
        }

        /// <summary>The actual enforcement point for "Google sign-in is Candidates only":
        /// refuses to complete sign-in for any account that isn't Role == Candidate,
        /// regardless of how a matching Google email was found. Recruiter/HR/Admin
        /// accounts are never created here, and an existing staff account can never be
        /// linked to a Google identity through either entry point above.</summary>
        private IActionResult? RejectIfNotCandidate(User user)
        {
            if (user.Role == PortalRoles.Candidate) return null;

            TempData["Error"] = "Google sign-in is only available for candidate accounts. Staff accounts must sign in with their portal username and password.";
            return RedirectToAction("Login");
        }
    }
}
