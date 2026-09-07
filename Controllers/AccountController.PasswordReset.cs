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
// AccountController — forgot/reset password flow.
// Split out of the single AccountController.cs for readability; same partial class.
    public partial class AccountController
    {
        private const int PasswordResetTokenValidityMinutes = 30;

        [HttpGet]
        public IActionResult ForgotPassword() =>
            User.Identity?.IsAuthenticated == true ? RedirectSignedInUser() : View();

        [HttpPost]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ForgotPassword: model invalid for submitted email {Email}. Errors: {Errors}",
                    model?.Email,
                    string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                return View(model);
            }

            var normalizedEmail = model.Email.Trim().ToLowerInvariant();
            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == normalizedEmail);

            // This log line is the key diagnostic: it tells us, server-side only, whether the
            // lookup found a match — something the user-facing response deliberately never reveals.
            _logger.LogInformation("ForgotPassword requested for {SubmittedEmail} (normalized: {NormalizedEmail}) — match found: {Found}",
                model.Email, normalizedEmail, user != null);

            // Always show the same generic message whether or not the email is registered,
            // so this endpoint can't be used to enumerate valid accounts.
            if (user != null)
            {
                var token = SecureCodeGenerator.GenerateUrlSafeToken();
                user.PasswordResetTokenHash = SecureCodeGenerator.HashToken(token);
                user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(PasswordResetTokenValidityMinutes);
                _db.SaveChanges();

                var resetLink = Url.Action("ResetPassword", "Account",
                    new { email = user.Email, token }, Request.Scheme);

                // Logged at Debug (not Information) because the link contains the raw, single-use
                // reset token — the DB only ever stores its hash, so this is the one place the
                // plaintext token exists outside the recipient's inbox. Debug keeps it out of
                // default production log output while still being available locally for
                // dev/testing without SMTP configured.
                _logger.LogDebug("ForgotPassword: token generated and saved for user {UserId} ({Email}). Reset link: {ResetLink}",
                    user.Id, user.Email, resetLink);

                _notifications.Queue(user, null, "Password Reset",
                    "VISTAWAYS TECH password reset request",
                    $"Dear {user.FullName},\n\nWe received a request to reset your password. " +
                    $"Click the link below to choose a new password. This link expires in " +
                    $"{PasswordResetTokenValidityMinutes} minutes and can only be used once.\n\n{resetLink}\n\n" +
                    "If you didn't request this, you can safely ignore this email — your password will not change.\n\n" +
                    "VISTAWAYS TECH Recruitment Team");

                _logger.LogInformation("ForgotPassword: notification queued for user {UserId} ({Email}).", user.Id, user.Email);

                // NotificationService.Queue() only adds the entity to EF's change tracker — it does
                // NOT call SaveChanges() itself (callers are expected to). Without this,
                // DispatchPendingAsync's query below would find nothing: it queries the database
                // directly, not the in-memory tracker, so an unsaved notification is invisible to
                // it and gets silently dropped when this request's DbContext is disposed at the
                // end of the request.
                _db.SaveChanges();

                // Dispatch immediately rather than waiting up to 30s for NotificationBackgroundService's
                // next polling cycle — a reset link is time-sensitive, and in local/dev runs the process
                // is often stopped well within that window, which would otherwise strand the email as
                // permanently "Queued" and never actually sent.
                try
                {
                    var dispatchedCount = await _notifications.DispatchPendingAsync(HttpContext.RequestAborted);
                    _logger.LogInformation("ForgotPassword: immediate dispatch attempted, {Count} notification(s) processed.", dispatchedCount);
                }
                catch (Exception ex)
                {
                    // Dispatch failures are already recorded per-notification (Status/ErrorMessage) by
                    // DispatchPendingAsync; the background service will retry automatically. Swallow here
                    // so a transient SMTP hiccup doesn't turn into a 500 for the user submitting the form.
                    _logger.LogWarning(ex, "ForgotPassword: immediate dispatch attempt threw for user {UserId}.", user.Id);
                }
            }

            TempData["Success"] = "If that email is registered, we've sent a password reset link to it.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            var user = FindUserForValidResetToken(email, token);
            if (user == null)
            {
                TempData["Error"] = "This password reset link is invalid or has expired. Please request a new one.";
                return RedirectToAction("ForgotPassword");
            }

            return View(new ResetPasswordViewModel { Email = email ?? "", Token = token ?? "" });
        }

        [HttpPost]
        [EnableRateLimiting("AuthSensitive")]
        public IActionResult ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = FindUserForValidResetToken(model.Email, model.Token);
            if (user == null)
            {
                TempData["Error"] = "This password reset link is invalid or has expired. Please request a new one.";
                return RedirectToAction("ForgotPassword");
            }

            user.PasswordHash = AppDbContext.HashPassword(model.NewPassword);
            // Single-use: clear the token immediately so the same link can't be replayed.
            user.PasswordResetTokenHash = "";
            user.PasswordResetTokenExpiresAt = null;
            _db.SaveChanges();

            _notifications.Queue(user, null, "Password Reset",
                "VISTAWAYS TECH — your password was changed",
                $"Dear {user.FullName},\n\nYour password was just changed. If this wasn't you, please contact " +
                "the recruitment team immediately.\n\nVISTAWAYS TECH Recruitment Team");

            TempData["Success"] = "Your password has been reset. Please log in with your new password.";
            return RedirectToAction("Login");
        }

        /// <summary>Looks up a user by email whose stored reset-token hash matches the supplied
        /// token and has not expired. Returns null for any mismatch — invalid email, invalid
        /// token, expired token, or already-used (cleared) token — without distinguishing which.</summary>
        private User? FindUserForValidResetToken(string? email, string? token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token)) return null;

            var normalizedEmail = email.Trim().ToLowerInvariant();
            var tokenHash = SecureCodeGenerator.HashToken(token);

            return _db.Users.FirstOrDefault(u =>
                u.Email.ToLower() == normalizedEmail &&
                u.PasswordResetTokenHash != "" &&
                u.PasswordResetTokenHash == tokenHash &&
                u.PasswordResetTokenExpiresAt > DateTime.UtcNow);
        }

    }
}
