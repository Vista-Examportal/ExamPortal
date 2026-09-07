using ExamPortal.Data;
using ExamPortal.Models;

namespace ExamPortal.Services
{
    public class CandidateRegistrationService
    {
        private readonly AppDbContext _db;
        private readonly CandidateIdService _candidateIds;

        public CandidateRegistrationService(AppDbContext db, CandidateIdService candidateIds)
        {
            _db = db;
            _candidateIds = candidateIds;
        }

        public User CreateCandidate(CandidateRegistrationDto dto)
        {
            var names = SplitName(dto.FullName);
            // Step 1 only: create the account with the 4 registration fields.
            // Profile, resume, skills, and completion are collected in Steps 3-6
            // after email verification. Do NOT set ProfileCompletion here -
            // AccountController.VerifyEmailOtp checks (candidate.ProfileCompletion != null)
            // to decide whether to skip to Home; creating it here bypassed the wizard.
            var user = new User
            {
                FirstName = names.First,
                LastName = names.Last,
                FullName = dto.FullName.Trim(),
                Username = dto.Email.Trim().ToLowerInvariant(),
                Email = dto.Email.Trim().ToLowerInvariant(),
                MobileNumber = dto.PhoneNumber.Trim(),
                PasswordHash = AppDbContext.HashPassword(dto.Password),
                Role = PortalRoles.Candidate,
                CandidateId = _candidateIds.GenerateNextCandidateId(),
                RecruitmentStage = RecruitmentStages.EmailVerificationPending,
                IsMobileVerified = true,
                // CandidateProfile and ProfileCompletion intentionally omitted here.
                // They are created by CompleteProfile / CompleteSkillProfile in Steps 3-6.
            };

            _db.Users.Add(user);
            return user;
        }

        /// <summary>Creates a Candidate account for a first-time Google sign-in. Distinct
        /// from CreateCandidate (used by the normal email/password Register form) in three
        /// ways: no OTP step (Google's own email_verified claim is the verification —
        /// caller must have already checked it), the account starts one stage further
        /// along (RecruitmentStages.ProfilePending, skipping EmailVerificationPending),
        /// and PasswordHash is a random value the person never sees rather than one they
        /// chose — they can set a real password later via "Forgot password" if they ever
        /// want local login too, but that's opt-in, not assumed here.</summary>
        public User CreateGoogleCandidate(string fullName, string email, string googleId, string pictureUrl)
        {
            var names = SplitName(fullName);
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = new User
            {
                FirstName = names.First,
                LastName = names.Last,
                FullName = fullName.Trim(),
                Username = normalizedEmail,
                Email = normalizedEmail,
                PasswordHash = AppDbContext.HashPassword(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                Role = PortalRoles.Candidate,
                CandidateId = _candidateIds.GenerateNextCandidateId(),
                RecruitmentStage = RecruitmentStages.ProfilePending,
                IsEmailVerified = true,
                IsMobileVerified = false,
                AuthProvider = AuthProviders.Google,
                GoogleId = googleId,
                ProfilePhotoPath = pictureUrl ?? ""
            };

            _db.Users.Add(user);
            return user;
        }

        private static (string First, string Last) SplitName(string fullName)
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return ("", "");
            if (parts.Length == 1) return (parts[0], "");
            return (parts[0], string.Join(' ', parts.Skip(1)));
        }
    }
}
