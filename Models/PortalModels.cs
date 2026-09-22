namespace ExamPortal.Models
{
    public static class PortalRoles
    {
        public const string Admin = "Admin";
        public const string Recruiter = "Recruiter";
        public const string Candidate = "Candidate";
        public const string Hr = "HR";

        public const string BackOfficeRoles = Admin + "," + Recruiter + "," + Hr;
    }

    /// <summary>Authentication scheme names used alongside the app's primary cookie
    /// scheme (CookieAuthenticationDefaults.AuthenticationScheme, registered in
    /// Program.cs). GoogleExternal is a short-lived, separate cookie the Google OAuth
    /// handler signs into transiently — AccountController.ExternalAuth.cs reads it once
    /// in GoogleCallback, then signs the person into the app's real cookie via the
    /// existing SignInUserAsync, and clears it. Candidates never end up signed in on
    /// the GoogleExternal scheme alone.</summary>
    public static class AuthSchemes
    {
        public const string GoogleExternal = "ExternalGoogleCookie";
    }

    public static class AuthProviders
    {
        public const string Local = "Local";
        public const string Google = "Google";
        public const string LinkedIn = "LinkedIn";
    }

    /// <summary>Registered as a singleton in Program.cs based on whether
    /// Authentication:Google:ClientId/ClientSecret are configured. Lets the Login/Register
    /// views decide whether to render the "Continue with Google" button, and lets
    /// AccountController.ExternalAuth.cs short-circuit with a friendly error instead of
    /// attempting a challenge against an unconfigured provider.</summary>
    public class GoogleSignInStatus
    {
        public bool IsConfigured { get; }
        public GoogleSignInStatus(bool isConfigured) => IsConfigured = isConfigured;
    }

    public static class RecruitmentStages
    {
        public const string Registered = "Registered";
        public const string EmailVerificationPending = "EmailVerificationPending";
        public const string ProfilePending = "ProfilePending";
        public const string ResumePending = "ResumePending";
        public const string SkillProfilePending = "SkillProfilePending";
        public const string ProfileUnderReview = "ProfileUnderReview";
        public const string InvitationSent = "InvitationSent";
        public const string AssessmentStarted = "AssessmentStarted";
        public const string AssessmentCompleted = "AssessmentCompleted";
        public const string Shortlisted = "Shortlisted";
        public const string InterviewInProgress = "InterviewInProgress";
        public const string DocumentsRequested = "DocumentsRequested";
        public const string DocumentsVerified = "DocumentsVerified";
        public const string OfferIssued = "OfferIssued";
        public const string OfferAccepted = "OfferAccepted";
        public const string OfferDeclined = "OfferDeclined";
        public const string Onboarding = "Onboarding";
        public const string Rejected = "Rejected";

        public static readonly string[] Ordered =
        {
            Registered,
            EmailVerificationPending,
            ProfilePending,
            ResumePending,
            SkillProfilePending,
            ProfileUnderReview,
            InvitationSent,
            AssessmentStarted,
            AssessmentCompleted,
            Shortlisted,
            InterviewInProgress,
            DocumentsRequested,
            DocumentsVerified,
            OfferIssued,
            OfferAccepted,
            Onboarding
        };

        /// <summary>
        /// Single source of truth for "which status color does this stage mean" —
        /// used everywhere a recruitment stage is rendered as a badge/tag/chip
        /// (Admin, Recruiter, Home, and shared portal dashboard views) so the
        /// same stage never shows up in a different color depending on the page.
        /// Returns one of: success, warning, primary, danger, secondary.
        /// </summary>
        public static string BadgeColor(string? stage) => stage switch
        {
            Shortlisted or OfferAccepted or DocumentsVerified or Onboarding => "success",
            Rejected or OfferDeclined => "danger",
            InvitationSent or AssessmentStarted or AssessmentCompleted or DocumentsRequested => "warning",
            InterviewInProgress or OfferIssued => "primary",
            _ => "secondary"
        };

        /// <summary>
        /// Human-readable operational label for a stage — inserts spaces before each
        /// internal capital ("InterviewInProgress" -> "Interview In Progress"). For
        /// back-office (Admin/Recruiter) views, where the precise stage name matters more
        /// than friendly phrasing. Was previously copy-pasted as an identical local Razor
        /// regex in both Admin/Index.cshtml ("friendlyLabel") and Admin/Pipeline.cshtml
        /// ("friendlyStage").
        /// Candidate-facing screens (Home/Index) intentionally use their own curated,
        /// softer copy instead (e.g. "Not Selected" rather than "Rejected") — that's a
        /// deliberate tone choice for the audience, not something to unify with this.
        /// </summary>
        public static string FriendlyLabel(string? stage) =>
            System.Text.RegularExpressions.Regex.Replace(stage ?? "", "([a-z])([A-Z])", "$1 $2");

        /// <summary>
        /// Stages in which it's valid to issue a *new* assessment invitation (Admin's
        /// CandidateDetail "Send Invitation" and Recruiter's bulk "Assign Assessment").
        /// ProfileUnderReview: the normal, single first-invite case. InvitationSent /
        /// AssessmentStarted: re-inviting is allowed here too (e.g. bulk-assigning a
        /// second assessment, or replacing a stale link) since AssessmentInvitationService.Issue
        /// revokes the prior Pending/Accepted invitation before creating the new one.
        /// Deliberately does NOT include later stages (Shortlisted, OfferIssued, Onboarding,
        /// etc.) — inviting a candidate back into an assessment after the pipeline has moved
        /// past it is not a normal action and should go through ResendInvitation if it's
        /// ever genuinely needed (see ResendInvitation, which is intentionally stage-agnostic).
        /// </summary>
        public static readonly string[] InvitableStages =
        {
            ProfileUnderReview,
            InvitationSent,
            AssessmentStarted
        };

        public static bool CanIssueInvitation(string? stage) => InvitableStages.Contains(stage);
    }

    /// <summary>
    /// Single source of truth for mapping a semantic color name ("success"/"danger"/
    /// "warning"/"primary"/"info"/etc — as produced by <see cref="RecruitmentStages.BadgeColor"/>,
    /// or used directly for invitation/interview/offer/notification status) to the
    /// "ui-badge-*" CSS class used to render it.
    /// Previously this exact switch was copy-pasted as a local Razor <c>Func&lt;string,string&gt;</c>
    /// in eight different views (Admin/Index, Admin/Students, Admin/ExamResults,
    /// Admin/CandidateDetail, Recruiter/Index, Home/Index, Home/Results, Shared/_PortalDashboard).
    /// Centralized here so there is exactly one mapping to update.
    /// </summary>
    public static class UiBadge
    {
        public static string CssClass(string? color) => color switch
        {
            "success" => "ui-badge-success",
            "danger" => "ui-badge-danger",
            "warning" => "ui-badge-warning",
            "primary" => "ui-badge-primary",
            "info" => "ui-badge-info",
            _ => "ui-badge-neutral"
        };

        /// <summary>Convenience for the common case of badging a RecruitmentStage directly.</summary>
        public static string StageCssClass(string? stage) => CssClass(RecruitmentStages.BadgeColor(stage));
    }

    /// <summary>
    /// Backs the shared _CandidateIdentityCell partial (Views/Shared) used by both
    /// Admin/Index's and Recruiter/Index's "Candidates" tab tables, so the
    /// name/ID/email cell markup exists in one place instead of two near-identical copies.
    /// ShowCandidateIdInline lets Admin's table (which has its own separate ID column)
    /// keep that column instead of showing the ID twice.
    /// </summary>
    public class CandidateIdentityCellViewModel
    {
        public required User Candidate { get; set; }
        public bool ShowCandidateIdInline { get; set; } = false;
    }

    public class PortalDashboardViewModel
    {
        public string PortalName { get; set; } = "";
        public string PortalRole { get; set; } = "";
        public string WelcomeMessage { get; set; } = "";
        public List<PortalMetric> Metrics { get; set; } = new();
        public List<PortalModule> Modules { get; set; } = new();
        public List<User> PriorityCandidates { get; set; } = new();
        public List<ExamAttempt> RecentAttempts { get; set; } = new();
        public List<InterviewRecord> UpcomingInterviews { get; set; } = new();
        public List<OfferLetter> ActiveOffers { get; set; } = new();
        public List<NotificationMessage> Notifications { get; set; } = new();
        public Dictionary<string, int> PipelineCounts { get; set; } = new();

        /// <summary>Role-scoped "Recent Activity" timeline, built by ActivityFeedService.
        /// Replaces the ad-hoc view-local merges that used to live in each dashboard's
        /// .cshtml (see ActivityFeedService for why).</summary>
        public List<ActivityItem> ActivityFeed { get; set; } = new();

        /// <summary>Role-scoped, actionable "Notifications" panel — derived from real
        /// pending-work signals (e.g. "3 documents pending verification"), not the raw
        /// outbound-email log. Built by ActivityFeedService.</summary>
        public List<PortalAlert> Alerts { get; set; } = new();
    }

    /// <summary>
    /// One entry in a role's "Recent Activity" timeline. Deliberately built only from
    /// events that have a real, trustworthy timestamp on an existing entity — see
    /// ActivityFeedService for the source of each role's feed.
    /// </summary>
    public class ActivityItem
    {
        public DateTime When { get; set; }
        public string Icon { get; set; } = "activity";
        public string Text { get; set; } = "";
        public string Sub { get; set; } = "";
    }

    /// <summary>
    /// One entry in a role's "Notifications" panel — an actionable, derived signal
    /// ("2 offers expiring soon"), not a copy of an outbound email. Severity drives the
    /// badge color; ActionController/Action/Fragment (all optional) let the card deep-link
    /// straight to the relevant board/tab.
    /// </summary>
    public class PortalAlert
    {
        public DateTime When { get; set; }
        public string Icon { get; set; } = "bell";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        /// <summary>info | warning | danger | success</summary>
        public string Severity { get; set; } = "info";
        public string? ActionController { get; set; }
        public string? ActionAction { get; set; }
        public string? ActionFragment { get; set; }
    }

    public class PortalMetric
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string Icon { get; set; } = "fas fa-chart-line";
        public string Color { get; set; } = "primary";
    }

    public class PortalModule
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "fas fa-layer-group";
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "";
        public string Color { get; set; } = "primary";
        public bool Enabled { get; set; } = true;

        // Optional URL fragment (e.g. "pipeline-interview") so two modules that
        // legitimately point at the same page (like Pipeline's Kanban board) can
        // still resolve to distinct, honest destinations instead of looking like
        // separate features while being byte-for-byte the same link. Display-only:
        // no route, controller, or authorization behavior depends on this.
        public string? Fragment { get; set; }
    }

    /// <summary>
    /// Predefined option lists for the candidate registration dropdowns.
    /// Centralized here so the view, server-side validation, and any future
    /// reporting/eligibility matching all stay in sync with the same values.
    /// </summary>
    public static class RegistrationOptions
    {
        public static readonly string[] Genders =
        {
            "Female", "Male", "Non-binary", "Prefer not to say"
        };

        public static readonly string[] States =
        {
            "Andhra Pradesh", "Arunachal Pradesh", "Assam", "Bihar", "Chhattisgarh", "Goa",
            "Gujarat", "Haryana", "Himachal Pradesh", "Jharkhand", "Karnataka", "Kerala",
            "Madhya Pradesh", "Maharashtra", "Manipur", "Meghalaya", "Mizoram", "Nagaland",
            "Odisha", "Punjab", "Rajasthan", "Sikkim", "Tamil Nadu", "Telangana", "Tripura",
            "Uttar Pradesh", "Uttarakhand", "West Bengal",
            "Delhi (NCT)", "Jammu and Kashmir", "Ladakh", "Puducherry", "Chandigarh",
            "Other"
        };

        public static readonly string[] Countries =
        {
            "India", "United States", "United Kingdom", "Canada", "Australia", "Germany",
            "France", "Singapore", "United Arab Emirates", "New Zealand", "Other"
        };

        public static readonly string[] Colleges =
        {
            "IIT Bombay", "IIT Delhi", "IIT Madras", "IIT Kanpur", "IIT Kharagpur",
            "IIT Roorkee", "IIT Hyderabad", "NIT Trichy", "NIT Warangal", "NIT Surathkal",
            "BITS Pilani", "VIT Vellore", "Anna University", "Osmania University",
            "JNTU Hyderabad", "Delhi University", "Other"
        };

        public static readonly string[] Branches =
        {
            "Computer Science Engineering", "Information Technology",
            "Electronics & Communication Engineering", "Electrical Engineering",
            "Mechanical Engineering", "Civil Engineering", "Chemical Engineering",
            "Biotechnology", "Aerospace Engineering", "Commerce", "Business Administration",
            "Arts & Humanities", "Science (General)", "Other"
        };

        /// <summary>Postgraduate degree/course options for the Education accordion.</summary>
        public static readonly string[] PostgraduateCourses =
        {
            "M.Tech", "M.E.", "MCA", "MBA", "M.Sc.", "M.Com.", "MA", "Ph.D.", "Other"
        };

        /// <summary>Undergraduate degree/course options for the Education accordion.</summary>
        public static readonly string[] UndergraduateCourses =
        {
            "B.Tech", "B.E.", "BCA", "B.Sc.", "B.Com.", "BA", "BBA", "Diploma", "Other"
        };

        /// <summary>Boards for the Intermediate (12th) education level.</summary>
        public static readonly string[] IntermediateBoards =
        {
            "CBSE", "ICSE", "State Board", "IB", "NIOS", "Other"
        };

        /// <summary>Boards for the Secondary (10th) education level.</summary>
        public static readonly string[] SecondaryBoards =
        {
            "CBSE", "ICSE", "State Board", "IB", "NIOS", "Other"
        };

        /// <summary>Streams for Intermediate / 12th (distinct from engineering Branches above).</summary>
        public static readonly string[] IntermediateStreams =
        {
            "Science (MPC)", "Science (BiPC)", "Commerce", "Arts / Humanities", "Vocational", "Other"
        };

        /// <summary>Graduation year dropdown: 60 years back through 5 years into the future.</summary>
        public static IReadOnlyList<int> GraduationYears { get; } =
            Enumerable.Range(DateTime.Today.Year - 60, 66).Reverse().ToList();

        public static readonly string[] Languages =
        {
            "Afrikaans", "Arabic", "Assamese", "Bengali", "Bulgarian", "Cantonese",
            "Croatian", "Czech", "Danish", "Dutch", "English", "Finnish", "French",
            "German", "Gujarati", "Greek", "Hebrew", "Hindi", "Hungarian",
            "Indonesian", "Italian", "Japanese", "Kannada", "Korean", "Malay",
            "Malayalam", "Mandarin", "Marathi", "Nepali", "Norwegian", "Odia",
            "Persian", "Polish", "Portuguese", "Punjabi", "Romanian", "Russian",
            "Sanskrit", "Sinhalese", "Spanish", "Swahili", "Swedish", "Tamil",
            "Telugu", "Thai", "Turkish", "Ukrainian", "Urdu", "Vietnamese", "Other"
        };

        public static readonly string[] LanguageProficiencies =
        {
            "Native", "Fluent", "Advanced", "Intermediate", "Basic"
        };

        public static readonly string[] JobLocations =
        {
            "Bengaluru", "Hyderabad", "Chennai", "Mumbai", "Pune", "Delhi NCR",
            "Kolkata", "Ahmedabad", "Kochi", "Coimbatore", "Visakhapatnam", "Remote",
            "Open to Relocate", "Other"
        };
    }

    /// <summary>
    /// Step 1 — Account creation only: 5 fields.
    /// All personal/education/skill/document fields have been moved to Steps 3–6.
    /// </summary>
    public class CandidateRegistrationViewModel : System.ComponentModel.DataAnnotations.IValidatableObject
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Full name is required."), System.ComponentModel.DataAnnotations.StringLength(160)]
        public string FullName { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Email is required."), System.ComponentModel.DataAnnotations.EmailAddress, System.ComponentModel.DataAnnotations.StringLength(150)]
        public string Email { get; set; } = "";

        /// <summary>Rendered as type="tel" in the view for mobile keyboards. Format is
        /// validated (and the value normalized to 10 bare digits) by
        /// RegistrationValidation in Validate() below — the [Phone] attribute alone was
        /// too permissive for production use (it accepts almost any string containing
        /// digits/dashes/parens).</summary>
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Phone number is required."), System.ComponentModel.DataAnnotations.StringLength(20)]
        public string PhoneNumber { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Password is required."), System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Password), System.ComponentModel.DataAnnotations.MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string Password { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Please confirm your password."), System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Password), System.ComponentModel.DataAnnotations.Compare(nameof(Password), ErrorMessage = "Password and confirm password must match.")]
        public string ConfirmPassword { get; set; } = "";

        /// <summary>
        /// Normalizes FullName/PhoneNumber (trim, collapse spaces, strip phone
        /// formatting) and validates the normalized values — format-only rules that
        /// don't need database access. Duplicate-email/phone and disposable-email
        /// checks stay in AccountController.Registration.cs, since those need the
        /// database/configuration and a specific, distinct error message.
        /// </summary>
        public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(System.ComponentModel.DataAnnotations.ValidationContext validationContext)
        {
            FullName = ExamPortal.Services.RegistrationValidation.NormalizeFullName(FullName);
            if (!ExamPortal.Services.RegistrationValidation.IsValidFullName(FullName))
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    "Enter a valid full name (letters, spaces, hyphens, and apostrophes only).",
                    new[] { nameof(FullName) });
            }

            var normalizedPhone = ExamPortal.Services.RegistrationValidation.NormalizePhoneNumber(PhoneNumber);
            if (!ExamPortal.Services.RegistrationValidation.IsValidIndianMobileNumber(normalizedPhone))
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    "Enter a valid 10-digit Indian mobile number.",
                    new[] { nameof(PhoneNumber) });
            }
            else
            {
                PhoneNumber = normalizedPhone;
            }

            // Password is normally already Required/MinLength-checked by the attributes
            // above by the time IValidatableObject runs, but guard anyway since
            // Validate() always executes regardless of other attribute results.
            if (!string.IsNullOrEmpty(Password) &&
                !ExamPortal.Services.RegistrationValidation.IsPasswordStrongEnough(Password, out var passwordError))
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(passwordError, new[] { nameof(Password) });
            }
        }
    }


    /// <summary>A single language + proficiency pair submitted from the registration form.</summary>
    public class LanguageKnownEntry
    {
        public string Language { get; set; } = "";
        public string Proficiency { get; set; } = "";
    }

    /// <summary>
    /// One education level's worth of fields, submitted from the Step 3 "Education" accordion.
    /// Maps 1:1 onto a <see cref="CandidateEducationRecord"/> row for a given
    /// <see cref="EducationLevels"/> value. Field-level attributes are intentionally not
    /// [Required] — required-ness depends on Status ("Not Applicable" relaxes everything;
    /// otherwise "Completed" is the only valid value, since every candidate here has
    /// already finished their education) and is enforced conditionally in
    /// CandidateProfileStepViewModel.Validate instead.
    /// </summary>
    public class EducationLevelEntry
    {
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string DegreeOrCourse { get; set; } = "";

        [System.ComponentModel.DataAnnotations.StringLength(150)]
        public string InstituteName { get; set; } = "";

        [System.ComponentModel.DataAnnotations.StringLength(150)]
        public string BoardOrUniversity { get; set; } = "";

        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string StreamBranch { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Range(1950, 2100)]
        public int? YearOfPassing { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 100)]
        public decimal? MarksValue { get; set; }

        /// <summary>"Percentage" or "CGPA".</summary>
        [System.ComponentModel.DataAnnotations.StringLength(20)]
        public string MarksType { get; set; } = "Percentage";

        /// <summary>Completed | Not Applicable — see <see cref="EducationStatuses"/>. There
        /// is no "Pursuing" state; every candidate here has already finished their education.</summary>
        [System.ComponentModel.DataAnnotations.StringLength(20)]
        public string Status { get; set; } = "";
    }

    /// <summary>Step-1-only registration payload — account creation happens with just these 4 fields.
    /// Profile, education, resume, and skills are collected later in the wizard (Steps 3-6).</summary>
    public class CandidateRegistrationDto
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class EmailOtpViewModel
    {
        public string Email { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(6, MinimumLength = 6)]
        public string Otp { get; set; } = "";
    }

    /// <summary>Step 3 — Personal profile: identity, address, languages, education.</summary>
    public class CandidateProfileStepViewModel : System.ComponentModel.DataAnnotations.IValidatableObject
    {
        // ── Identity ──────────────────────────────────────────────────────────
        /// <summary>Rendered as type="date" in the view for mobile date pickers.</summary>
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(30)]
        public string Gender { get; set; } = "";

        // ── Address ───────────────────────────────────────────────────────────
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(500)]
        public string PermanentAddress { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string City { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string State { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Country { get; set; } = "";

        /// <summary>inputmode="numeric" set in view for mobile numeric keyboard.</summary>
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(12)]
        public string Pincode { get; set; } = "";

        // ── Languages ─────────────────────────────────────────────────────────
        /// <summary>Structured language + proficiency entries; serialised to JSON by controller.</summary>
        public List<LanguageKnownEntry> LanguagesKnown { get; set; } = new();

        // ── Education ─────────────────────────────────────────────────────────
        // Structured per-level records — see Views/Account/CompleteProfile.cshtml's
        // Education accordion (Postgraduate / Undergraduate / Intermediate / Secondary).
        // Required-ness is conditional on each entry's Status; see Validate() below.

        /// <summary>Optional — most freshers won't have this. Defaults to Not Applicable.</summary>
        public EducationLevelEntry Postgraduate { get; set; } = new() { Status = EducationStatuses.NotApplicable };

        /// <summary>Mandatory for every candidate — the qualifying fresher degree.</summary>
        public EducationLevelEntry Undergraduate { get; set; } = new();

        /// <summary>Intermediate / 12th — mandatory for every candidate.</summary>
        public EducationLevelEntry Intermediate { get; set; } = new();

        /// <summary>10th / Secondary — mandatory for every candidate.</summary>
        public EducationLevelEntry Secondary { get; set; } = new();

        /// <summary>No longer collected on the Complete Profile form (nothing there posts
        /// to it) — kept only for backward compatibility with existing records and because
        /// Students.cshtml still displays it. Previously also fed a broken piece of Status
        /// derivation logic in CandidateWorkflowService that's since been removed — see
        /// that file's CompleteProfile for details.</summary>
        [System.ComponentModel.DataAnnotations.StringLength(80)]
        public string CurrentAcademicStatus { get; set; } = "";

        /// <summary>Replaces free-text BacklogInformation. "0" means none.</summary>
        [System.ComponentModel.DataAnnotations.Range(0, 50)]
        public int? BacklogCount { get; set; }

        /// <summary>Cleared / Active / N/A — replaces free-text backlog status.</summary>
        [System.ComponentModel.DataAnnotations.StringLength(20)]
        public string BacklogStatus { get; set; } = "";

        /// <summary>Structured gap selection; replaces free-text GapInEducation.</summary>
        [System.ComponentModel.DataAnnotations.StringLength(30)]
        public string GapInEducation { get; set; } = "None";

        // Adjust these two bounds if the actual minimum hiring age or a different
        // maximum-plausible-age policy applies — no such rule existed in the codebase
        // before this check, so 16/100 are reasonable, clearly-adjustable defaults
        // rather than a value read from anywhere else in the system.
        private const int MinCandidateAgeYears = 16;
        private const int MaxCandidateAgeYears = 100;

        public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(System.ComponentModel.DataAnnotations.ValidationContext validationContext)
        {
            if (DateOfBirth.HasValue && DateOfBirth.Value.Date >= DateTime.Today)
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult("Date of birth must be in the past.", new[] { nameof(DateOfBirth) });
            }
            else if (DateOfBirth.HasValue)
            {
                var today = DateTime.Today;
                var age = today.Year - DateOfBirth.Value.Year;
                if (DateOfBirth.Value.Date > today.AddYears(-age)) age--; // hasn't had this year's birthday yet
                if (age < MinCandidateAgeYears || age > MaxCandidateAgeYears)
                {
                    yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                        $"Date of birth must correspond to an age between {MinCandidateAgeYears} and {MaxCandidateAgeYears} years.",
                        new[] { nameof(DateOfBirth) });
                }
            }

            // Undergraduate, Intermediate, and Secondary are mandatory for every fresher.
            // Postgraduate is the only level allowed to be "Not Applicable". Every level
            // is always "Completed" by now — there's no "Pursuing" state anywhere (see
            // EducationLevelFieldset.cshtml) — so year/marks are always required too.
            foreach (var error in ValidateLevel(nameof(Undergraduate), Undergraduate, allowNotApplicable: false))
                yield return error;
            // Group/Course (DegreeOrCourse) isn't collected for Intermediate either — the field
            // is hidden in the view (ShowDegree: false in CompleteProfile.cshtml), same reasoning
            // as Secondary below: Stream/Branch already captures the meaningful distinction
            // (Science/Commerce/Arts), so a separate "Group/Course" free-text field was redundant.
            foreach (var error in ValidateLevel(nameof(Intermediate), Intermediate, allowNotApplicable: false, requireDegreeOrCourse: false))
                yield return error;
            // 10th/Secondary: Degree/Course and Stream/Branch aren't meaningful at this level
            // (there's no "degree" or "stream" in 10th grade), so they're not required/collected here.
            foreach (var error in ValidateLevel(nameof(Secondary), Secondary, allowNotApplicable: false, requireDegreeOrCourse: false))
                yield return error;
            foreach (var error in ValidateLevel(nameof(Postgraduate), Postgraduate, allowNotApplicable: true))
                yield return error;
        }

        /// <summary>
        /// Required-ness for one education level depends on its own Status:
        ///   • Not Applicable (Postgraduate only) — no further fields required.
        ///   • Completed — everything required (this is the only other valid value —
        ///     see EducationLevelFieldset.cshtml, which never lets the UI submit
        ///     anything else). Anything other than these two values — including the
        ///     no-longer-offered "Pursuing" — is rejected outright, so a raw POST
        ///     bypassing the UI can't use it to relax year/marks requirements.
        /// </summary>
        private static IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> ValidateLevel(
            string levelName, EducationLevelEntry entry, bool allowNotApplicable, bool requireDegreeOrCourse = true)
        {
            if (string.IsNullOrWhiteSpace(entry.Status))
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: please select a status.", new[] { $"{levelName}.{nameof(entry.Status)}" });
                yield break;
            }

            if (entry.Status == EducationStatuses.NotApplicable)
            {
                if (!allowNotApplicable)
                {
                    yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                        $"{levelName} is required for all candidates.", new[] { $"{levelName}.{nameof(entry.Status)}" });
                }
                yield break;
            }

            if (entry.Status != EducationStatuses.Completed)
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: invalid status.", new[] { $"{levelName}.{nameof(entry.Status)}" });
                yield break;
            }

            if (requireDegreeOrCourse && string.IsNullOrWhiteSpace(entry.DegreeOrCourse))
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: degree/course is required.", new[] { $"{levelName}.{nameof(entry.DegreeOrCourse)}" });
            if (string.IsNullOrWhiteSpace(entry.InstituteName))
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: institute/school name is required.", new[] { $"{levelName}.{nameof(entry.InstituteName)}" });
            if (string.IsNullOrWhiteSpace(entry.BoardOrUniversity))
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: board/university is required.", new[] { $"{levelName}.{nameof(entry.BoardOrUniversity)}" });

            if (!entry.YearOfPassing.HasValue)
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: year of passing is required.", new[] { $"{levelName}.{nameof(entry.YearOfPassing)}" });
            if (!entry.MarksValue.HasValue)
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: marks/CGPA is required.", new[] { $"{levelName}.{nameof(entry.MarksValue)}" });
            }
            else if (entry.MarksType == "CGPA" && entry.MarksValue.Value > 10)
            {
                // EducationLevelEntry.MarksValue only has a blanket [Range(0, 100)] —
                // adequate for Percentage, but that alone lets a "CGPA" of e.g. 87 through,
                // which is nonsensical on the standard 10-point CGPA scale used here.
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"{levelName}: CGPA must be between 0 and 10.", new[] { $"{levelName}.{nameof(entry.MarksValue)}" });
            }
        }
    }

    /// <summary>Step 4 (final) — Resume, skills, and preferences merged into one step.</summary>
    public class ResumeSkillStepViewModel : System.ComponentModel.DataAnnotations.IValidatableObject
    {
        // ── Resume & Photo ────────────────────────────────────────────────────
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Please upload your resume.")]
        public Microsoft.AspNetCore.Http.IFormFile? ResumeUpload { get; set; }

        public Microsoft.AspNetCore.Http.IFormFile? ProfilePhoto { get; set; }

        // ── Skills ────────────────────────────────────────────────────────────
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Please add at least one skill."),
         System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string Skills { get; set; } = "";

        // ── Online profiles ───────────────────────────────────────────────────
        /// <summary>Optional — format is checked manually in Validate() so an empty value doesn't fail [Url].</summary>
        [System.ComponentModel.DataAnnotations.StringLength(250)]
        public string LinkedInUrl { get; set; } = "";

        // ── Job preferences ───────────────────────────────────────────────────
        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string PreferredJobLocation { get; set; } = "";

        public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(System.ComponentModel.DataAnnotations.ValidationContext validationContext)
        {
            if (!string.IsNullOrWhiteSpace(LinkedInUrl) &&
                !(Uri.TryCreate(LinkedInUrl, UriKind.Absolute, out var uri) &&
                  (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    "The LinkedInUrl field is not a valid fully-qualified http or https URL.",
                    new[] { nameof(LinkedInUrl) });
            }
        }
    }
}
