using System.ComponentModel.DataAnnotations;
using ExamPortal.Services;

namespace ExamPortal.Models
{
    public class User
    {
        public int Id { get; set; }
        [Required] public string Username { get; set; } = "";
        [Required] public string Email { get; set; } = "";
        [Required] public string PasswordHash { get; set; } = "";
        public string FullName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string MobileNumber { get; set; } = "";
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; } = "";
        public string CurrentLocation { get; set; } = "";
        public string PermanentAddress { get; set; } = "";
        public string CollegeUniversityName { get; set; } = "";
        public string Degree { get; set; } = "";
        public string BranchSpecialization { get; set; } = "";
        public int? GraduationYear { get; set; }
        public decimal? CgpaOrPercentage { get; set; }
        public decimal? TenthPercentage { get; set; }
        public decimal? TwelfthPercentage { get; set; }
        public string BacklogInformation { get; set; } = "";
        public string ResumePath { get; set; } = "";
        public string ProfilePhotoPath { get; set; } = "";
        public string Skills { get; set; } = "";
        public string Certifications { get; set; } = "";
        public string Projects { get; set; } = "";
        public string Internships { get; set; } = "";
        public string WorkExperience { get; set; } = "";
        public string LinkedInUrl { get; set; } = "";
        public string GitHubUrl { get; set; } = "";
        public string PortfolioWebsite { get; set; } = "";
        public string PreferredJobLocations { get; set; } = "";
        public string CurrentAcademicStatus { get; set; } = "";
        public string GapInEducation { get; set; } = "";
        public string Role { get; set; } = "Candidate"; // Candidate | Admin
        /// <summary>Unique human-readable ID generated at registration, e.g. VWT202600001.</summary>
        [StringLength(32)]
        public string CandidateId { get; set; } = "";
        /// <summary>Current stage in the 16-step recruitment pipeline.</summary>
        public string RecruitmentStage { get; set; } = "Registered";
        public bool IsEmailVerified { get; set; }
        public bool IsMobileVerified { get; set; }

        /// <summary>How this account authenticates: "Local" (email/password, the default —
        /// used by all Recruiter/HR/Admin accounts and candidates who registered normally)
        /// or "Google" (candidate signed up/linked via Google Sign-In). Google sign-in is
        /// only ever wired up for Role == Candidate — see AccountController.ExternalAuth.cs.
        /// Informational only; a "Google" account can still use "Forgot password" to also
        /// enable local login, so this is not itself an access-control check.</summary>
        public string AuthProvider { get; set; } = "Local";

        /// <summary>Google's stable "sub" (subject) claim for accounts linked to Google
        /// Sign-In. Preferred over matching on Email alone when re-authenticating, since a
        /// Google account's email can change; empty for accounts that have never linked
        /// Google.</summary>
        public string GoogleId { get; set; } = "";

        /// <summary>Legacy field from a since-removed "Continue with LinkedIn" sign-in
        /// feature — LinkedIn's OIDC "sub" claim for any candidate who signed in that way
        /// while it existed. No longer written to by any current code path; kept
        /// (rather than dropped in a migration) only so those old account rows keep
        /// reading back correctly. Same shape as GoogleId above.</summary>
        public string LinkedInId { get; set; } = "";

