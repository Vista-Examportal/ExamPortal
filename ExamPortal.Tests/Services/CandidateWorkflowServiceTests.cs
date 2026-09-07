using System;
using ExamPortal.Configuration;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExamPortal.Tests.Services
{
    // CandidateWorkflowService is the recruitment pipeline state machine: OTP
    // verification (with a brute-force lockout), candidate ID issuance, and profile
    // completion tracking. These tests build the service with real, lightweight
    // collaborators rather than mocks wherever possible:
    //   - AppDbContext: EF Core's in-memory provider (see CandidateIdServiceTests for
    //     the same pattern). Notably, CandidateWorkflowService's own _db field is never
    //     actually read by any method under test here — confirmed by inspection — so an
    //     empty in-memory context is sufficient.
    //   - IMemoryCache: the real Microsoft.Extensions.Caching.Memory.MemoryCache, not a
    //     fake — it's a simple, fully in-memory, dependency-free implementation, so
    //     there's no reason to fake it and every reason to exercise the real thing.
    //   - NotificationService: constructed for real too, since Queue()/QueueChannel()
    //     (the only methods CandidateWorkflowService calls) only touch _db.Notifications
    //     — confirmed by inspection they never touch the other 4 constructor
    //     dependencies (IConfiguration, IHttpClientFactory, EmailOptions, ILogger), which
    //     only matter for the async DispatchPendingAsync/DispatchOneAsync send pipeline
    //     that nothing here calls. Passing null!/minimal values for those four is safe.
    public class CandidateWorkflowServiceTests
    {
        private static AppDbContext BuildContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static CandidateWorkflowService BuildService(AppDbContext db, IMemoryCache cache)
        {
            var notifications = new NotificationService(
                db,
                config: null!,
                httpClientFactory: null!,
                emailOptions: Options.Create(new EmailOptions()),
                logger: NullLogger<NotificationService>.Instance);
            var candidateIds = new CandidateIdService(db);
            return new CandidateWorkflowService(db, notifications, candidateIds, cache);
        }

        private static User MakeCandidate() => new User
        {
            Id = 1,
            Username = "VWT202600001",
            Email = "candidate@example.com",
            PasswordHash = "not-a-real-hash",
            FullName = "Test Candidate",
        };

        // ── QueueEmailOtp ──────────────────────────────────────────────────────

        [Fact]
        public void QueueEmailOtp_SetsStageToEmailVerificationPending()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();

            service.QueueEmailOtp(candidate);

            Assert.Equal(RecruitmentStages.EmailVerificationPending, candidate.RecruitmentStage);
        }

        [Fact]
        public void QueueEmailOtp_ReturnsSixDigitCode()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();

            var otp = service.QueueEmailOtp(candidate);

            Assert.Equal(6, otp.Length);
            Assert.All(otp, c => Assert.True(char.IsDigit(c)));
        }

        [Fact]
        public void QueueEmailOtp_StoresOnlyAHashNotTheRawCode()
        {
            // Regression coverage: EmailVerificationToken must never hold the plaintext
            // OTP — only its SHA-256 hash (same pattern as PasswordResetTokenHash).
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();

            var otp = service.QueueEmailOtp(candidate);

            Assert.NotEqual(otp, candidate.EmailVerificationToken);
            Assert.Equal(64, candidate.EmailVerificationToken.Length); // SHA-256 hex string
            Assert.Equal(SecureCodeGenerator.HashToken(otp), candidate.EmailVerificationToken);
        }

        [Fact]
        public void QueueEmailOtp_SetsExpiryFifteenMinutesOut()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            var before = DateTime.UtcNow;

            service.QueueEmailOtp(candidate);

            Assert.NotNull(candidate.MobileOtpExpiresAt);
            var delta = candidate.MobileOtpExpiresAt!.Value - before;
            Assert.InRange(delta.TotalMinutes, 14.9, 15.1);
        }

        [Fact]
        public void QueueEmailOtp_ResetsAnyExistingLockout()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            service.QueueEmailOtp(candidate);

            // Lock the candidate out with 5 wrong attempts against the first OTP.
            for (int i = 0; i < 5; i++)
                service.VerifyEmailOtp(candidate, "000000");
            Assert.True(service.IsOtpLocked(candidate));

            // Requesting a fresh OTP should clear that lockout immediately.
            service.QueueEmailOtp(candidate);

            Assert.False(service.IsOtpLocked(candidate));
        }

        // ── IsOtpLocked / VerifyEmailOtp ──────────────────────────────────────

        [Fact]
        public void IsOtpLocked_FreshCandidate_IsNotLocked()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);

            Assert.False(service.IsOtpLocked(MakeCandidate()));
        }

        [Fact]
        public void VerifyEmailOtp_CorrectCode_ReturnsTrueAndMarksVerified()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            var code = service.QueueEmailOtp(candidate);

            var result = service.VerifyEmailOtp(candidate, code);

            Assert.True(result);
            Assert.True(candidate.IsEmailVerified);
            Assert.Equal("", candidate.EmailVerificationToken);
            Assert.Null(candidate.MobileOtpExpiresAt);
        }

        [Fact]
        public void VerifyEmailOtp_CorrectCode_TrimsWhitespace()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            var code = service.QueueEmailOtp(candidate);

            var result = service.VerifyEmailOtp(candidate, $"  {code}  ");

            Assert.True(result);
        }

        [Fact]
        public void VerifyEmailOtp_NoProfileCompletionYet_AdvancesToProfilePending()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.ProfileCompletion = null;
            var code = service.QueueEmailOtp(candidate);

            service.VerifyEmailOtp(candidate, code);

            Assert.Equal(RecruitmentStages.ProfilePending, candidate.RecruitmentStage);
        }

        [Fact]
        public void VerifyEmailOtp_ProfileCompletionAlreadyExists_AdvancesToProfileUnderReview()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.ProfileCompletion = new CandidateProfileCompletion { UserId = candidate.Id };
            var code = service.QueueEmailOtp(candidate);

            service.VerifyEmailOtp(candidate, code);

            Assert.Equal(RecruitmentStages.ProfileUnderReview, candidate.RecruitmentStage);
        }

        [Fact]
        public void VerifyEmailOtp_WrongCode_ReturnsFalseAndDoesNotVerify()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            service.QueueEmailOtp(candidate);

            var result = service.VerifyEmailOtp(candidate, "000000");

            Assert.False(result);
            Assert.False(candidate.IsEmailVerified);
        }

        [Fact]
        public void VerifyEmailOtp_ExpiredCode_ReturnsFalse()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            var code = service.QueueEmailOtp(candidate);
            candidate.MobileOtpExpiresAt = DateTime.UtcNow.AddMinutes(-1); // already expired

            var result = service.VerifyEmailOtp(candidate, code);

            Assert.False(result);
        }

        [Fact]
        public void VerifyEmailOtp_EmptyOrWhitespaceCode_ReturnsFalse()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            service.QueueEmailOtp(candidate);

            Assert.False(service.VerifyEmailOtp(candidate, ""));
            Assert.False(service.VerifyEmailOtp(candidate, "   "));
        }

        [Fact]
        public void VerifyEmailOtp_FiveWrongAttempts_LocksOutSixthAttemptEvenIfCorrect()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            var code = service.QueueEmailOtp(candidate);

            for (int i = 0; i < 5; i++)
                Assert.False(service.VerifyEmailOtp(candidate, "wrong-code"));

            Assert.True(service.IsOtpLocked(candidate));
            // Even the *correct* code should now be rejected until the lockout clears.
            var result = service.VerifyEmailOtp(candidate, code);
            Assert.False(result);
            Assert.False(candidate.IsEmailVerified);
        }

        [Fact]
        public void VerifyEmailOtp_FourWrongAttempts_StillAllowsFifthCorrectAttempt()
        {
            // One under the lockout threshold — confirms the boundary is 5, not 4.
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            var code = service.QueueEmailOtp(candidate);

            for (int i = 0; i < 4; i++)
                service.VerifyEmailOtp(candidate, "wrong-code");

            Assert.False(service.IsOtpLocked(candidate));
            Assert.True(service.VerifyEmailOtp(candidate, code));
        }

        // ── IssueCandidateId ───────────────────────────────────────────────────

        [Fact]
        public void IssueCandidateId_NoExistingId_GeneratesOne()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.CandidateId = "";

            var id = service.IssueCandidateId(candidate);

            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.Equal(id, candidate.CandidateId);
            Assert.StartsWith($"VWT{DateTime.UtcNow.Year}", id);
        }

        [Fact]
        public void IssueCandidateId_AlreadyHasId_DoesNotRegenerateIt()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.CandidateId = "VWT202600042";

            var id = service.IssueCandidateId(candidate);

            Assert.Equal("VWT202600042", id);
        }

        [Fact]
        public void IssueCandidateId_SetsStageToProfileUnderReview()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();

            service.IssueCandidateId(candidate);

            Assert.Equal(RecruitmentStages.ProfileUnderReview, candidate.RecruitmentStage);
        }

        [Fact]
        public void IssueCandidateId_NoResume_MarksResumeAsNotValidated()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.ResumePath = "";

            service.IssueCandidateId(candidate);

            Assert.False(candidate.CandidateProfile!.IsResumeValidated);
        }

        [Fact]
        public void IssueCandidateId_WithResume_MarksResumeAsValidated()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.ResumePath = "resumes/candidate1.pdf";

            service.IssueCandidateId(candidate);

            Assert.True(candidate.CandidateProfile!.IsResumeValidated);
        }

        // ── RecalculateProfileCompletion ───────────────────────────────────────

        [Fact]
        public void RecalculateProfileCompletion_EmptyCandidate_LowCompletionPercentage()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            // Only the always-required fields set by MakeCandidate(): FullName, Email,
            // PasswordHash — 3 of the 21 tracked fields.
            var candidate = MakeCandidate();

            service.RecalculateProfileCompletion(candidate);

            Assert.NotNull(candidate.ProfileCompletion);
            Assert.Equal(21, candidate.ProfileCompletion!.TotalFields);
            Assert.True(candidate.ProfileCompletion.CompletionPercentage < 30);
        }

        [Fact]
        public void RecalculateProfileCompletion_AllFieldsFilled_HundredPercent()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.MobileNumber = "9999999999";
            candidate.ProfilePhotoPath = "photos/p1.jpg";
            candidate.DateOfBirth = new DateTime(2000, 1, 1);
            candidate.Gender = "Female";
            candidate.PermanentAddress = "123 Main St";
            candidate.CollegeUniversityName = "Test University";
            candidate.Degree = "B.Tech";
            candidate.BranchSpecialization = "CSE";
            candidate.GraduationYear = 2022;
            candidate.Skills = "C#, SQL";
            candidate.LinkedInUrl = "https://linkedin.com/in/test";
            candidate.ResumePath = "resumes/r1.pdf";
            candidate.PreferredJobLocations = "Hyderabad";
            candidate.Languages.Add(new CandidateLanguage { UserId = candidate.Id, Name = "English:Fluent" });
            candidate.Address = new CandidateAddress
            {
                UserId = candidate.Id,
                AddressLine = "123 Main St",
                State = "Telangana",
                City = "Hyderabad",
                Country = "India",
                Pincode = "500085",
            };

            service.RecalculateProfileCompletion(candidate);

            Assert.Equal(100, candidate.ProfileCompletion!.CompletionPercentage);
            Assert.Equal(candidate.ProfileCompletion.TotalFields, candidate.ProfileCompletion.CompletedFields);
        }

        [Fact]
        public void RecalculateProfileCompletion_CalledTwice_UpdatesExistingRowRatherThanDuplicating()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();

            service.RecalculateProfileCompletion(candidate);
            var firstRow = candidate.ProfileCompletion;
            candidate.Skills = "Newly added skill";
            service.RecalculateProfileCompletion(candidate);

            Assert.Same(firstRow, candidate.ProfileCompletion);
        }

        // ── GetNextAction ──────────────────────────────────────────────────────

        [Theory]
        [InlineData(RecruitmentStages.EmailVerificationPending, "VerifyEmailOtp")]
        [InlineData(RecruitmentStages.ProfilePending, "CompleteProfile")]
        [InlineData(RecruitmentStages.ResumePending, "UploadResume")]
        [InlineData(RecruitmentStages.ProfileUnderReview, "")]
        [InlineData(RecruitmentStages.Shortlisted, "")]
        [InlineData(RecruitmentStages.OfferAccepted, "")]
        public void GetNextAction_ReturnsExpectedActionForStage(string stage, string expectedAction)
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.RecruitmentStage = stage;

            Assert.Equal(expectedAction, service.GetNextAction(candidate));
        }

        // ── Profile-edit must not reset an already-progressed recruitment status ──────
        // Regression coverage for: a candidate who has already progressed past
        // ProfileUnderReview (assessment/interview/offer stages) editing their profile
        // via CompleteProfile/UploadResume must NOT have RecruitmentStage bumped back to
        // ResumePending/ProfileUnderReview. New candidates going through the wizard for
        // the first time must still progress exactly as before.

        private static CandidateProfileStepViewModel MakeProfileStepModel() => new()
        {
            DateOfBirth = new DateTime(2000, 1, 1),
            Gender = "Female",
            PermanentAddress = "123 Main St",
            City = "Hyderabad",
            State = "Telangana",
            Country = "India",
            Pincode = "500085",
            Undergraduate = new EducationLevelEntry
            {
                Status = EducationStatuses.Completed,
                DegreeOrCourse = "B.Tech",
                InstituteName = "ABC College",
                BoardOrUniversity = "XYZ University",
                YearOfPassing = 2022,
                MarksValue = 75,
            },
            Intermediate = new EducationLevelEntry
            {
                Status = EducationStatuses.Completed,
                InstituteName = "ABC Junior College",
                BoardOrUniversity = "State Board",
                YearOfPassing = 2018,
                MarksValue = 85,
            },
            Secondary = new EducationLevelEntry
            {
                Status = EducationStatuses.Completed,
                InstituteName = "ABC High School",
                BoardOrUniversity = "State Board",
                YearOfPassing = 2016,
                MarksValue = 90,
            },
            CurrentAcademicStatus = "Completed",
        };

        private static ResumeSkillStepViewModel MakeResumeSkillModel() => new()
        {
            Skills = "C#, SQL",
            LinkedInUrl = "",
            PreferredJobLocation = "Hyderabad",
        };

        [Theory]
        [InlineData(RecruitmentStages.AssessmentCompleted)]
        [InlineData(RecruitmentStages.Shortlisted)]
        [InlineData(RecruitmentStages.InterviewInProgress)]
        [InlineData(RecruitmentStages.DocumentsVerified)]
        [InlineData(RecruitmentStages.OfferIssued)]
        [InlineData(RecruitmentStages.OfferAccepted)]
        [InlineData(RecruitmentStages.Onboarding)]
        public void CompleteProfile_CandidateAlreadyProgressed_DoesNotResetStage(string existingStage)
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.RecruitmentStage = existingStage;

            service.CompleteProfile(candidate, MakeProfileStepModel());

            Assert.Equal(existingStage, candidate.RecruitmentStage);
        }

        [Theory]
        [InlineData(RecruitmentStages.Registered)]
        [InlineData(RecruitmentStages.ProfilePending)]
        [InlineData(RecruitmentStages.ResumePending)]
        [InlineData(RecruitmentStages.ProfileUnderReview)]
        public void CompleteProfile_CandidateStillOnboarding_AdvancesToResumePending(string existingStage)
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.RecruitmentStage = existingStage;

            service.CompleteProfile(candidate, MakeProfileStepModel());

            Assert.Equal(RecruitmentStages.ResumePending, candidate.RecruitmentStage);
        }

        [Theory]
        [InlineData(RecruitmentStages.AssessmentCompleted)]
        [InlineData(RecruitmentStages.InterviewInProgress)]
        [InlineData(RecruitmentStages.OfferAccepted)]
        public void CompleteResumeAndSkills_CandidateAlreadyProgressed_DoesNotResetStage(string existingStage)
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.RecruitmentStage = existingStage;
            candidate.CandidateId = "VWT202600042"; // already issued, as a progressed candidate would have

            service.CompleteResumeAndSkills(candidate, "resumes/candidate1.pdf", "", MakeResumeSkillModel());

            Assert.Equal(existingStage, candidate.RecruitmentStage);
        }

        [Fact]
        public void CompleteResumeAndSkills_NewCandidate_StillAdvancesToProfileUnderReview()
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.RecruitmentStage = RecruitmentStages.ResumePending;

            service.CompleteResumeAndSkills(candidate, "resumes/candidate1.pdf", "", MakeResumeSkillModel());

            Assert.Equal(RecruitmentStages.ProfileUnderReview, candidate.RecruitmentStage);
        }

        [Theory]
        [InlineData(RecruitmentStages.AssessmentCompleted)]
        [InlineData(RecruitmentStages.Shortlisted)]
        [InlineData(RecruitmentStages.InterviewInProgress)]
        public void IssueCandidateId_CandidateAlreadyProgressed_DoesNotResetStage(string existingStage)
        {
            using var db = BuildContext();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = BuildService(db, cache);
            var candidate = MakeCandidate();
            candidate.RecruitmentStage = existingStage;

            service.IssueCandidateId(candidate);

            Assert.Equal(existingStage, candidate.RecruitmentStage);
        }
    }
}
