using ExamPortal.Data;
using ExamPortal.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ExamPortal.Services
{
    public class CandidateWorkflowService
    {
        private readonly AppDbContext _db;
        private readonly NotificationService _notifications;
        private readonly CandidateIdService _candidateIds;
        private readonly IMemoryCache _cache;

        /// <summary>
        /// Max wrong OTP attempts allowed before a short in-memory lockout kicks in.
        /// Mitigates brute-forcing a 6-digit OTP (1,000,000 possible codes) beyond what the
        /// existing per-IP "AuthSensitive" rate limit (10 req/min) alone would stop, since an
        /// authenticated candidate can otherwise script repeated guesses against their own account.
        /// </summary>
        private const int MaxOtpAttempts = 5;

        /// <summary>
        /// Lockout duration matches the OTP's own 15-minute expiry window, so a locked-out
        /// candidate is unblocked at roughly the same time their OTP would have expired anyway.
        /// </summary>
        private static readonly TimeSpan OtpLockoutDuration = TimeSpan.FromMinutes(15);

        public CandidateWorkflowService(AppDbContext db, NotificationService notifications, CandidateIdService candidateIds, IMemoryCache cache)
        {
            _db = db;
            _notifications = notifications;
            _candidateIds = candidateIds;
            _cache = cache;
        }

        private static string OtpAttemptCacheKey(int candidateId) => $"otp-attempts:{candidateId}";

        public string QueueEmailOtp(User candidate)
        {
            var otp = SecureCodeGenerator.GenerateNumericCode();
            // Store only a SHA-256 hash of the OTP — never the raw value — matching the
            // same pattern already used for password-reset tokens (see
            // SecureCodeGenerator.HashToken / AccountController.PasswordReset.cs). A
            // database leak alone then can't be used to complete email verification.
            candidate.EmailVerificationToken = SecureCodeGenerator.HashToken(otp);
            candidate.MobileOtpExpiresAt = DateTime.UtcNow.AddMinutes(15);
            candidate.RecruitmentStage = RecruitmentStages.EmailVerificationPending;

            // A fresh OTP also resets the attempt counter — a new code deserves a new set of tries.
            _cache.Remove(OtpAttemptCacheKey(candidate.Id));

            _notifications.Queue(candidate, null, "Email Verification OTP",
                "Verify your VISTAWAYS TECH email",
                $"Dear {candidate.FullName},\n\nYour Candidate ID is {candidate.CandidateId}.\n\nYour email verification OTP is {otp}.\n\nThis OTP expires in 15 minutes.\n\nUse your Candidate ID with your password for portal login, assessment access, and future recruitment communication.\n\nVISTAWAYS TECH Recruitment Team");

            // The raw OTP only ever exists here and in the email above — callers that
            // need it (currently just tests) must capture this return value rather than
            // reading it back off the candidate, since the entity now only holds the hash.
            return otp;
        }

        /// <summary>
        /// Returns true if the candidate has exceeded the wrong-attempt limit for their
        /// current OTP and must wait for the lockout to clear (or request a fresh OTP, which
        /// resets the counter immediately via QueueEmailOtp).
        /// </summary>
        public bool IsOtpLocked(User candidate) =>
            _cache.TryGetValue(OtpAttemptCacheKey(candidate.Id), out int attempts) && attempts >= MaxOtpAttempts;

        public bool VerifyEmailOtp(User candidate, string otp)
        {
            if (IsOtpLocked(candidate)) return false;

            var valid = !string.IsNullOrWhiteSpace(otp)
                && !string.IsNullOrEmpty(candidate.EmailVerificationToken)
                && candidate.EmailVerificationToken == SecureCodeGenerator.HashToken(otp.Trim())
                && candidate.MobileOtpExpiresAt >= DateTime.UtcNow;

            if (!valid)
            {
                var attempts = _cache.GetOrCreate(OtpAttemptCacheKey(candidate.Id), entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = OtpLockoutDuration;
                    entry.Size = 1;
                    return 0;
                });
                _cache.Set(OtpAttemptCacheKey(candidate.Id), attempts + 1, OtpLockoutDuration);
                return false;
            }

            _cache.Remove(OtpAttemptCacheKey(candidate.Id));
            candidate.IsEmailVerified = true;
            candidate.EmailVerificationToken = "";
            candidate.MobileOtpExpiresAt = null;
            candidate.RecruitmentStage = candidate.ProfileCompletion != null
                ? RecruitmentStages.ProfileUnderReview
                : RecruitmentStages.ProfilePending;
            return true;
        }

        /// <summary>
        /// True while the candidate is still moving through the initial registration/
        /// onboarding wizard (Steps 1-4: CompleteProfile → UploadResume) and has not yet
        /// been invited to an assessment or progressed further into the pipeline.
        ///
        /// AccountController's CompleteProfile/UploadResume actions double as both (a) the
        /// initial onboarding wizard and (b) the screens a candidate returns to later to
        /// edit their profile (see the "Pre-populate ... candidate is returning to edit"
        /// comments on their GET actions). CompleteProfile/CompleteResumeAndSkills/
        /// IssueCandidateId below must only advance RecruitmentStage while onboarding is
        /// genuinely still in progress — once a candidate has been invited to an
        /// assessment (or moved further along), saving a profile edit is an ordinary
        /// update and must never move RecruitmentStage backwards to ResumePending/
        /// ProfileUnderReview, which would wipe out their real, already-progressed
        /// recruitment/application status.
        /// </summary>
        public static bool IsStillOnboarding(User candidate) => candidate.RecruitmentStage is
            RecruitmentStages.Registered or
            RecruitmentStages.EmailVerificationPending or
            RecruitmentStages.ProfilePending or
            RecruitmentStages.ResumePending or
            RecruitmentStages.SkillProfilePending or
            RecruitmentStages.ProfileUnderReview;

        public string IssueCandidateId(User candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate.CandidateId))
                candidate.CandidateId = _candidateIds.GenerateNextCandidateId();

            // Only advance the stage while the candidate is still onboarding — see
            // IsStillOnboarding. A candidate who has already progressed past
            // ProfileUnderReview (assessment invited/completed, interview, offer, etc.)
            // keeps their existing RecruitmentStage untouched.
            if (IsStillOnboarding(candidate))
                candidate.RecruitmentStage = RecruitmentStages.ProfileUnderReview;
            candidate.CandidateProfile ??= new CandidateProfile { UserId = candidate.Id };
            candidate.CandidateProfile.IsProfileComplete = true;
            candidate.CandidateProfile.IsResumeValidated = !string.IsNullOrWhiteSpace(candidate.ResumePath);
            candidate.CandidateProfile.VerificationStatus = "Ready for Recruiter Review";
            candidate.CandidateProfile.CompletedAt ??= DateTime.UtcNow;
            candidate.CandidateProfile.UpdatedAt = DateTime.UtcNow;
            return candidate.CandidateId;
        }

        public void CompleteProfile(User candidate, CandidateProfileStepViewModel model)
        {
            candidate.DateOfBirth = model.DateOfBirth;
            candidate.Gender = model.Gender.Trim();
            candidate.PermanentAddress = model.PermanentAddress.Trim();
            // City/State/Country/Pincode live on the CandidateAddress navigation entity.
            candidate.Address ??= new CandidateAddress { UserId = candidate.Id };
            candidate.Address.AddressLine = model.PermanentAddress.Trim();
            candidate.Address.City = model.City.Trim();
            candidate.Address.State = model.State.Trim();
            candidate.Address.Country = model.Country.Trim();
            candidate.Address.Pincode = model.Pincode.Trim();
            candidate.Address.UpdatedAt = DateTime.UtcNow;
            // Languages Known serialised from structured widget entries.
            // Stored as CandidateLanguage rows ("Language:Proficiency" in Name) so
            // AccountController.CompleteProfile (GET) can read candidate.Languages back.
            // Not required — candidates can skip and add later.
            if (model.LanguagesKnown != null)
            {
                candidate.Languages.Clear();
                foreach (var entry in model.LanguagesKnown)
                {
                    candidate.Languages.Add(new CandidateLanguage
                    {
                        UserId = candidate.Id,
                        Name = $"{entry.Language}:{entry.Proficiency}"
                    });
                }
            }
            // Undergraduate is always "Completed" by the time someone registers here —
            // same reality as every other level (see EducationLevelFieldset.cshtml) — so
            // there's nothing left to derive. This used to force Status to "Pursuing"
            // whenever CurrentAcademicStatus (a field the form hasn't collected in a long
            // time — always blank) wasn't literally "Completed", which meant every real
            // submission got silently marked Pursuing regardless of what was actually
            // true. model.Undergraduate.Status now already comes through as "Completed"
            // (or "NotApplicable", though Undergraduate never allows that) straight from
            // the posted form, so it's simply left alone here.

            UpsertEducationRecord(candidate, EducationLevels.Postgraduate, model.Postgraduate);
            UpsertEducationRecord(candidate, EducationLevels.Undergraduate, model.Undergraduate);
            UpsertEducationRecord(candidate, EducationLevels.Intermediate, model.Intermediate);
            UpsertEducationRecord(candidate, EducationLevels.Secondary, model.Secondary);

            // Keep the legacy flat User columns in sync so existing admin/recruiter views
            // (Students.cshtml, Pipeline.cshtml, CandidateDetail.cshtml) that read them
            // directly keep working without needing their own rewrite.
            candidate.CollegeUniversityName = model.Undergraduate.InstituteName.Trim();
            candidate.Degree = model.Undergraduate.DegreeOrCourse.Trim();
            candidate.BranchSpecialization = model.Undergraduate.StreamBranch?.Trim() ?? "";
            candidate.GraduationYear = model.Undergraduate.YearOfPassing;
            candidate.CgpaOrPercentage = model.Undergraduate.MarksValue;
            candidate.TenthPercentage = model.Secondary.MarksValue;
            candidate.TwelfthPercentage = model.Intermediate.MarksValue;

            // Backlog stored as "Count:Status" e.g. "2:Active" in the existing BacklogInformation column.
            candidate.BacklogInformation = model.BacklogCount.HasValue
                ? $"{model.BacklogCount}:{model.BacklogStatus ?? "N/A"}"
                : "";
            candidate.GapInEducation = model.GapInEducation?.Trim() ?? "None";

            // Only advance the stage while the candidate is still onboarding — see
            // IsStillOnboarding. A candidate who has already progressed past
            // ProfileUnderReview (assessment invited/completed, interview, offer, etc.)
            // keeps their existing RecruitmentStage untouched when they save an ordinary
            // profile edit.
            if (IsStillOnboarding(candidate))
                candidate.RecruitmentStage = RecruitmentStages.ResumePending;

            candidate.CandidateProfile ??= new CandidateProfile { UserId = candidate.Id };
            candidate.CandidateProfile.IsProfileComplete = false;
            candidate.CandidateProfile.VerificationStatus = "Profile Completed";
            candidate.CandidateProfile.UpdatedAt = DateTime.UtcNow;

            RecalculateProfileCompletion(candidate);
        }

        /// <summary>
        /// Creates or updates the candidate's <see cref="CandidateEducationRecord"/> row for
        /// the given level from one accordion entry. "Not Applicable" entries (Postgraduate
        /// only) still get a row, just with blank detail fields, so the accordion can be
        /// re-rendered exactly as last saved when the candidate returns to this step.
        /// </summary>
        private static void UpsertEducationRecord(User candidate, string level, EducationLevelEntry entry)
        {
            var record = candidate.EducationRecords.FirstOrDefault(e => e.Level == level);
            if (record == null)
            {
                record = new CandidateEducationRecord { UserId = candidate.Id, Level = level };
                candidate.EducationRecords.Add(record);
            }

            var notApplicable = entry.Status == EducationStatuses.NotApplicable;
            record.Status = string.IsNullOrWhiteSpace(entry.Status) ? EducationStatuses.NotApplicable : entry.Status;
            record.DegreeOrCourse = notApplicable ? "" : (entry.DegreeOrCourse ?? "").Trim();
            record.InstituteName = notApplicable ? "" : (entry.InstituteName ?? "").Trim();
            record.BoardOrUniversity = notApplicable ? "" : (entry.BoardOrUniversity ?? "").Trim();
            record.StreamBranch = notApplicable ? "" : (entry.StreamBranch ?? "").Trim();
            record.YearOfPassing = notApplicable ? null : entry.YearOfPassing;
            record.MarksValue = notApplicable ? null : entry.MarksValue;
            record.MarksType = notApplicable ? "Percentage" : (string.IsNullOrWhiteSpace(entry.MarksType) ? "Percentage" : entry.MarksType);
            record.UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Step 4 (final) — saves resume, photo, skills, and preferences in one transaction.
        /// Replaces the former separate CompleteResume + CompleteSkillProfile calls.
        /// </summary>
        public string CompleteResumeAndSkills(User candidate, string resumePath, string profilePhotoPath, ResumeSkillStepViewModel model)
        {
            // ── Resume & photo ────────────────────────────────────────────────
            candidate.ResumePath = resumePath;
            if (!string.IsNullOrWhiteSpace(profilePhotoPath))
                candidate.ProfilePhotoPath = profilePhotoPath;

            // NOTE: The résumé is intentionally NOT added to CandidateDocuments.
            // CandidateDocuments is reserved for documents a recruiter explicitly
            // requests post-review (Aadhar, PAN, marksheets, degree certificate, etc. —
            // see AdminController.RequestDocuments). Treating the résumé as one of those
            // rows double-counted it in the dashboard's "Documents" tile and could block
            // the DocumentsVerified stage transition (VerifyDocument requires every row
            // in CandidateDocuments to be "Verified", but nothing ever verifies a résumé
            // through that flow). Recruiter/Admin views read the résumé straight from
            // candidate.ResumePath instead.

            // ── Fresher skills (no work experience fields) ────────────────────
            // Certifications/Projects/Internships are no longer collected during
            // registration — intentionally not touched here so any pre-existing
            // values (e.g. added later by the candidate or an admin) are left as-is.
            candidate.Skills = model.Skills.Trim();
            candidate.LinkedInUrl = model.LinkedInUrl?.Trim() ?? "";

            candidate.ProfessionalProfile ??= new CandidateProfessionalProfile { UserId = candidate.Id };
            candidate.ProfessionalProfile.Skills = candidate.Skills;
            candidate.ProfessionalProfile.LinkedInUrl = candidate.LinkedInUrl;
            candidate.ProfessionalProfile.GitHubUrl = candidate.GitHubUrl;
            candidate.ProfessionalProfile.PortfolioUrl = candidate.PortfolioWebsite;
            candidate.ProfessionalProfile.UpdatedAt = DateTime.UtcNow;

            // ── Job preferences ───────────────────────────────────────────────
            // ExpectedSalary is no longer collected during registration —
            // intentionally not touched here so any pre-existing value is left as-is.
            if (!string.IsNullOrWhiteSpace(model.PreferredJobLocation))
                candidate.PreferredJobLocations = model.PreferredJobLocation.Trim();
            candidate.JobPreference ??= new CandidateJobPreference { UserId = candidate.Id };
            candidate.JobPreference.PreferredJobLocation = model.PreferredJobLocation?.Trim() ?? "";
            candidate.JobPreference.UpdatedAt = DateTime.UtcNow;

            // ── Profile flags ─────────────────────────────────────────────────
            candidate.CandidateProfile ??= new CandidateProfile { UserId = candidate.Id };
            candidate.CandidateProfile.IsResumeValidated = true;
            candidate.CandidateProfile.UpdatedAt = DateTime.UtcNow;

            // Stage goes straight to ProfileUnderReview — no intermediate SkillProfilePending.
            // (The actual assignment — guarded so it only fires while still onboarding, per
            // IsStillOnboarding — lives in IssueCandidateId below, so it isn't duplicated here.)
            IssueCandidateId(candidate);
            RecalculateProfileCompletion(candidate);

            // Registration is now fully complete — nothing further is required of the
            // candidate at this stage, so show 100% regardless of any optional fields
            // (LinkedIn, profile photo, etc.) they chose to skip. The 20-field formula
            // in RecalculateProfileCompletion is still used as-is for in-progress
            // percentages during steps 1-3 (CompleteProfile); this override only applies
            // once the wizard itself is done.
            candidate.ProfileCompletion!.CompletedFields = candidate.ProfileCompletion.TotalFields;
            candidate.ProfileCompletion.CompletionPercentage = 100;

            // CLEANUP: removed the "Profile Ready for Review" email — no action required
            // from the candidate, and it's largely redundant with the Email Verification
            // OTP email which already gave them their Candidate ID.

            return candidate.CandidateId;
        }

        /// <summary>
        /// Recomputes the candidate's profile completion percentage from the live candidate
        /// entity (and its related Address/JobPreference/Languages data) and creates or
        /// updates the CandidateProfileCompletion row.
        ///
        /// This is the single source of truth for profile completion — it reads from the
        /// persisted entity (not a registration DTO), so it can be called again after every
        /// wizard step from Step 3 onward, once the User row exists.
        ///
        /// Call this any time candidate profile data changes, then let the caller's existing
        /// _db.SaveChanges() persist it — EF Core will insert or update
        /// CandidateProfileCompletions automatically since candidate.ProfileCompletion is a
        /// tracked navigation property on an already-tracked candidate.
        /// </summary>
        public void RecalculateProfileCompletion(User candidate)
        {
            var values = new[]
            {
                candidate.FullName, candidate.Email, candidate.MobileNumber, candidate.PasswordHash,
                candidate.ProfilePhotoPath,
                candidate.DateOfBirth?.ToString("O") ?? "", candidate.Gender,
                candidate.Address?.AddressLine ?? candidate.PermanentAddress,
                candidate.Address?.State ?? "", candidate.Address?.City ?? "",
                candidate.Address?.Country ?? "", candidate.Address?.Pincode ?? "",
                candidate.CollegeUniversityName, candidate.Degree, candidate.BranchSpecialization,
                candidate.GraduationYear?.ToString() ?? "",
                candidate.Skills,
                candidate.LinkedInUrl,
                candidate.ResumePath,
                candidate.Languages.Any() ? "yes" : "",
                candidate.PreferredJobLocations
            };

            var completed = values.Count(v => !string.IsNullOrWhiteSpace(v));
            var total = values.Length;
            var percentage = (int)Math.Round(completed * 100m / total);

            candidate.ProfileCompletion ??= new CandidateProfileCompletion { UserId = candidate.Id };
            candidate.ProfileCompletion.CompletedFields = completed;
            candidate.ProfileCompletion.TotalFields = total;
            candidate.ProfileCompletion.CompletionPercentage = percentage;
            candidate.ProfileCompletion.CalculatedAt = DateTime.UtcNow;
        }

        public string GetNextAction(User candidate)
        {
            return candidate.RecruitmentStage switch
            {
                RecruitmentStages.EmailVerificationPending => "VerifyEmailOtp",
                RecruitmentStages.ProfilePending => "CompleteProfile",
                RecruitmentStages.ResumePending => "UploadResume",
                _ => ""
            };
        }

    }
}