        public string EmailVerificationToken { get; set; } = "";
        public DateTime? MobileOtpExpiresAt { get; set; }
        /// <summary>SHA-256 hash of the current password-reset token. Never store the raw token —
        /// only the hash, so a DB leak alone can't be used to reset a password. Cleared after use.</summary>
        public string PasswordResetTokenHash { get; set; } = "";
        public DateTime? PasswordResetTokenExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public CandidateProfile? CandidateProfile { get; set; }
        public List<ExamAttempt> Attempts { get; set; } = new();
        public List<EligibilityResult> EligibilityResults { get; set; } = new();
        public List<NotificationMessage> Notifications { get; set; } = new();
        public CandidatePersonalDetail? PersonalDetail { get; set; }
        public CandidateAddress? Address { get; set; }
        /// <summary>Structured education history — one row per level (PG/UG/12th/10th). See <see cref="CandidateEducationRecord"/>.</summary>
        public List<CandidateEducationRecord> EducationRecords { get; set; } = new();
        public CandidateProfessionalProfile? ProfessionalProfile { get; set; }
        public CandidateSocialProfile? SocialProfile { get; set; }
        public CandidateJobPreference? JobPreference { get; set; }
        public CandidateProfileCompletion? ProfileCompletion { get; set; }
        public List<CandidateLanguage> Languages { get; set; } = new();
    }

    public class CandidateProfile
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string VerificationStatus { get; set; } = "Pending";
        public bool IsProfileComplete { get; set; }
        public bool IsResumeValidated { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CandidatePersonalDetail
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CandidateAddress
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string AddressLine { get; set; } = "";
        public string State { get; set; } = "";
        public string City { get; set; } = "";
        public string Country { get; set; } = "";
        public string Pincode { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Education level discriminator values for <see cref="CandidateEducationRecord"/>.</summary>
    public static class EducationLevels
    {
        public const string Postgraduate = "Postgraduate";
        public const string Undergraduate = "Undergraduate";
        public const string Intermediate = "Intermediate";
        public const string Secondary = "Secondary";

        public static readonly string[] All = { Postgraduate, Undergraduate, Intermediate, Secondary };
    }

    /// <summary>Status values for <see cref="CandidateEducationRecord"/>. There is no
    /// "Pursuing" value — every candidate applying here has already finished their
    /// education, at every level (see EducationLevelFieldset.cshtml).</summary>
    public static class EducationStatuses
    {
        public const string Completed = "Completed";
        public const string NotApplicable = "Not Applicable";

        public static readonly string[] All = { Completed, NotApplicable };
    }

    /// <summary>Document types the candidate document-upload workflow understands — see
    /// HomeController.UploadDocument, AdminController.Documents.cs, and
    /// Views/Home/Index.cshtml's "Upload Required Documents" section, all of which must
    /// stay in sync with this list rather than keeping their own separate copies.</summary>
    public static class DocumentTypes
    {
        public const string Aadhar = "Aadhar Card";
        public const string Pan = "PAN Card";
        public const string MarkSheet10 = "10th Marksheet";
        public const string MarkSheet12 = "12th Marksheet";
        public const string Degree = "Degree Certificate";
        public const string ExperienceLetter = "Experience Letter";
        public const string Other = "Other";

        /// <summary>Every one of these must have a Verified CandidateDocument before a
        /// candidate's RecruitmentStage can advance to DocumentsVerified — see
        /// AdminController.VerifyDocument. "Other" is intentionally excluded: it's an
        /// optional catch-all slot, so an unrelated Pending/unused "Other" upload must
        /// never block the required set from completing. "Experience Letter" is also
        /// excluded — it only applies to candidates who actually have prior work
        /// experience, so it's optional-if-applicable rather than mandatory for everyone
        /// (see RequestDocuments' own email wording, "Experience Letters (if
        /// applicable)", which already reflected this even before Required did).</summary>
        public static readonly string[] Required = { Aadhar, Pan, MarkSheet10, MarkSheet12, Degree };

        /// <summary>Every document type the candidate upload form offers, required or not
        /// — used to reject an upload for any type outside this known set. Includes
        /// ExperienceLetter (optional-if-applicable, see Required above) and Other.</summary>
        public static readonly string[] AllOffered = { Aadhar, Pan, MarkSheet10, MarkSheet12, Degree, ExperienceLetter, Other };
    }

    /// <summary>
    /// One row per education level (Postgraduate / Undergraduate / Intermediate / Secondary)
    /// per candidate. Replaces the old 1:1 <c>CandidateEducation</c> entity, which was never
    /// actually populated by the registration wizard (Step 3 wrote flat <see cref="User"/>
    /// columns instead) — that gap silently broke eligibility checks that read
    /// <c>candidate.Education</c>. This table is now the single source of truth for
    /// education and is read/written directly by the registration flow.
    /// </summary>
    public class CandidateEducationRecord
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }

        /// <summary>Postgraduate | Undergraduate | Intermediate | Secondary — see <see cref="EducationLevels"/>.</summary>
        [StringLength(20)]
        public string Level { get; set; } = "";

        /// <summary>Degree/course type, e.g. "M.Tech", "B.Tech", "CBSE", "State Board".</summary>
        [StringLength(100)]
        public string DegreeOrCourse { get; set; } = "";

        /// <summary>Institute / College / School name.</summary>
        [StringLength(150)]
        public string InstituteName { get; set; } = "";

        /// <summary>Board (10th/12th) or University (PG/UG).</summary>
        [StringLength(150)]
        public string BoardOrUniversity { get; set; } = "";

        /// <summary>Stream / branch — most relevant for B.Tech/M.Tech, optional elsewhere.</summary>
        [StringLength(100)]
        public string StreamBranch { get; set; } = "";

        public int? YearOfPassing { get; set; }

        public decimal? MarksValue { get; set; }

        /// <summary>"Percentage" or "CGPA".</summary>
        [StringLength(20)]
        public string MarksType { get; set; } = "Percentage";

        /// <summary>Completed | Not Applicable — see <see cref="EducationStatuses"/>.</summary>
        [StringLength(20)]
        public string Status { get; set; } = "";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CandidateProfessionalProfile
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        // Fresher-only fields — no work experience or current company
        public string Skills { get; set; } = "";
        public string Certifications { get; set; } = "";
        public string Projects { get; set; } = "";
        public string Internships { get; set; } = "";
        public string LinkedInUrl { get; set; } = "";
        public string GitHubUrl { get; set; } = "";
        public string PortfolioUrl { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CandidateSocialProfile
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string LinkedInUrl { get; set; } = "";
        public string GitHubUrl { get; set; } = "";
        public string PortfolioUrl { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CandidateJobPreference
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string PreferredJobLocation { get; set; } = "";
        public decimal? ExpectedSalary { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CandidateLanguage
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CandidateProfileCompletion
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int CompletionPercentage { get; set; }
        public int CompletedFields { get; set; }
        public int TotalFields { get; set; }
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Exam
    {
        public int Id { get; set; }
        [Required] public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Instructions { get; set; } = "";
        public string Subject { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public bool IsTemplate { get; set; }
        public int RandomQuestionCount { get; set; }
        public int DurationMinutes { get; set; } = 60;
        public int TotalMarks { get; set; }
        public int PassingMarks { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string Status { get; set; } = "Draft"; // Draft | Scheduled | Active | Closed | Archived
        public bool IsActive { get; set; } = true;
        public int TotalQuestions { get; set; }
        public decimal NegativeMarks { get; set; }
        public bool UseQuestionBank { get; set; } = true;
        public bool RandomizeQuestions { get; set; } = true;
        public bool RandomizeOptions { get; set; } = true;
        public bool AllowResumeAssessment { get; set; } = true;
        public bool RequireFullScreen { get; set; } = true;
        public bool RestrictCopyPaste { get; set; } = true;
        public bool RequireWebcam { get; set; }
        public bool RequireScreenMonitoring { get; set; } = true;
        public int CutoffScore { get; set; }
        public string RequiredEducation { get; set; } = "";
        public string RequiredSkills { get; set; } = "";
        public string EligibleLocations { get; set; } = "";
        public string ExperienceLevel { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<AssessmentSection> Sections { get; set; } = new();
        public List<Question> Questions { get; set; } = new();
        public List<ExamAttempt> Attempts { get; set; } = new();
        public List<EligibilityResult> EligibilityResults { get; set; } = new();
        public List<AssessmentReport> Reports { get; set; } = new();
    }

    public class AssessmentSection
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public Exam? Exam { get; set; }
        [Required] public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int TimeLimitMinutes { get; set; }
        public int WeightPercent { get; set; } = 100;
        public int DisplayOrder { get; set; }
        public List<Question> Questions { get; set; } = new();
    }

    public class Question
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public Exam? Exam { get; set; }
        public int? AssessmentSectionId { get; set; }
        public AssessmentSection? AssessmentSection { get; set; }
        [Required] public string Text { get; set; } = "";
        public string Category { get; set; } = "Technical MCQ";
        public string QuestionType { get; set; } = "Single Choice MCQ";
        public string Difficulty { get; set; } = "Medium";
        public string Tags { get; set; } = "";
        public bool IsQuestionBankItem { get; set; } = true;
        public int DisplayOrder { get; set; }
        public string OptionA { get; set; } = "";
        public string OptionB { get; set; } = "";
        public string OptionC { get; set; } = "";
        public string OptionD { get; set; } = "";
        public string CorrectAnswer { get; set; } = "A"; // A,B,C,D
        public int Marks { get; set; } = 1;
        public decimal NegativeMarks { get; set; }
        public CodingQuestion? CodingQuestion { get; set; }
    }

    public class CodingQuestion
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public Question? Question { get; set; }
        public string StarterCode { get; set; } = "";
        public string SupportedLanguages { get; set; } = "C#, JavaScript, Python";
        public string SampleTestCases { get; set; } = "";
        public string HiddenTestCases { get; set; } = "";
        public int RuntimeLimitSeconds { get; set; } = 2;
        public int MemoryLimitMb { get; set; } = 128;
    }

    public class ExamAttempt
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int ExamId { get; set; }
        public Exam? Exam { get; set; }
        public int Score { get; set; }
        public int TotalMarks { get; set; }
        public bool Passed { get; set; }
        public string Status { get; set; } = "InProgress"; // InProgress | Submitted | AutoSubmitted | Evaluated
        public int EligibilityScore { get; set; }
        public string EligibilityStatus { get; set; } = "Pending";
        public string QuestionOrder { get; set; } = "";
        public string ProctoringSessionId { get; set; } = "";
        public int Rank { get; set; }
        public decimal PercentageScore { get; set; }
        public DateTime? LastAutoSavedAt { get; set; }
        public DateTime? LastHeartbeatAt { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SubmittedAt { get; set; }
        public List<UserAnswer> Answers { get; set; } = new();
        public List<CodingSubmission> CodingSubmissions { get; set; } = new();
        public List<Score> Scores { get; set; } = new();
        public List<ProctoringLog> ProctoringLogs { get; set; } = new();
    }

    public class UserAnswer
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public ExamAttempt? Attempt { get; set; }
        public int QuestionId { get; set; }
        public Question? Question { get; set; }
        public string SelectedAnswer { get; set; } = "";
        public string TextAnswer { get; set; } = "";
        public bool IsCorrect { get; set; }
        public decimal ScoreAwarded { get; set; }
        public bool MarkedForReview { get; set; }
    }

    public class CodingSubmission
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public ExamAttempt? Attempt { get; set; }
        public int QuestionId { get; set; }
        public Question? Question { get; set; }
        public string Language { get; set; } = "";
        public string SourceCode { get; set; } = "";
        public decimal ScoreAwarded { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    }

    public class Score
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public ExamAttempt? Attempt { get; set; }
        public string SectionName { get; set; } = "";
        public decimal MaxScore { get; set; }
        public decimal AwardedScore { get; set; }
        public decimal WeightedScore { get; set; }
    }

    public class EligibilityResult
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int ExamId { get; set; }
        public Exam? Exam { get; set; }
        public bool RegistrationValid { get; set; }
        public bool EducationValid { get; set; }
        public bool ExperienceValid { get; set; }
        public bool SkillsMatched { get; set; }
        public bool LocationEligible { get; set; }
        public int EligibilityScore { get; set; }
        public string Status { get; set; } = "Pending";
        public string Notes { get; set; } = "";
        public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    }

    public class NotificationMessage
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int? ExamId { get; set; }
        public Exam? Exam { get; set; }
        public string Type { get; set; } = "";
        public string Channel { get; set; } = "Email"; // Email | SMS | WhatsApp | InApp | Push
        public string Recipient { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public string Status { get; set; } = "Queued"; // Queued | Processing | Sent | Failed | Skipped
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 3;
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ProviderMessageId { get; set; } = "";
        public string AttachmentFileName { get; set; } = "";
        public byte[]? AttachmentBytes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ProctoringLog
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public ExamAttempt? Attempt { get; set; }
        public string EventType { get; set; } = "";
        public string Details { get; set; } = "";
        public int Severity { get; set; } = 1;
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    public class AuditLog
    {
        public int Id { get; set; }
        public string Actor { get; set; } = "";
        public string Action { get; set; } = "";
        public string EntityName { get; set; } = "";
        public int? EntityId { get; set; }
        public string Details { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SystemSetting
    {
        public int Id { get; set; }
        [Required, StringLength(120)] public string Key { get; set; } = "";
        [StringLength(120)] public string Category { get; set; } = "General";
        [StringLength(2000)] public string Value { get; set; } = "";
        [StringLength(500)] public string Description { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AssessmentReport
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public Exam? Exam { get; set; }
        public string ReportType { get; set; } = "Analytics";
        public int InvitedCandidates { get; set; }
        public int CompletedAttempts { get; set; }
        public decimal AverageScore { get; set; }
        public int EligibleCandidates { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    // View Models
    public class ExamTakeViewModel
    {
        public Exam Exam { get; set; } = new();
        public int AttemptId { get; set; }
        public Dictionary<int, string> Answers { get; set; } = new();
        public Dictionary<int, string> TextAnswers { get; set; } = new();
    }

    public class ExamResultViewModel
    {
        public ExamAttempt Attempt { get; set; } = new();
        public Exam Exam { get; set; } = new();
        public List<QuestionResultItem> Results { get; set; } = new();
    }

    public class QuestionResultItem
    {
        public Question Question { get; set; } = new();
        public string UserAnswer { get; set; } = "";
        public bool IsCorrect { get; set; }
    }

    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalExams { get; set; }
        public int TotalAttempts { get; set; }
        public List<Exam> Exams { get; set; } = new();
        public List<ExamAttempt> RecentAttempts { get; set; } = new();

        /// <summary>Admin-only "Recent Activity" — platform administration events
        /// (staff/user/role/system-setting changes). Deliberately excludes candidate
        /// recruitment activity — see ActivityFeedService.BuildAdminFeed.</summary>
        public List<ActivityItem> ActivityFeed { get; set; } = new();

        /// <summary>Admin-only alerts: failed logins, security signals, and platform
        /// health/monitoring. See ActivityFeedService.BuildAdminAlerts.</summary>
        public List<PortalAlert> Alerts { get; set; } = new();
    }

    public class CreateExamViewModel
    {
        [Required, StringLength(200)] public string Title { get; set; } = "";
        [StringLength(2000)] public string Description { get; set; } = "";
        [StringLength(4000)] public string Instructions { get; set; } = "";
        [Required, StringLength(100)] public string Subject { get; set; } = "";
        [StringLength(150)] public string TemplateName { get; set; } = "";
        public bool IsTemplate { get; set; }
        [Range(0, 500)] public int RandomQuestionCount { get; set; }
        [Range(5, 300)] public int DurationMinutes { get; set; } = 60;
        [Range(1, 100)] public int PassingMarks { get; set; } = 35;
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        /// <summary>IANA time zone the Start/End fields above were entered in — used to convert
        /// them into a true UTC instant before storage. Defaults to IST.</summary>
        [StringLength(100)] public string TimeZone { get; set; } = "Asia/Kolkata";
        [StringLength(50)] public string Status { get; set; } = "Scheduled";
        [StringLength(300)] public string RequiredEducation { get; set; } = "";
        [StringLength(500)] public string RequiredSkills { get; set; } = "";
        [StringLength(500)] public string EligibleLocations { get; set; } = "";
        [StringLength(100)] public string ExperienceLevel { get; set; } = "";
        public decimal NegativeMarks { get; set; }
        public bool UseQuestionBank { get; set; } = true;
        public bool RandomizeQuestions { get; set; } = true;
        public bool RandomizeOptions { get; set; } = true;
        public bool AllowResumeAssessment { get; set; } = true;
        public bool RequireFullScreen { get; set; } = true;
        public bool RestrictCopyPaste { get; set; } = true;
        public bool RequireWebcam { get; set; }
        public bool RequireScreenMonitoring { get; set; } = true;
        public List<QuestionInput> Questions { get; set; } = new();
    }

    /// <summary>
    /// Deliberately narrow — only what an existing assessment's schedule needs, not the full
    /// CreateExamViewModel. Editing questions/scoring on a live assessment (one candidates may
    /// already be mid-attempt on) is a separate, riskier feature this doesn't attempt; this is
    /// just for the everyday task of extending/rescheduling a window that's expired or needs to
    /// move, without recreating the assessment (which would orphan its existing attempts/results).
    /// </summary>
    public class EditExamScheduleViewModel
    {
        public int Id { get; set; }
        [Required, StringLength(200)] public string Title { get; set; } = "";
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        /// <summary>IANA time zone the Start/End fields above should be read in. Exam doesn't
        /// persist which zone was used originally (only the resulting UTC instant), so this
        /// always defaults to Asia/Kolkata rather than trying to reconstruct it — see
        /// SchedulingHelper.ConvertUtcToWallClock.</summary>
        [StringLength(100)] public string TimeZone { get; set; } = "Asia/Kolkata";
        [StringLength(50)] public string Status { get; set; } = "Scheduled";
    }

    /// <summary>
    /// Deliberately narrow — name/email/phone only. Role changes stay on UpdateUserRole (which
    /// already exists and has its own "don't strip your own admin role" safeguard), and password
    /// resets are kept a separate, more sensitive operation rather than folded in here. Before
    /// this existed, CreateStaffUser had no edit counterpart at all — a typo'd staff email (which
    /// doubles as their login Username) was permanently unfixable through the app.
    /// </summary>
    public class EditStaffUserViewModel
    {
        public int Id { get; set; }
        [Required, StringLength(160)] public string FullName { get; set; } = "";
        [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = "";
        [StringLength(20)] public string Phone { get; set; } = "";
        /// <summary>Read-only display only — shown so the admin has context, not editable here.</summary>
        public string Role { get; set; } = "";
    }

    public class QuestionInput
    {
        [StringLength(2000)] public string Text { get; set; } = "";
        [StringLength(100)] public string Category { get; set; } = "Technical MCQ";
        [StringLength(100)] public string SectionName { get; set; } = "General";
        [StringLength(100)] public string QuestionType { get; set; } = "Single Choice MCQ";
        [StringLength(50)] public string Difficulty { get; set; } = "Medium";
        [StringLength(500)] public string Tags { get; set; } = "";
        public bool IsQuestionBankItem { get; set; } = true;
        [StringLength(1000)] public string OptionA { get; set; } = "";
        [StringLength(1000)] public string OptionB { get; set; } = "";
        [StringLength(1000)] public string OptionC { get; set; } = "";
        [StringLength(1000)] public string OptionD { get; set; } = "";
        [StringLength(50)] public string CorrectAnswer { get; set; } = "A";
        public int Marks { get; set; } = 1;
        public decimal NegativeMarks { get; set; }
        [StringLength(4000)] public string StarterCode { get; set; } = "";
        [StringLength(200)] public string SupportedLanguages { get; set; } = "C#, JavaScript, Python";
        [StringLength(4000)] public string SampleTestCases { get; set; } = "";
        [StringLength(4000)] public string HiddenTestCases { get; set; } = "";
    }


    // ── Recruitment Workflow Models ──────────────────────────────────────────

    /// <summary>Secure, time-limited invitation sent by admin to a candidate for a specific exam.</summary>
    public class AssessmentInvitation
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int ExamId { get; set; }
        public Exam? Exam { get; set; }
        /// <summary>Cryptographically random token embedded in the invitation link.</summary>
        public string Token { get; set; } = "";
        public string OneTimeLoginToken { get; set; } = "";
        public string Status { get; set; } = "Pending"; // Pending | Accepted | Expired | Revoked
        public DateTime? AssessmentDate { get; set; }
        public int DurationMinutes { get; set; }
        public int PassingMarks { get; set; }
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcceptedAt { get; set; }
        public DateTime? TokenUsedAt { get; set; }
    }

    /// <summary>One round of an interview for a shortlisted candidate.</summary>
    public class InterviewRecord
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string Round { get; set; } = "HR"; // HR | Technical | Managerial | Final
        public string Status { get; set; } = "Scheduled"; // Scheduled | Completed | Cancelled | Missed
        public string Outcome { get; set; } = ""; // Passed | Failed | On Hold
        public string Notes { get; set; } = "";
        public DateTime? ScheduledAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Extended scheduling fields ────────────────────────────────────────
        /// <summary>IANA time-zone name for the scheduled time, e.g. "Asia/Kolkata".</summary>
        public string TimeZone { get; set; } = "UTC";
        /// <summary>Duration of the interview in minutes.</summary>
        public int DurationMinutes { get; set; } = 60;
        /// <summary>Interview format: Online | In-Person | Hybrid</summary>
        public string Format { get; set; } = "Online";
        /// <summary>Physical location or virtual meeting URL / ID, e.g. "https://meet.google.com/abc-defg-hij".</summary>
        public string MeetingLink { get; set; } = "";
        /// <summary>Human-readable meeting ID shown in the email, e.g. "abc-defg-hij".</summary>
        public string MeetingId { get; set; } = "";
        /// <summary>Comma-separated names of interviewers.</summary>
        public string InterviewerNames { get; set; } = "";
        /// <summary>Comma-separated panel members assigned to the interview.</summary>
        public string PanelMembers { get; set; } = "";
        /// <summary>Action items the candidate must complete before the interview.</summary>
        public string CandidateActionItems { get; set; } = "";
        public string Feedback { get; set; } = "";
        public int? Rating { get; set; }
        public string Remarks { get; set; } = "";
        public string CandidateStatusAfterInterview { get; set; } = "";
        public string UpdatedBy { get; set; } = "";
        public DateTime? CancelledAt { get; set; }
    }

    /// <summary>Documents uploaded by a candidate after being shortlisted.</summary>
    public class CandidateDocument
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string DocumentType { get; set; } = ""; // Aadhar | PAN | MarkSheet10 | MarkSheet12 | Degree | ExperienceLetter | Other
        public string FilePath { get; set; } = "";
        public string OriginalFileName { get; set; } = "";
        public string VerificationStatus { get; set; } = "Pending"; // Pending | Verified | Rejected
        public string AdminNotes { get; set; } = "";
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime? VerifiedAt { get; set; }
        public string VerifiedBy { get; set; } = "";
        public string FileHash { get; set; } = "";
    }

    /// <summary>Offer letter generated for a selected candidate.</summary>
    public class OfferLetter
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string Designation { get; set; } = "";
        public string Department { get; set; } = "";
        public string Location { get; set; } = "";
        public decimal CtcLpa { get; set; }
        public decimal BaseSalaryLpa { get; set; }
        public decimal VariablePayLpa { get; set; }
        public decimal BonusLpa { get; set; }
        public string CompensationBreakup { get; set; } = "";
        public DateTime JoiningDate { get; set; }

        /// <summary>Last date the candidate can confirm acceptance before the offer is treated as
        /// withdrawn (letter clause 1 — "valid till"). Distinct from JoiningDate ("join on").
        /// Defaults to 7 days after issue when not explicitly set, matching prior behavior.</summary>
        public DateTime? OfferValidUntil { get; set; }

        // ── Monthly salary breakup (Annexure III) ───────────────────────────
        // These back the itemized compensation table on the offer letter PDF.
        // Deliberately separate from CtcLpa/BaseSalaryLpa/VariablePayLpa/BonusLpa
        // above (which remain the source of truth for dashboards, filters, and
        // reports elsewhere in the app) — this breakup only needs to be
        // internally consistent for what prints on the letter itself.
        public decimal BasicMonthly { get; set; }
        public decimal HraMonthly { get; set; }
        public decimal SpecialAllowanceMonthly { get; set; }
        public decimal EmployeeEpfMonthly { get; set; }
        public decimal EmployeeEsiMonthly { get; set; }
        public decimal TrainingChargesMonthly { get; set; }
        public decimal ProfessionalTaxMonthly { get; set; }
        public decimal EmployerEpfMonthly { get; set; }
        public decimal EmployerEsiMonthly { get; set; }
        public decimal PerformanceIncentiveAnnual { get; set; }

        public string Status { get; set; } = "Issued"; // Draft | PendingHRApproval | Issued | Accepted | Declined | Revoked | Rejected
        public string AcceptanceToken { get; set; } = "";
        public string DigitalSignature { get; set; } = "";
        public string SignedBy { get; set; } = "";
        public DateTime? SignedAt { get; set; }
        public string HrApprovalStatus { get; set; } = "Pending";
        public string HrApprovedBy { get; set; } = "";
        public DateTime? HrApprovedAt { get; set; }
        public string HrApprovalRemarks { get; set; } = "";
        public string RejectionReason { get; set; } = "";
        public DateTime? RejectedAt { get; set; }
        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcceptedAt { get; set; }
        public DateTime? JoiningConfirmedAt { get; set; }
        public List<OfferHistory> History { get; set; } = new();
    }

    public class OfferHistory
    {
        public int Id { get; set; }
        public int OfferLetterId { get; set; }
        public OfferLetter? OfferLetter { get; set; }
        public string Action { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Remarks { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Recruitment Stage enum (stored as string on User) ───────────────────
    // Registered → ProfileUnderReview → InvitationSent → AssessmentCompleted
    // → Shortlisted → InterviewInProgress → DocumentsRequested → DocumentsVerified
    // → OfferIssued → OfferAccepted → Onboarding
    // (Rejected can occur at most stages)

    // ── View-Models for new workflow ─────────────────────────────────────────

    public class CandidateDashboardViewModel
    {
        public User Candidate { get; set; } = new();
        public List<AssessmentInvitation> Invitations { get; set; } = new();
        public List<ExamAttempt> Attempts { get; set; } = new();
        public List<InterviewRecord> Interviews { get; set; } = new();
        public List<CandidateDocument> Documents { get; set; } = new();
        public List<NotificationMessage> Notifications { get; set; } = new();
        public OfferLetter? Offer { get; set; }

        /// <summary>The candidate's own "Recent Activity" timeline (their actions only —
        /// see ActivityFeedService.BuildCandidateFeed). Distinct from Notifications, which
        /// is the outbound-email log addressed to them.</summary>
        public List<ActivityItem> ActivityFeed { get; set; } = new();
    }

    /// <summary>Read-only "open role" summary shown on the candidate Jobs
    /// page — derived from active Exams grouped by Subject, the same
    /// pattern used by the Admin and Recruiter Jobs tabs. Not a persisted
    /// entity; there is no standalone Job/JobPosting table in this schema.</summary>
    public class OpenRoleViewModel
    {
        public string Role { get; set; } = "";
        public string Skills { get; set; } = "";
        public string Locations { get; set; } = "";
        public string Experience { get; set; } = "";
        public int OpenCount { get; set; }
    }

    public class AdminPipelineViewModel
    {
        public List<User> Registered { get; set; } = new();
        public List<User> UnderReview { get; set; } = new();
        public List<User> InvitationSent { get; set; } = new();
        public List<User> AssessmentCompleted { get; set; } = new();
        public List<User> Shortlisted { get; set; } = new();
        public List<User> InterviewInProgress { get; set; } = new();
        public List<User> DocumentsRequested { get; set; } = new();
        public List<User> DocumentsVerified { get; set; } = new();
        public List<User> OfferIssued { get; set; } = new();
        public List<User> OfferAccepted { get; set; } = new();
        public List<User> Onboarding { get; set; } = new();
        public List<Exam> Exams { get; set; } = new();
    }

    public class AdminCandidateDetailViewModel
    {
        public User Candidate { get; set; } = new();
        public List<AssessmentInvitation> Invitations { get; set; } = new();
        public List<ExamAttempt> Attempts { get; set; } = new();
        public List<InterviewRecord> Interviews { get; set; } = new();
        public List<CandidateDocument> Documents { get; set; } = new();
        public OfferLetter? Offer { get; set; }
        public List<OfferHistory> OfferHistory { get; set; } = new();
        public List<Exam> AvailableExams { get; set; } = new();
    }

    public class CandidateLoginViewModel
    {
        [Required] public string CandidateId { get; set; } = "";
        [Required][DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }

    public class ContactMessageViewModel
    {
        [Required(ErrorMessage = "Please enter your name.")]
        [StringLength(120)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = "";

        [StringLength(30)]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Please enter a message.")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "Please enter at least 10 characters.")]
        public string Message { get; set; } = "";

        /// <summary>Honeypot field — real visitors never see or fill this (hidden via
        /// CSS in the view), but simple spam bots that auto-fill every input will. Any
        /// non-empty value here means silently drop the submission rather than send it.</summary>
        public string? Website { get; set; }
    }

    public class ForgotPasswordViewModel
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
    }

    public class ResetPasswordViewModel : IValidatableObject
    {
        [Required] public string Email { get; set; } = "";
        [Required] public string Token { get; set; } = "";

        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
        public string NewPassword { get; set; } = "";

        [Required, DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";

        /// <summary>Same strength bar as registration (RegistrationValidation.
        /// IsPasswordStrongEnough) — previously only length was checked here, so a
        /// password registration would now reject (e.g. a common password, or one
        /// missing enough character variety) could still be set via a reset.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!string.IsNullOrEmpty(NewPassword) && !RegistrationValidation.IsPasswordStrongEnough(NewPassword, out var error))
            {
                yield return new ValidationResult(error, new[] { nameof(NewPassword) });
            }
        }
    }

    /// <summary>
    /// Data for the shared "_EmptyState" partial — a consistent icon +
    /// message + optional action used anywhere a list/table/panel has
    /// nothing to show yet (empty tables, empty kanban columns, empty
    /// info panels), instead of one-off bare text scattered per view.
    /// </summary>
    public class EmptyStateViewModel
    {
        /// <summary>Lucide icon name (e.g. "inbox", "users", "calendar-x").</summary>
        public string Icon { get; set; } = "inbox";
        public string Title { get; set; } = "Nothing here yet";
        public string? Message { get; set; }

        public string? ActionText { get; set; }
        public string? ActionUrl { get; set; }
        public string? ActionIcon { get; set; }

        public string? SecondaryText { get; set; }
        public string? SecondaryUrl { get; set; }

        /// <summary>Icon badge color: "default", "accent", "success", "warning".</summary>
        public string Variant { get; set; } = "default";

        /// <summary>Smaller footprint for table rows, kanban columns, and side-panel lists.</summary>
        public bool Compact { get; set; } = false;
    }

    /// <summary>
    /// Data for the shared "_InterviewScheduleFields" partial (Admin/CandidateDetail) —
    /// the Time Zone / Duration / Format / Meeting Link / Meeting ID / Interviewer /
    /// Panel fields that are identical between the "Schedule Interview" form
    /// (asp-action="ScheduleInterview") and the "Reschedule Interview" form
    /// (asp-action="RescheduleInterview"). Prefill values are left blank for a
    /// fresh schedule and populated from the existing Interview record for a
    /// reschedule. Purely a view-support type — not persisted, not an EF entity.
    /// </summary>
    public class InterviewScheduleFieldsViewModel
    {
        public string? MeetingLink { get; set; }
        public string? MeetingId { get; set; }
        public string? InterviewerNames { get; set; }
        public string? PanelMembers { get; set; }
    }

    public class IssueOfferViewModel
    {
        public int UserId { get; set; }
        [Required, StringLength(150)] public string Designation { get; set; } = "";
        [Required, StringLength(150)] public string Department { get; set; } = "";
        [Required, StringLength(150)] public string Location { get; set; } = "";
        [Required][Range(0, 9999)] public decimal CtcLpa { get; set; }
        [Range(0, 9999)] public decimal BaseSalaryLpa { get; set; }
        [Range(0, 9999)] public decimal VariablePayLpa { get; set; }
        [Range(0, 9999)] public decimal BonusLpa { get; set; }
        [StringLength(2000)] public string CompensationBreakup { get; set; } = "";
        [Required] public DateTime JoiningDate { get; set; } = DateTime.Today.AddDays(30);

        /// <summary>Last date the candidate can confirm acceptance (letter clause 1 — "valid till").
        /// Defaults to 7 days out, matching the prior hardcoded PDF text.</summary>
        [Required] public DateTime OfferValidUntil { get; set; } = DateTime.Today.AddDays(7);

        // ── Monthly salary breakup (Annexure III) — all optional; default to 0
        // so a recruiter can still issue an offer without filling every line,
        // same as the existing optional Base/Variable/Bonus fields above. ──
        [Range(0, 9999999)] public decimal BasicMonthly { get; set; }
        [Range(0, 9999999)] public decimal HraMonthly { get; set; }
        [Range(0, 9999999)] public decimal SpecialAllowanceMonthly { get; set; }
        [Range(0, 9999999)] public decimal EmployeeEpfMonthly { get; set; }
        [Range(0, 9999999)] public decimal EmployeeEsiMonthly { get; set; }
        [Range(0, 9999999)] public decimal TrainingChargesMonthly { get; set; }
        [Range(0, 9999999)] public decimal ProfessionalTaxMonthly { get; set; }
        [Range(0, 9999999)] public decimal EmployerEpfMonthly { get; set; }
        [Range(0, 9999999)] public decimal EmployerEsiMonthly { get; set; }
        [Range(0, 9999999)] public decimal PerformanceIncentiveAnnual { get; set; }
    }
}
