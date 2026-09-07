using Microsoft.EntityFrameworkCore;
using ExamPortal.Models;
using Microsoft.AspNetCore.Identity;

namespace ExamPortal.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
        public DbSet<Exam> Exams => Set<Exam>();
        public DbSet<AssessmentSection> AssessmentSections => Set<AssessmentSection>();
        public DbSet<Question> Questions => Set<Question>();
        public DbSet<CodingQuestion> CodingQuestions => Set<CodingQuestion>();
        public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
        public DbSet<UserAnswer> UserAnswers => Set<UserAnswer>();
        public DbSet<CodingSubmission> CodingSubmissions => Set<CodingSubmission>();
        public DbSet<Score> Scores => Set<Score>();
        public DbSet<EligibilityResult> EligibilityResults => Set<EligibilityResult>();
        public DbSet<NotificationMessage> Notifications => Set<NotificationMessage>();
        public DbSet<ProctoringLog> ProctoringLogs => Set<ProctoringLog>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
        public DbSet<AssessmentReport> AssessmentReports => Set<AssessmentReport>();
        public DbSet<AssessmentInvitation> AssessmentInvitations => Set<AssessmentInvitation>();
        public DbSet<InterviewRecord> InterviewRecords => Set<InterviewRecord>();
        public DbSet<CandidateDocument> CandidateDocuments => Set<CandidateDocument>();
        public DbSet<OfferLetter> OfferLetters => Set<OfferLetter>();
        public DbSet<OfferHistory> OfferHistories => Set<OfferHistory>();
        public DbSet<CandidatePersonalDetail> CandidatePersonalDetails => Set<CandidatePersonalDetail>();
        public DbSet<CandidateAddress> CandidateAddresses => Set<CandidateAddress>();
        public DbSet<CandidateEducationRecord> CandidateEducationRecords => Set<CandidateEducationRecord>();
        public DbSet<CandidateProfessionalProfile> CandidateProfessionalProfiles => Set<CandidateProfessionalProfile>();
        public DbSet<CandidateSocialProfile> CandidateSocialProfiles => Set<CandidateSocialProfile>();
        public DbSet<CandidateJobPreference> CandidateJobPreferences => Set<CandidateJobPreference>();
        public DbSet<CandidateLanguage> CandidateLanguages => Set<CandidateLanguage>();
        public DbSet<CandidateProfileCompletion> CandidateProfileCompletions => Set<CandidateProfileCompletion>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();
            mb.Entity<User>()
                .Property(u => u.CandidateId)
                .HasMaxLength(32);
            mb.Entity<User>()
                .Property(u => u.Role)
                .HasMaxLength(50);
            mb.Entity<User>()
                .Property(u => u.RecruitmentStage)
                .HasMaxLength(80);
            mb.Entity<User>()
                .HasIndex(u => new { u.Role, u.RecruitmentStage });
            mb.Entity<User>()
                .Property(u => u.CgpaOrPercentage).HasPrecision(5, 2);
            mb.Entity<User>()
                .Property(u => u.TenthPercentage).HasPrecision(5, 2);
            mb.Entity<User>()
                .Property(u => u.TwelfthPercentage).HasPrecision(5, 2);
            mb.Entity<User>()
                .HasOne(u => u.CandidateProfile).WithOne(p => p.User).HasForeignKey<CandidateProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<User>()
                .HasOne(u => u.PersonalDetail).WithOne(p => p.User).HasForeignKey<CandidatePersonalDetail>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<User>()
                .HasOne(u => u.Address).WithOne(p => p.User).HasForeignKey<CandidateAddress>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<CandidateEducationRecord>()
                .HasOne(e => e.User).WithMany(u => u.EducationRecords).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<CandidateEducationRecord>()
                .Property(e => e.MarksValue).HasPrecision(5, 2);
            // One row per level per candidate (PG/UG/Intermediate/Secondary).
            mb.Entity<CandidateEducationRecord>()
                .HasIndex(e => new { e.UserId, e.Level }).IsUnique();
            mb.Entity<User>()
                .HasOne(u => u.ProfessionalProfile).WithOne(p => p.User).HasForeignKey<CandidateProfessionalProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<User>()
                .HasOne(u => u.SocialProfile).WithOne(p => p.User).HasForeignKey<CandidateSocialProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<User>()
                .HasOne(u => u.JobPreference).WithOne(p => p.User).HasForeignKey<CandidateJobPreference>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<User>()
                .HasOne(u => u.ProfileCompletion).WithOne(p => p.User).HasForeignKey<CandidateProfileCompletion>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<CandidateLanguage>()
                .HasOne(l => l.User).WithMany(u => u.Languages).HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);

            mb.Entity<Exam>()
                .Property(e => e.NegativeMarks).HasPrecision(5, 2);
            mb.Entity<Exam>()
                .Property(e => e.Status)
                .HasMaxLength(50);
            mb.Entity<Exam>()
                .HasIndex(e => new { e.IsActive, e.IsTemplate, e.Status });
            mb.Entity<Question>()
                .Property(q => q.NegativeMarks).HasPrecision(5, 2);
            mb.Entity<UserAnswer>()
                .Property(a => a.ScoreAwarded).HasPrecision(8, 2);
            mb.Entity<CodingSubmission>()
                .Property(s => s.ScoreAwarded).HasPrecision(8, 2);
            mb.Entity<Score>()
                .Property(s => s.MaxScore).HasPrecision(8, 2);
            mb.Entity<Score>()
                .Property(s => s.AwardedScore).HasPrecision(8, 2);
            mb.Entity<Score>()
                .Property(s => s.WeightedScore).HasPrecision(8, 2);
            mb.Entity<AssessmentReport>()
                .Property(r => r.AverageScore).HasPrecision(8, 2);
            mb.Entity<SystemSetting>()
                .HasIndex(s => s.Key)
                .IsUnique();

            mb.Entity<ExamAttempt>()
                .HasOne(a => a.User).WithMany(u => u.Attempts).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
            mb.Entity<ExamAttempt>()
                .Property(a => a.PercentageScore).HasPrecision(6, 2);
            mb.Entity<ExamAttempt>()
                .Property(a => a.Status)
                .HasMaxLength(50);
            mb.Entity<ExamAttempt>()
                .HasIndex(a => new { a.UserId, a.StartedAt });
            mb.Entity<ExamAttempt>()
                .HasIndex(a => new { a.ExamId, a.SubmittedAt });
            mb.Entity<ExamAttempt>()
                .HasIndex(a => a.Status);
            mb.Entity<ExamAttempt>()
                .HasOne(a => a.Exam).WithMany(e => e.Attempts).HasForeignKey(a => a.ExamId).OnDelete(DeleteBehavior.Restrict);
            mb.Entity<AssessmentSection>()
                .HasOne(s => s.Exam).WithMany(e => e.Sections).HasForeignKey(s => s.ExamId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<Question>()
                .HasOne(q => q.Exam).WithMany(e => e.Questions).HasForeignKey(q => q.ExamId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<Question>()
                .HasOne(q => q.AssessmentSection).WithMany(s => s.Questions).HasForeignKey(q => q.AssessmentSectionId).OnDelete(DeleteBehavior.Restrict);
            mb.Entity<CodingQuestion>()
                .HasOne(c => c.Question).WithOne(q => q.CodingQuestion).HasForeignKey<CodingQuestion>(c => c.QuestionId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<UserAnswer>()
                .HasOne(a => a.Attempt).WithMany(e => e.Answers).HasForeignKey(a => a.AttemptId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<CodingSubmission>()
                .HasOne(s => s.Attempt).WithMany(a => a.CodingSubmissions).HasForeignKey(s => s.AttemptId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<Score>()
                .HasOne(s => s.Attempt).WithMany(a => a.Scores).HasForeignKey(s => s.AttemptId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<EligibilityResult>()
                .HasOne(e => e.User).WithMany(u => u.EligibilityResults).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<EligibilityResult>()
                .HasOne(e => e.Exam).WithMany(a => a.EligibilityResults).HasForeignKey(e => e.ExamId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<NotificationMessage>()
                .HasOne(n => n.User).WithMany(u => u.Notifications).HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<NotificationMessage>()
                .Property(n => n.Channel)
                .HasMaxLength(30);
            mb.Entity<NotificationMessage>()
                .Property(n => n.Status)
                .HasMaxLength(50);
            mb.Entity<NotificationMessage>()
                .HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
            mb.Entity<NotificationMessage>()
                .HasIndex(n => new { n.Status, n.Channel, n.NextRetryAt });
            mb.Entity<ProctoringLog>()
                .HasOne(p => p.Attempt).WithMany(a => a.ProctoringLogs).HasForeignKey(p => p.AttemptId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<AssessmentReport>()
                .HasOne(r => r.Exam).WithMany(e => e.Reports).HasForeignKey(r => r.ExamId).OnDelete(DeleteBehavior.Cascade);

            // Recruitment workflow entities
            // NOTE: The unique filtered index on CandidateId (WHERE CandidateId <> '')
            // is created manually at startup in Program.cs using a guarded CREATE INDEX
            // statement for older databases that may not have the column yet.

            mb.Entity<AssessmentInvitation>()
                .HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<AssessmentInvitation>()
                .HasOne(i => i.Exam).WithMany().HasForeignKey(i => i.ExamId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<AssessmentInvitation>()
                .HasIndex(i => i.Token).IsUnique();
            mb.Entity<AssessmentInvitation>()
                .Property(i => i.Status)
                .HasMaxLength(50);
            mb.Entity<AssessmentInvitation>()
                .HasIndex(i => new { i.UserId, i.Status, i.ExpiresAt });
            mb.Entity<AssessmentInvitation>()
                .HasIndex(i => new { i.ExamId, i.Status });

            mb.Entity<InterviewRecord>()
                .HasOne(ir => ir.User).WithMany().HasForeignKey(ir => ir.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<InterviewRecord>()
                .Property(ir => ir.Status)
                .HasMaxLength(50);
            mb.Entity<InterviewRecord>()
                .HasIndex(ir => new { ir.UserId, ir.Status, ir.ScheduledAt });

            mb.Entity<CandidateDocument>()
                .HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<CandidateDocument>()
                .Property(d => d.DocumentType)
                .HasMaxLength(100);
            mb.Entity<CandidateDocument>()
                .HasIndex(d => new { d.UserId, d.DocumentType });

            mb.Entity<OfferLetter>()
                .Property(o => o.CtcLpa).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.BaseSalaryLpa).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.VariablePayLpa).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.BonusLpa).HasPrecision(10, 2);
            // Annexure III monthly salary breakup — added after the block above
            // and missed the same precision treatment, which EF Core flags as a
            // "no store type specified" warning on every startup (values would
            // silently truncate under SQL Server's default decimal(18,2) precision
            // if they ever exceeded it). Matches the (10, 2) used for the LPA
            // fields above since these are also currency amounts, just monthly
            // instead of annual.
            mb.Entity<OfferLetter>()
                .Property(o => o.BasicMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.HraMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.SpecialAllowanceMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.EmployeeEpfMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.EmployeeEsiMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.TrainingChargesMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.ProfessionalTaxMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.EmployerEpfMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.EmployerEsiMonthly).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.PerformanceIncentiveAnnual).HasPrecision(10, 2);
            mb.Entity<OfferLetter>()
                .Property(o => o.Status)
                .HasMaxLength(50);
            mb.Entity<OfferLetter>()
                .HasIndex(o => new { o.UserId, o.Status, o.IssuedAt });
            mb.Entity<CandidateJobPreference>()
                .Property(p => p.ExpectedSalary).HasPrecision(12, 2);
            mb.Entity<OfferLetter>()
                .HasOne(o => o.User).WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
            mb.Entity<OfferLetter>()
                .HasIndex(o => o.AcceptanceToken).IsUnique();
            mb.Entity<OfferHistory>()
                .HasOne(h => h.OfferLetter).WithMany(o => o.History).HasForeignKey(h => h.OfferLetterId).OnDelete(DeleteBehavior.Cascade);
        }

        public static string HashPassword(string password)
        {
            return new PasswordHasher<User>().HashPassword(new User(), password);
        }

        public static bool VerifyPassword(User user, string password)
        {
            var result = new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, password);
            return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded;
        }
    }

    public static class DbSeeder
    {
        /// <summary>
        /// Seeds demo admin/candidate accounts and sample exams.
        /// Only runs when <paramref name="seedDemoAccounts"/> is true — callers should pass
        /// true only for Development environments. The shared password for the seeded
        /// accounts is never hardcoded: it must be supplied via <paramref name="demoAccountsPassword"/>,
        /// sourced from .NET User Secrets (DemoAccounts:Password) or the
        /// DemoAccounts__Password environment variable. See <see cref="RequireDemoPassword"/>.
        /// </summary>
        /// <summary>
        /// Guards against seeding demo accounts without an explicitly configured password.
        /// Called only on the path where <c>seedDemoAccounts</c> is true and the database is
        /// empty (i.e. seeding is actually about to happen) — fails fast with a clear,
        /// actionable error rather than silently seeding a guessable/hardcoded credential.
        /// </summary>
        public static void RequireDemoPassword(string? demoAccountsPassword)
        {
            if (string.IsNullOrWhiteSpace(demoAccountsPassword))
            {
                throw new InvalidOperationException(
                    "Demo-account seeding is enabled (Development) but no DemoAccounts:Password " +
                    "was configured. Set it with: dotnet user-secrets set \"DemoAccounts:Password\" " +
                    "\"<password>\" (or the DemoAccounts__Password environment variable) before running the app.");
            }
        }

        public static void Seed(AppDbContext db, bool seedDemoAccounts, string? demoAccountsPassword = null)
        {
            if (db.Users.Any()) return;
            if (!seedDemoAccounts)
            {
                SeedExamsOnly(db);
                return;
            }

            RequireDemoPassword(demoAccountsPassword);

            // Seed admin and student
            var admin = new User
            {
                FirstName = "Admin",
                LastName = "User",
                FullName = "Admin User",
                Username = "admin",
                Email = "admin@exam.com",
                MobileNumber = "9999999999",
                IsEmailVerified = true,
                IsMobileVerified = true,
                PasswordHash = AppDbContext.HashPassword(demoAccountsPassword!),
                Role = PortalRoles.Admin,
                // Admins are not candidates; sentinel value keeps CandidateId non-null
                // and distinct from every real candidate ID (which use the VWTyyyy prefix).
                CandidateId = "ADMIN-001",
                RecruitmentStage = "N/A"
            };
            var recruiter = new User
            {
                FirstName = "Riya",
                LastName = "Recruiter",
                FullName = "Riya Recruiter",
                Username = "recruiter",
                Email = "recruiter@exam.com",
                MobileNumber = "7777777777",
                IsEmailVerified = true,
                IsMobileVerified = true,
                PasswordHash = AppDbContext.HashPassword(demoAccountsPassword!),
                Role = PortalRoles.Recruiter,
                CandidateId = "STAFF-REC-001",
                RecruitmentStage = "N/A"
            };
            var hr = new User
            {
                FirstName = "Harish",
                LastName = "HR",
                FullName = "Harish HR",
                Username = "hr",
                Email = "hr@exam.com",
                MobileNumber = "6666666666",
                IsEmailVerified = true,
                IsMobileVerified = true,
                PasswordHash = AppDbContext.HashPassword(demoAccountsPassword!),
                Role = PortalRoles.Hr,
                CandidateId = "STAFF-HR-001",
                RecruitmentStage = "N/A"
            };
            var student = new User
            {
                FirstName = "John",
                LastName = "Doe",
                FullName = "John Doe",
                Username = "student",
                Email = "student@exam.com",
                MobileNumber = "8888888888",
                DateOfBirth = new DateTime(2001, 1, 15),
                Gender = "Male",
                CurrentLocation = "Bengaluru",
                PermanentAddress = "Bengaluru, Karnataka",
                CollegeUniversityName = "Demo University",
                Degree = "B.Tech",
                BranchSpecialization = "Computer Science",
                GraduationYear = DateTime.Today.Year,
                CgpaOrPercentage = 8.2m,
                TenthPercentage = 88,
                TwelfthPercentage = 86,
                BacklogInformation = "No active backlogs",
                Skills = "C#, ASP.NET Core, SQL",
                Certifications = "Microsoft Learn ASP.NET Core Fundamentals",
                Projects = "Online Exam Portal",
                Internships = "Software Development Intern",
                // WorkExperience removed — fresher candidates have no work experience field
                LinkedInUrl = "https://www.linkedin.com/",
                GitHubUrl = "https://github.com/",
                PortfolioWebsite = "https://example.com",
                PreferredJobLocations = "Bengaluru, Hyderabad, Pune",
                CurrentAcademicStatus = "Final Year",
                GapInEducation = "None",
                IsEmailVerified = true,
                IsMobileVerified = true,
                PasswordHash = AppDbContext.HashPassword(demoAccountsPassword!),
                Role = PortalRoles.Candidate,
                CandidateId = "VWT202600001",
                RecruitmentStage = "Registered",
                CandidateProfile = new CandidateProfile
                {
                    IsProfileComplete = true,
                    IsResumeValidated = true,
                    VerificationStatus = "Verified",
                    CompletedAt = DateTime.UtcNow
                },
                EducationRecords = new List<CandidateEducationRecord>
                {
                    new() { Level = EducationLevels.Postgraduate, Status = EducationStatuses.NotApplicable },
                    new()
                    {
                        Level = EducationLevels.Undergraduate, DegreeOrCourse = "B.Tech",
                        InstituteName = "Demo University", BoardOrUniversity = "Demo University",
                        StreamBranch = "Computer Science", YearOfPassing = DateTime.Today.Year - 1,
                        MarksValue = 8.2m, MarksType = "CGPA", Status = EducationStatuses.Completed
                    },
                    new()
                    {
                        Level = EducationLevels.Intermediate, DegreeOrCourse = "Intermediate / 12th",
                        InstituteName = "Demo Junior College", BoardOrUniversity = "State Board",
                        StreamBranch = "Science (MPC)", YearOfPassing = 2019,
                        MarksValue = 86, MarksType = "Percentage", Status = EducationStatuses.Completed
                    },
                    new()
                    {
                        Level = EducationLevels.Secondary, DegreeOrCourse = "10th / SSC",
                        InstituteName = "Demo High School", BoardOrUniversity = "State Board",
                        YearOfPassing = 2017, MarksValue = 88, MarksType = "Percentage",
                        Status = EducationStatuses.Completed
                    }
                }
            };
            db.Users.AddRange(admin, recruiter, hr, student);
            db.SaveChanges();

            SeedExams(db);
        }

        /// <summary>
        /// Production-safe path: seeds only the sample exams (no accounts with known
        /// credentials). Real admin/candidate accounts must be created through normal
        /// registration / a dedicated admin-provisioning process.
        /// </summary>
        private static void SeedExamsOnly(AppDbContext db) => SeedExams(db);

        private static void SeedExams(AppDbContext db)
        {
            if (db.Exams.Any()) return;

            // Seed exams
            var exam1 = new Exam
            {
                Title = "Software Engineer Eligibility Assessment",
                Description = "Eligibility assessment for registered software professional candidates covering C#, web, SQL, aptitude, reasoning, and coding readiness.",
                Instructions = "Use full-screen mode, keep a stable internet connection, do not switch tabs, and submit before the timer expires. Coding questions are evaluated automatically in this prototype.",
                Subject = "Software Engineering",
                DurationMinutes = 30,
                PassingMarks = 6,
                CutoffScore = 6,
                TotalMarks = 10,
                TotalQuestions = 10,
                Status = "Active",
                StartDateTime = DateTime.UtcNow.AddDays(-1),
                EndDateTime = DateTime.UtcNow.AddDays(14),
                RequiredEducation = "Computer",
                RequiredSkills = "C#, SQL, ASP.NET",
                EligibleLocations = "Bengaluru, Hyderabad, Pune, Remote",
                ExperienceLevel = "Fresher",
                Questions = new List<Question>
                {
                    new() { Text = "Which keyword is used to declare an implicitly typed variable in C#?", Category = "Technical MCQs", QuestionType = "Single Choice MCQ", OptionA = "var", OptionB = "let", OptionC = "dim", OptionD = "define", CorrectAnswer = "A", Marks = 1 },
                    new() { Text = "What does OOP stand for?", Category = "Technical MCQs", QuestionType = "Single Choice MCQ", OptionA = "Object Oriented Protocol", OptionB = "Object Oriented Programming", OptionC = "Object Only Processing", OptionD = "None", CorrectAnswer = "B", Marks = 1 },
                    new() { Text = "Which of the following is a value type in C#?", Category = "Technical MCQs", QuestionType = "Single Choice MCQ", OptionA = "string", OptionB = "object", OptionC = "int", OptionD = "array", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "If 5 developers complete 5 modules in 5 days, how many days do 10 developers need for 10 modules at the same rate?", Category = "Aptitude Questions", QuestionType = "Single Choice MCQ", OptionA = "2.5", OptionB = "5", OptionC = "10", OptionD = "20", CorrectAnswer = "B", Marks = 1 },
                    new() { Text = "Find the next term: 2, 6, 12, 20, 30, ?", Category = "Logical Reasoning", QuestionType = "Single Choice MCQ", OptionA = "36", OptionB = "40", OptionC = "42", OptionD = "44", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "Choose the grammatically correct sentence.", Category = "Verbal Ability", QuestionType = "Single Choice MCQ", OptionA = "She don't write tests", OptionB = "She doesn't writes tests", OptionC = "She doesn't write tests", OptionD = "She not write tests", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "What is the correct syntax to create a generic list in C#?", Category = "Programming Questions", QuestionType = "Single Choice MCQ", OptionA = "List<int> l = new List<int>()", OptionB = "int[] l = new List()", OptionC = "ArrayList l = new List()", OptionD = "None", CorrectAnswer = "A", Marks = 1 },
                    new() { Text = "A production API has high latency only during peak traffic. What should you inspect first?", Category = "Scenario-Based Questions", QuestionType = "Single Choice MCQ", OptionA = "Load metrics and slow traces", OptionB = "Button colors", OptionC = "Logo file size only", OptionD = "Timezone settings only", CorrectAnswer = "A", Marks = 1 },
                    new() { Text = "Which SQL keyword removes duplicate rows in a result?", Category = "Domain-Specific Questions", QuestionType = "Single Choice MCQ", OptionA = "UNIQUE", OptionB = "DISTINCT", OptionC = "SINGLE", OptionD = "FILTER", CorrectAnswer = "B", Marks = 1 },
                    new() { Text = "Return true if a string is a palindrome. Use the integrated editor.", Category = "Coding Challenges", QuestionType = "Coding Questions", OptionA = "Manual review", OptionB = "Compile only", OptionC = "Automated tests", OptionD = "Skip", CorrectAnswer = "C", Marks = 1, CodingQuestion = new CodingQuestion { SupportedLanguages = "C#, JavaScript, Python", StarterCode = "// Write your solution here", SampleTestCases = "racecar => true; exam => false", HiddenTestCases = "level => true; portal => false" } },
                }
            };

            var exam2 = new Exam
            {
                Title = "Frontend Developer Eligibility Assessment",
                Description = "Eligibility assessment covering HTML, CSS, JavaScript, browser behavior, and frontend debugging.",
                Instructions = "Attempt all sections. Browser compatibility and tab switching are monitored.",
                Subject = "Web Development",
                DurationMinutes = 20,
                PassingMarks = 4,
                CutoffScore = 4,
                TotalMarks = 6,
                TotalQuestions = 6,
                Status = "Scheduled",
                StartDateTime = DateTime.UtcNow,
                EndDateTime = DateTime.UtcNow.AddDays(7),
                RequiredSkills = "HTML, CSS, JavaScript",
                EligibleLocations = "Remote, Bengaluru",
                Questions = new List<Question>
                {
                    new() { Text = "What does HTML stand for?", OptionA = "Hyper Text Markup Language", OptionB = "High Text Machine Language", OptionC = "Hyper Transfer Markup Language", OptionD = "None", CorrectAnswer = "A", Marks = 1 },
                    new() { Text = "Which CSS property controls text size?", OptionA = "text-size", OptionB = "font-size", OptionC = "text-style", OptionD = "size", CorrectAnswer = "B", Marks = 1 },
                    new() { Text = "Which HTTP method is used to send data to a server?", OptionA = "GET", OptionB = "PUT", OptionC = "POST", OptionD = "FETCH", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "What does 'DOM' stand for in web development?", OptionA = "Document Object Model", OptionB = "Data Object Method", OptionC = "Document Operation Mode", OptionD = "Display Object Model", CorrectAnswer = "A", Marks = 1 },
                    new() { Text = "Which JavaScript function parses a JSON string?", OptionA = "JSON.parse()", OptionB = "JSON.stringify()", OptionC = "parseJSON()", OptionD = "JSON.convert()", CorrectAnswer = "A", Marks = 1 },
                    new() { Text = "What is the correct HTML element for the largest heading?", OptionA = "<heading>", OptionB = "<head>", OptionC = "<h6>", OptionD = "<h1>", CorrectAnswer = "D", Marks = 1 },
                }
            };

            var exam3 = new Exam
            {
                Title = "Database Engineer Eligibility Assessment",
                Description = "Eligibility assessment for SQL query writing, database design, and data reasoning.",
                Instructions = "Negative marking may apply to incorrect objective answers.",
                Subject = "Database",
                DurationMinutes = 25,
                PassingMarks = 5,
                CutoffScore = 5,
                TotalMarks = 8,
                TotalQuestions = 8,
                Status = "Active",
                StartDateTime = DateTime.UtcNow.AddDays(-2),
                EndDateTime = DateTime.UtcNow.AddDays(10),
                RequiredSkills = "SQL, Database",
                EligibleLocations = "Hyderabad, Pune, Remote",
                NegativeMarks = .25m,
                Questions = new List<Question>
                {
                    new() { Text = "Which SQL statement is used to retrieve data?", OptionA = "GET", OptionB = "FETCH", OptionC = "SELECT", OptionD = "READ", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "What does PRIMARY KEY enforce?", OptionA = "Uniqueness only", OptionB = "Not null only", OptionC = "Both uniqueness and not null", OptionD = "None", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "Which JOIN returns only matching rows from both tables?", OptionA = "LEFT JOIN", OptionB = "RIGHT JOIN", OptionC = "FULL JOIN", OptionD = "INNER JOIN", CorrectAnswer = "D", Marks = 1 },
                    new() { Text = "Which command removes a table from the database?", OptionA = "DELETE TABLE", OptionB = "DROP TABLE", OptionC = "REMOVE TABLE", OptionD = "TRUNCATE", CorrectAnswer = "B", Marks = 1 },
                    new() { Text = "Which SQL clause filters groups?", OptionA = "WHERE", OptionB = "ORDER BY", OptionC = "HAVING", OptionD = "GROUP BY", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "What is a FOREIGN KEY?", OptionA = "Unique identifier", OptionB = "A key from another table", OptionC = "An encrypted key", OptionD = "A composite key", CorrectAnswer = "B", Marks = 1 },
                    new() { Text = "Which aggregate function counts rows?", OptionA = "SUM()", OptionB = "TOTAL()", OptionC = "COUNT()", OptionD = "NUMBER()", CorrectAnswer = "C", Marks = 1 },
                    new() { Text = "Which SQL keyword removes duplicate rows in result?", OptionA = "UNIQUE", OptionB = "DISTINCT", OptionC = "SINGLE", OptionD = "FILTER", CorrectAnswer = "B", Marks = 1 },
                }
            };

            db.Exams.AddRange(exam1, exam2, exam3);
            db.SaveChanges();
        }
    }
}
