using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace ExamPortal.Controllers
{
// AccountController — sign-in: candidate Login, and AssessmentAuth (the token-based
// magic-link entry point used by assessment invitation emails). Split out of the single
// AccountController.cs for readability; this is still the same partial class (same fields,
// same DB context) as the other AccountController.*.cs files — nothing here changes behavior.
    public partial class AccountController
    {

        [HttpGet]
        public IActionResult Login() =>
            User.Identity?.IsAuthenticated == true ? RedirectSignedInUser() : View();

        [HttpPost]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> Login(CandidateLoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var id = (model.CandidateId ?? "").Trim();
            var normalized = id.ToUpperInvariant();
            var lower = id.ToLowerInvariant();

            User? user;
            if (id.Contains('@') ||
                normalized is "ADMIN" or "RECRUITER" or "HR")
            {
                user = _db.Users.FirstOrDefault(u =>
                    u.Username.ToLower() == lower ||
                    u.Email.ToLower() == lower ||
                    u.Role.ToLower() == lower);
            }
            else
            {
                user = _db.Users.FirstOrDefault(u =>
                    u.Role == PortalRoles.Candidate &&
                    u.CandidateId.ToUpper() == normalized);
            }

            if (user == null || !AppDbContext.VerifyPassword(user, model.Password))
            {
                // Recorded for the Admin dashboard's security alerts (see
                // ActivityFeedService.BuildAdminAlerts). The attempted identifier is
                // stored as Actor for internal monitoring only — this AuditLog is never
                // shown to the person attempting to log in, so it doesn't reveal
                // account existence to them; it's the same "id" they just typed.
                _audit.Record(id, "FailedLogin", nameof(User), user?.Id, "Invalid credentials at Login");
                _db.SaveChanges();
                ModelState.AddModelError("", "Invalid Candidate ID or password.");
                return View(model);
            }

            // An existing candidate who registered but never completed OTP verification
            // must not get the real login cookie either — Register no longer grants it
            // up front (see AccountController.Registration.cs), and Login re-authenticating
            // them with correct credentials doesn't change that they still haven't proven
            // they control this email address. Route them the same way a fresh
            // registration is: the short-lived pending-verification cookie, then
            // VerifyEmailOtp. Credential validation above, Remember Me, staff roles
            // (Admin/Recruiter/HR), and an already-verified candidate's login are all
            // unaffected — RememberMe is simply irrelevant for a cookie this short-lived.
            if (user.Role == PortalRoles.Candidate && !user.IsEmailVerified)
            {
                await SignInPendingVerificationAsync(user);
                TempData["Success"] = "Please verify your email to continue. Enter the OTP sent to your email, or request a new one.";
                return RedirectToAction("VerifyEmailOtp");
            }

            await SignInUserAsync(user, model.RememberMe);
            return RedirectToPortal(user);
        }

        /// <summary>
        /// Looks up an assessment invitation by token and, if it can't be used right now,
        /// returns a specific reason instead of one generic message — "already used to submit
        /// an attempt", "superseded by a newer invitation", "expired", or "no such link" are
        /// very different situations for a candidate (or whoever's helping them) to act on.
        /// </summary>
        private (AssessmentInvitation? Invitation, string? Error) ResolveAssessmentInvitation(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return (null, "This assessment link is missing or malformed. Please copy the full link from your email, or contact the recruitment team.");

            var invitation = _db.AssessmentInvitations.FirstOrDefault(i => i.Token == token);
            if (invitation == null)
                return (null, "This assessment link doesn't match any invitation on file. Please contact the recruitment team.");

            if (invitation.Status is "Pending" or "Accepted")
            {
                if (invitation.ExpiresAt < DateTime.UtcNow)
                {
                    invitation.Status = "Expired";
                    _db.SaveChanges();
                    return (null, "This assessment link has expired. Please contact the recruitment team for a new invitation.");
                }
                return (invitation, null);
            }

            return invitation.Status switch
            {
                "Used" => (null, "This link has already been used to submit an assessment attempt. Contact the recruitment team if you believe this is an error."),
                "Revoked" => (null, "This assessment link is no longer active — it looks like a newer invitation was sent since. Please check your email for the most recent one, or contact the recruitment team."),
                "Expired" => (null, "This assessment link has expired. Please contact the recruitment team for a new invitation."),
                _ => (null, "This assessment link is invalid. Please contact the recruitment team.")
            };
        }

        [HttpGet]
        public IActionResult AssessmentAuth(string token)
        {
            var (invitation, error) = ResolveAssessmentInvitation(token);
            if (invitation == null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Login");
            }

            if (User.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdClaim, out var signedInId) && signedInId == invitation.UserId)
                {
                    TempData["AssessmentLinkExamId"] = invitation.ExamId;
                    return RedirectToAction("AssessmentLanding", "Exam");
                }
            }

            ViewBag.Token = token;
            ViewBag.ExamId = invitation.ExamId;
            return View();
        }

        [HttpPost]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> AssessmentAuth(string token, string candidateId, string password, string oneTimeLoginToken)
        {
            var (invitation, error) = ResolveAssessmentInvitation(token);
            if (invitation == null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Login");
            }

            var id = (candidateId ?? "").Trim();
            var otp = (oneTimeLoginToken ?? "").Trim();

            // Accept either the candidate's email or their system-generated Candidate ID,
            // same as the main Login action — candidates commonly try their email here
            // since that's what the assessment invitation was sent to.
            User? user;
            if (id.Contains('@'))
            {
                var lower = id.ToLowerInvariant();
                user = _db.Users.FirstOrDefault(u =>
                    u.Role == PortalRoles.Candidate && u.Email.ToLower() == lower);
            }
            else
            {
                var normalized = id.ToUpperInvariant();
                user = _db.Users.FirstOrDefault(u =>
                    u.Role == PortalRoles.Candidate && u.CandidateId.ToUpper() == normalized);
            }

            var passwordValid = user != null && !string.IsNullOrWhiteSpace(password) && AppDbContext.VerifyPassword(user, password);
            var tokenValid = user != null &&
                invitation.UserId == user.Id &&
                invitation.TokenUsedAt == null &&
                !string.IsNullOrWhiteSpace(invitation.OneTimeLoginToken) &&
                invitation.OneTimeLoginToken == otp;

            if (user == null || user.Id != invitation.UserId || (!passwordValid && !tokenValid))
            {
                _audit.Record(id, "FailedLogin", nameof(User), user?.Id, "Invalid credentials at AssessmentAuth");
                _db.SaveChanges();
                ViewBag.Token = token;
                ViewBag.ExamId = invitation.ExamId;
                ModelState.AddModelError("", "Invalid Candidate ID, password, one-time token, or this invitation is not for you.");
                return View();
            }

            invitation.Status = "Accepted";
            invitation.AcceptedAt ??= DateTime.UtcNow;
            if (tokenValid) invitation.TokenUsedAt = DateTime.UtcNow;
            if (user.RecruitmentStage == RecruitmentStages.InvitationSent)
                user.RecruitmentStage = RecruitmentStages.AssessmentStarted;

            _db.SaveChanges();
            await SignInUserAsync(user);
            TempData["AssessmentLinkExamId"] = invitation.ExamId;
            return RedirectToAction("AssessmentLanding", "Exam");
        }

    }
}
