using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExamPortal.Controllers
{
// AccountController — the candidate registration/onboarding wizard: Register, email
// OTP verification, CompleteProfile, and UploadResume. Split out of the single
// AccountController.cs for readability; same partial class as the other files.
    public partial class AccountController
    {
        private static readonly string[] AllowedPhotoExtensions = [".jpg", ".jpeg", ".png", ".webp"];

        [HttpGet]
        public IActionResult Register() => View(new CandidateRegistrationViewModel());

        [HttpPost]
        [RequestSizeLimit(2 * 1024 * 1024)]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> Register(CandidateRegistrationViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var email = model.Email.Trim().ToLowerInvariant();
            if (_db.Users.Any(u => u.Email.ToLower() == email))
            {
                ModelState.AddModelError(nameof(model.Email), "Email address is already registered.");
                return View(model);
            }

            if (_disposableEmail.IsDisposable(email))
            {
                ModelState.AddModelError(nameof(model.Email),
                    "Temporary or disposable email addresses are not allowed. Please use a permanent email address.");
                return View(model);
            }

            // Step 1 only creates the account — all profile/skill/document data
            // is collected in Steps 3–6 after email verification.
            var dto = new CandidateRegistrationDto
            {
                FullName = model.FullName,
                Email = email,
                PhoneNumber = model.PhoneNumber,
                Password = model.Password,
            };

            User user;
            try
            {
                user = _candidateRegistration.CreateCandidate(dto);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Final safety net against the check-then-insert race above: two
                // concurrent requests for the same email can both pass the .Any() check
                // before either commits. The database's own unique index on Email (see
                // AppDbContext.OnModelCreating) is what actually prevents the duplicate
                // row; this just turns that constraint violation into the same friendly
                // message instead of an unhandled 500 with a raw DB exception.
                _logger.LogWarning(ex, "Register: SaveChanges failed for {Email}, likely a duplicate-email race.", email);
                ModelState.AddModelError(nameof(model.Email), "Email address is already registered.");
                return View(model);
            }

            _candidateWorkflow.QueueEmailOtp(user);
            await _db.SaveChangesAsync();

            // Deliberately NOT SignInUserAsync here — that would grant the normal,
            // [Authorize]-recognized login cookie before the candidate has proven they
            // control this email address. Issue only the short-lived pending-verification
            // cookie instead; SignInUserAsync happens once VerifyEmailOtp succeeds below.
            await SignInPendingVerificationAsync(user);

            TempData["Success"] = $"Registration successful. Your Candidate ID is {user.CandidateId}. We sent a 6-digit OTP to your email.";
            return RedirectToAction("VerifyEmailOtp");
        }

        // GetSignedInCandidate covers a candidate who already holds the real login cookie
        // (an already-verified candidate revisiting this page, or an existing session
        // issued before this pending-cookie mechanism existed — CandidateEmailVerificationMiddleware
        // is what actually redirects that second case here from elsewhere in the portal).
        // GetPendingVerificationCandidateAsync covers the normal, fresh case: a brand-new
        // registration or an existing-but-unverified Login, neither of which ever received
        // the real login cookie. Checking the real cookie first costs nothing extra (no
        // pending-cookie lookup needed once it's found) and preserves exactly who could
        // already reach this page before this change.
        private async Task<User?> GetSignedInOrPendingVerificationCandidateAsync() =>
            GetSignedInCandidate() ?? await GetPendingVerificationCandidateAsync();

        [HttpGet]
        public async Task<IActionResult> VerifyEmailOtp()
        {
            var candidate = await GetSignedInOrPendingVerificationCandidateAsync();
            if (candidate == null) return RedirectToAction("Login");
            return View(new EmailOtpViewModel { Email = candidate.Email });
        }

        [HttpPost]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> VerifyEmailOtp(EmailOtpViewModel model)
        {
            var candidate = await GetSignedInOrPendingVerificationCandidateAsync();
            if (candidate == null) return RedirectToAction("Login");
            model.Email = candidate.Email;

            if (!ModelState.IsValid) return View(model);
            if (_candidateWorkflow.IsOtpLocked(candidate))
            {
                ModelState.AddModelError(nameof(model.Otp), "Too many incorrect attempts. Please request a new OTP and try again.");
                return View(model);
            }
            if (!_candidateWorkflow.VerifyEmailOtp(candidate, model.Otp))
            {
                ModelState.AddModelError(nameof(model.Otp), "Invalid or expired OTP.");
                return View(model);
            }

            _db.SaveChanges();
            // Real login cookie now that email is verified — and immediately clear the
            // short-lived pending-verification cookie (if any) rather than leaving it to
            // expire on its own; a candidate who arrived here via GetSignedInCandidate
            // instead never had one, and clearing an absent cookie is a harmless no-op.
            await SignInUserAsync(candidate);
            await HttpContext.SignOutAsync(AuthSchemes.PendingEmailVerification);
            if (candidate.ProfileCompletion != null)
            {
                TempData["Success"] = $"Email verified. Your Candidate ID is {candidate.CandidateId}. Recruiters can now review your profile.";
                return RedirectToAction("Index", "Home");
            }

            TempData["Success"] = "Email verified. Complete your candidate profile.";
            return RedirectToAction("CompleteProfile");
        }

        [HttpPost]
        [EnableRateLimiting("AuthSensitive")]
        public async Task<IActionResult> ResendEmailOtp()
        {
            var candidate = await GetSignedInOrPendingVerificationCandidateAsync();
            if (candidate == null) return RedirectToAction("Login");

            _candidateWorkflow.QueueEmailOtp(candidate);
            _db.SaveChanges();

            // A candidate here on the pending-verification cookie (not the real login
            // cookie) gets that cookie re-issued too, so its 15-minute expiry moves in
            // step with the fresh OTP's — otherwise the cookie could expire before a
            // just-resent OTP does, stranding them on this page with no way back.
            if (User.Identity?.IsAuthenticated != true)
                await SignInPendingVerificationAsync(candidate);

            TempData["Success"] = "A new OTP was sent to your email.";
            return RedirectToAction("VerifyEmailOtp");
        }

        [HttpGet]
        public IActionResult CompleteProfile()
        {
            var candidate = GetSignedInCandidate();
            if (candidate == null) return RedirectToAction("Login");
            // Pre-populate from existing candidate data so the form survives page refreshes
            var vm = new CandidateProfileStepViewModel
            {
                DateOfBirth = candidate.DateOfBirth,
                Gender = candidate.Gender,
                PermanentAddress = candidate.Address?.AddressLine ?? candidate.PermanentAddress,
                City = candidate.Address?.City ?? "",
                State = candidate.Address?.State ?? "",
                Country = candidate.Address?.Country ?? "",
                Pincode = candidate.Address?.Pincode ?? "",
                Postgraduate = ToEntry(candidate, EducationLevels.Postgraduate) ?? new EducationLevelEntry { Status = EducationStatuses.NotApplicable },
                Undergraduate = ToEntry(candidate, EducationLevels.Undergraduate) ?? new EducationLevelEntry(),
                Intermediate = ToEntry(candidate, EducationLevels.Intermediate) ?? new EducationLevelEntry(),
                Secondary = ToEntry(candidate, EducationLevels.Secondary) ?? new EducationLevelEntry(),
                CurrentAcademicStatus = candidate.CurrentAcademicStatus,
                GapInEducation = candidate.GapInEducation,
            };
            // Restore backlog from stored "Count:Status" string
            if (!string.IsNullOrWhiteSpace(candidate.BacklogInformation))
            {
                var parts = candidate.BacklogInformation.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var count))
                {
                    vm.BacklogCount = count;
                    vm.BacklogStatus = parts[1];
                }
            }
            // Restore languages from Name field (stored as "Language:Proficiency")
            vm.LanguagesKnown = candidate.Languages
                .Select(l => {
                    var parts = (l.Name ?? "").Split(':');
                    return new LanguageKnownEntry {
                        Language = parts.Length > 0 ? parts[0].Trim() : (l.Name ?? ""),
                        Proficiency = parts.Length > 1 ? parts[1].Trim() : ""
                    };
                })
                .ToList();
            return View(vm);
        }

        [HttpPost]
        public IActionResult CompleteProfile(CandidateProfileStepViewModel model)
        {
            var candidate = GetSignedInCandidate();
            if (candidate == null) return RedirectToAction("Login");

            // Serialise Languages Known entries from the dynamic widget.
            // Not required — candidates can skip and add later.
            var validEntries = (model.LanguagesKnown ?? new())
                .Where(e => !string.IsNullOrWhiteSpace(e.Language) && !string.IsNullOrWhiteSpace(e.Proficiency))
                .GroupBy(e => e.Language, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            model.LanguagesKnown = validEntries;

            if (!ModelState.IsValid) return View(model);

            _candidateWorkflow.CompleteProfile(candidate, model);
            _db.SaveChanges();
            TempData["Success"] = "Profile details saved. Upload your resume next.";
            return RedirectToAction("UploadResume");
        }

        [HttpGet]
        public IActionResult UploadResume()
        {
            var candidate = GetSignedInCandidate();
            if (candidate == null) return RedirectToAction("Login");
            // Pre-populate if candidate is returning to edit
            var vm = new ResumeSkillStepViewModel
            {
                Skills = candidate.Skills,
                LinkedInUrl = candidate.LinkedInUrl,
                PreferredJobLocation = candidate.JobPreference?.PreferredJobLocation ?? candidate.PreferredJobLocations,
            };
            return View(vm);
        }

        [HttpPost]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> UploadResume(ResumeSkillStepViewModel model)
        {
            var candidate = GetSignedInCandidate();
            if (candidate == null) return RedirectToAction("Login");
            if (!ModelState.IsValid) return View(model);

            var resumePath = await SaveResumeAsync(model.ResumeUpload);
            var profilePhotoPath = await SaveProfilePhotoAsync(model.ProfilePhoto);
            if (!ModelState.IsValid) return View(model);

            var candidateId = _candidateWorkflow.CompleteResumeAndSkills(candidate, resumePath, profilePhotoPath, model);
            _db.SaveChanges();
            await SignInUserAsync(candidate);
            TempData["Success"] = $"Registration complete! Your Candidate ID is {candidateId}. Recruiters will now review your profile.";
            return RedirectToAction("Index", "Home");
        }

        /// <summary>Maps a saved CandidateEducationRecord row back onto the accordion entry shape, or null if never saved.</summary>
        private static EducationLevelEntry? ToEntry(User candidate, string level)
        {
            var record = candidate.EducationRecords.FirstOrDefault(e => e.Level == level);
            if (record == null) return null;
            return new EducationLevelEntry
            {
                DegreeOrCourse = record.DegreeOrCourse ?? "",
                InstituteName = record.InstituteName ?? "",
                BoardOrUniversity = record.BoardOrUniversity ?? "",
                StreamBranch = record.StreamBranch ?? "",
                YearOfPassing = record.YearOfPassing,
                MarksValue = record.MarksValue,
                MarksType = record.MarksType ?? "Percentage",
                Status = record.Status ?? "",
            };
        }

        // Address/ProfessionalProfile/JobPreference must be included here (not just
        // CandidateProfile/ProfileCompletion/Languages): CompleteProfile and
        // CompleteResumeAndSkills both do `candidate.X ??= new CandidateX(...)` to
        // create-or-update these one-to-one rows. Without eager-loading them first, EF
        // sees a null navigation on every request and creates a brand new row each time,
        // which both throws a unique-constraint error on the second save and starves
        // RecalculateProfileCompletion of the data it needs to compute an accurate
        // percentage. Shared by GetSignedInCandidate (reads the real login cookie) and
        // GetPendingVerificationCandidateAsync (reads the short-lived pre-verification
        // cookie instead) so both return an identically-shaped candidate.
        private IQueryable<User> CandidateQueryWithProfileIncludes() =>
            _db.Users
                .Include(u => u.CandidateProfile)
                .Include(u => u.ProfileCompletion)
                .Include(u => u.Languages)
                .Include(u => u.Address)
                .Include(u => u.ProfessionalProfile)
                .Include(u => u.JobPreference)
                .Include(u => u.EducationRecords);

        private User? GetSignedInCandidate()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId)) return null;
            return CandidateQueryWithProfileIncludes().FirstOrDefault(u => u.Id == userId && u.Role == PortalRoles.Candidate);
        }

        /// <summary>
        /// Reads the candidate id off the short-lived pending-verification cookie (see
        /// AuthSchemes.PendingEmailVerification / AccountController.SignInPendingVerificationAsync)
        /// rather than the real login cookie GetSignedInCandidate reads. This is the
        /// identity carrier used between Register/Login and VerifyEmailOtp/ResendEmailOtp
        /// before a candidate has verified their email and received the normal login
        /// cookie. Deliberately a separate, explicit AuthenticateAsync call — this scheme
        /// is never the app's DefaultAuthenticateScheme, so it's never reflected in the
        /// ambient `User`/HttpContext.User the way GetSignedInCandidate's check is. Returns
        /// null if the cookie is missing, expired (matches the OTP's own 15-minute expiry),
        /// or doesn't resolve to a real candidate.
        /// </summary>
        private async Task<User?> GetPendingVerificationCandidateAsync()
        {
            var result = await HttpContext.AuthenticateAsync(AuthSchemes.PendingEmailVerification);
            if (!result.Succeeded || result.Principal == null) return null;
            var userIdClaim = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId)) return null;
            return await CandidateQueryWithProfileIncludes().FirstOrDefaultAsync(u => u.Id == userId && u.Role == PortalRoles.Candidate);
        }

        private async Task<string> SaveResumeAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError(nameof(ResumeSkillStepViewModel.ResumeUpload), "Resume upload is required.");
                return "";
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".pdf" || file.ContentType != "application/pdf")
            {
                ModelState.AddModelError(nameof(ResumeSkillStepViewModel.ResumeUpload), "Resume must be a PDF file.");
                return "";
            }

            if (file.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(ResumeSkillStepViewModel.ResumeUpload), "Resume must be 5 MB or smaller.");
                return "";
            }

            // Extension and Content-Type are both client-supplied and easily spoofed in a
            // manually crafted request — this checks the file's actual leading bytes match
            // a real PDF ("%PDF-"), catching a renamed/mislabeled or corrupt file that
            // passed both checks above.
            if (!await FileSignatureValidator.MatchesExtensionAsync(file, extension))
            {
                ModelState.AddModelError(nameof(ResumeSkillStepViewModel.ResumeUpload), "This file doesn't appear to be a valid PDF.");
                return "";
            }

            return await SaveUploadAsync(file, "resumes", ".pdf");
        }

        private async Task<string> SaveProfilePhotoAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0) return "";

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedPhotoExtensions.Contains(extension) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(ResumeSkillStepViewModel.ProfilePhoto), "Profile photo must be a JPG, PNG, or WebP image.");
                return "";
            }

            if (file.Length > 2 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(ResumeSkillStepViewModel.ProfilePhoto), "Profile photo must be 2 MB or smaller.");
                return "";
            }

            // Same reasoning as SaveResumeAsync above — extension/Content-Type are
            // client-supplied and spoofable, so this checks the actual image signature.
            if (!await FileSignatureValidator.MatchesExtensionAsync(file, extension))
            {
                ModelState.AddModelError(nameof(ResumeSkillStepViewModel.ProfilePhoto), "This file doesn't appear to be a valid image.");
                return "";
            }

            return await SaveUploadAsync(file, "profile-photos", extension);
        }

        private async Task<string> SaveUploadAsync(IFormFile file, string folder, string extension)
        {
            var stored = await _fileStorage.SavePrivateAsync(file, folder, extension);
            return stored.PublicPath;
        }

    }
}
