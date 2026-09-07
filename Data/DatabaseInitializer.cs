using Microsoft.EntityFrameworkCore;

namespace ExamPortal.Data
{
    /// <summary>
    /// Brings the database schema fully up to date, regardless of its starting state
    /// (brand-new empty DB, or an existing one from any previous version of the app).
    /// Called once at startup from Program.cs.
    ///
    /// Problem this solves:
    ///   SQL error 2714 "There is already an object named X" happens when:
    ///   • The DB exists with tables (created by EnsureCreated or SSMS or a previous
    ///     run of the app) but has NO __EFMigrationsHistory entries, AND
    ///   • dotnet ef migrations add / Update-Database is run — EF sees no history,
    ///     assumes the DB is blank, tries CREATE TABLE on existing tables → crash.
    ///
    /// Solution — never use Migrate() at runtime. Use this instead:
    ///
    ///   Step 1. EnsureCreated()
    ///     • Idempotent: skips ALL DDL if every table already exists.
    ///     • Creates everything from scratch if the DB is brand-new.
    ///     • Never drops or truncates data.
    ///
    ///   Step 2+. Guarded ALTER TABLE / CREATE INDEX / CREATE TABLE for everything
    ///     added after initial creation — each one runs only when sys.columns /
    ///     sys.indexes / OBJECT_ID confirms the object is actually absent, so this
    ///     whole method is safe to call on every single app startup.
    /// </summary>
    public static class DatabaseInitializer
    {
        /// <param name="db">An AppDbContext resolved from a DI scope.</param>
        /// <param name="seedDemoAccounts">Whether to seed the built-in demo Admin/
        /// Recruiter/HR/Candidate accounts — pass true only in Development.</param>
        /// <param name="demoAccountsPassword">Password for the seeded demo accounts, sourced
        /// from .NET User Secrets or an environment variable — never hardcoded. Required
        /// (and validated) only when <paramref name="seedDemoAccounts"/> is true and the
        /// database is empty; ignored otherwise.</param>
        public static void EnsureSchema(AppDbContext db, bool seedDemoAccounts, string? demoAccountsPassword = null)
        {
    // ── Step 1: create DB + all tables if they don't exist ──────────────────
    db.Database.EnsureCreated();

    // ── Step 2: add CandidateId column if missing ───────────────────────────
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'CandidateId'
        )
        BEGIN
            ALTER TABLE [dbo].[Users]
                ADD [CandidateId] nvarchar(32) NOT NULL
                CONSTRAINT [DF_Users_CandidateId] DEFAULT (N'');
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]')
              AND name = N'CandidateId'
              AND max_length = -1
        )
        BEGIN
            ALTER TABLE [dbo].[Users] ALTER COLUMN [CandidateId] nvarchar(32) NOT NULL;
        END
    ");

    // ── Step 3: add RecruitmentStage column if missing ──────────────────────
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'RecruitmentStage'
        )
        BEGIN
            ALTER TABLE [dbo].[Users]
                ADD [RecruitmentStage] nvarchar(max) NOT NULL
                CONSTRAINT [DF_Users_RecruitmentStage] DEFAULT (N'Registered');
        END
    ");

    // ── Step 4: create filtered unique index on CandidateId if missing ──────
    // Filtered on CandidateId <> '' so admin/system users with empty CandidateId
    // are excluded from the uniqueness constraint.
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]')
              AND name = N'IX_Users_CandidateId'
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_Users_CandidateId]
                ON [dbo].[Users] ([CandidateId])
                WHERE [CandidateId] <> N'';
        END
    ");

    // ── Google Sign-In columns (candidates only — see AccountController.ExternalAuth.cs) ──
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'AuthProvider'
        )
        BEGIN
            ALTER TABLE [dbo].[Users]
                ADD [AuthProvider] nvarchar(20) NOT NULL
                CONSTRAINT [DF_Users_AuthProvider] DEFAULT (N'Local');
        END

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'GoogleId'
        )
        BEGIN
            ALTER TABLE [dbo].[Users]
                ADD [GoogleId] nvarchar(100) NOT NULL
                CONSTRAINT [DF_Users_GoogleId] DEFAULT (N'');
        END

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'LinkedInId'
        )
        BEGIN
            ALTER TABLE [dbo].[Users]
                ADD [LinkedInId] nvarchar(100) NOT NULL
                CONSTRAINT [DF_Users_LinkedInId] DEFAULT (N'');
        END
    ");

    // Filtered (WHERE GoogleId <> '') so the many local-auth accounts with an empty
    // GoogleId don't collide on uniqueness — same pattern as IX_Users_CandidateId above.
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]')
              AND name = N'IX_Users_GoogleId'
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_Users_GoogleId]
                ON [dbo].[Users] ([GoogleId])
                WHERE [GoogleId] <> N'';
        END

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]')
              AND name = N'IX_Users_LinkedInId'
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_Users_LinkedInId]
                ON [dbo].[Users] ([LinkedInId])
                WHERE [LinkedInId] <> N'';
        END
    ");

    // ── Step 4b: add PasswordResetTokenHash / PasswordResetTokenExpiresAt columns ──
    // Added to support the "Forgot password" flow. Only the SHA-256 hash of the
    // reset token is stored, never the raw token itself.
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'PasswordResetTokenHash'
        )
        BEGIN
            ALTER TABLE [dbo].[Users]
                ADD [PasswordResetTokenHash] nvarchar(max) NOT NULL
                CONSTRAINT [DF_Users_PasswordResetTokenHash] DEFAULT (N'');
        END

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'PasswordResetTokenExpiresAt'
        )
        BEGIN
            ALTER TABLE [dbo].[Users]
                ADD [PasswordResetTokenExpiresAt] datetime2 NULL;
        END
    ");

    // ── Step 4c: drop legacy MobileOtp column ────────────────────────────────
    // Older versions of the User model stored the raw OTP code in a MobileOtp
    // column; the code now only tracks MobileOtpExpiresAt (see EligibilityService/
    // CandidateWorkflowService), so MobileOtp has no mapping in the EF model.
    // On databases created by that older version, the column is still present as
    // NOT NULL with no default, which makes every new-user INSERT fail. Drop it
    // if it's still there — including its default constraint, if any — so
    // registration works on both old and freshly-created databases.
    db.Database.ExecuteSqlRaw(@"
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = N'MobileOtp'
        )
        BEGIN
            DECLARE @constraintName nvarchar(200);
            SELECT @constraintName = dc.name
            FROM sys.default_constraints dc
            JOIN sys.columns c
                ON c.default_object_id = dc.object_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[Users]')
              AND c.name = N'MobileOtp';

            IF @constraintName IS NOT NULL
            BEGIN
                EXEC('ALTER TABLE [dbo].[Users] DROP CONSTRAINT [' + @constraintName + ']');
            END

            ALTER TABLE [dbo].[Users] DROP COLUMN [MobileOtp];
        END
    ");

    // ── Step 5: add new InterviewRecord columns if missing ───────────────────
    // Columns added in this update: TimeZone, DurationMinutes, Format,
    // MeetingLink, MeetingId, InterviewerNames, CandidateActionItems,
    // PanelMembers, Feedback, Rating, Remarks, CandidateStatusAfterInterview,
    // UpdatedBy, CancelledAt.
    // Each block is idempotent — safe to run on every startup.
    //
    // EF1002 (SQL injection via interpolated ExecuteSqlRaw) is suppressed for this
    // section on purpose, not overlooked: every interpolated value from here through
    // the end of this method — table names, column names, index names, DDL type/
    // constraint fragments — comes exclusively from hardcoded `new[] { (...), ... }`
    // literal arrays declared right next to each loop, never from a request, a
    // database query result, or any other externally-influenced source. Switching
    // these to the suggested ExecuteSql alternative would also not work correctly:
    // SQL parameters can only stand in for data *values* (a WHERE clause comparison,
    // an INSERT value, etc.), not identifiers or type definitions — `ALTER TABLE x
    // ADD @p0 @p1` isn't valid syntax, so a column name or "nvarchar(max) NOT NULL
    // DEFAULT ('')" fragment can't be parameterized the way a value could.
#pragma warning disable EF1002
    var interviewColumns = new[]
    {
        ("TimeZone",             "nvarchar(100) NOT NULL DEFAULT (N'UTC')"),
        ("DurationMinutes",      "int NOT NULL DEFAULT (60)"),
        ("Format",               "nvarchar(50)  NOT NULL DEFAULT (N'Online')"),
        ("MeetingLink",          "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("MeetingId",            "nvarchar(200) NOT NULL DEFAULT (N'')"),
        ("InterviewerNames",     "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("CandidateActionItems", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("PanelMembers",         "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("Feedback",             "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("Rating",               "int NULL"),
        ("Remarks",              "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("CandidateStatusAfterInterview", "nvarchar(100) NOT NULL DEFAULT (N'')"),
        ("UpdatedBy",            "nvarchar(150) NOT NULL DEFAULT (N'')"),
        ("CancelledAt",          "datetime2 NULL")
    };

    foreach (var (col, colDef) in interviewColumns)
    {
        var addInterviewColumnSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[InterviewRecords]') AND name = N'{col}'
            )
            BEGIN
                ALTER TABLE [dbo].[InterviewRecords]
                    ADD [{col}] {colDef};
            END
        ";
        db.Database.ExecuteSqlRaw(addInterviewColumnSql);
    }

    // ── Step 6 (fresher update): add new fresher-only columns to CandidateProfessionalProfiles ──
    // Adds Projects, Internships, LinkedInUrl, GitHubUrl, PortfolioUrl;
    // removes nothing (old Experience/CurrentCompany columns are left for existing
    // rows but are no longer used by the application — safe, additive-only migration).
    var fresherProfessionalColumns = new[]
    {
        ("Projects",     "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("Internships",  "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("LinkedInUrl",  "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("GitHubUrl",    "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("PortfolioUrl", "nvarchar(max) NOT NULL DEFAULT (N'')")
    };
    foreach (var (col, colDef) in fresherProfessionalColumns)
    {
        db.Database.ExecuteSqlRaw($@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[CandidateProfessionalProfiles]')
                  AND name = N'{col}'
            )
            BEGIN
                ALTER TABLE [dbo].[CandidateProfessionalProfiles]
                    ADD [{col}] {colDef};
            END
        ");
    }


    // Demo admin/candidate accounts are only seeded in Development, and only when a
    // password is explicitly configured (see DbSeeder.RequireDemoPassword). Other
    // environments get the sample exams only; real accounts must be created via
    // registration or a separate admin-provisioning step.
    var examPlatformColumns = new[]
    {
        ("Exams", "TemplateName", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("Exams", "IsTemplate", "bit NOT NULL DEFAULT (0)"),
        ("Exams", "RandomQuestionCount", "int NOT NULL DEFAULT (0)"),
        ("Exams", "UseQuestionBank", "bit NOT NULL DEFAULT (1)"),
        ("Questions", "Difficulty", "nvarchar(max) NOT NULL DEFAULT (N'Medium')"),
        ("Questions", "Tags", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("Questions", "IsQuestionBankItem", "bit NOT NULL DEFAULT (1)"),
        ("Questions", "DisplayOrder", "int NOT NULL DEFAULT (0)"),
        ("ExamAttempts", "QuestionOrder", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("ExamAttempts", "ProctoringSessionId", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("ExamAttempts", "Rank", "int NOT NULL DEFAULT (0)"),
        ("ExamAttempts", "PercentageScore", "decimal(6,2) NOT NULL DEFAULT (0)"),
        ("ExamAttempts", "LastHeartbeatAt", "datetime2 NULL")
    };

    foreach (var (table, col, colDef) in examPlatformColumns)
    {
        var addAssessmentColumnSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[{table}]') AND name = N'{col}'
            )
            BEGIN
                ALTER TABLE [dbo].[{table}]
                    ADD [{col}] {colDef};
            END
        ";
        db.Database.ExecuteSqlRaw(addAssessmentColumnSql);
    }

    var invitationWorkflowColumns = new[]
    {
        ("OneTimeLoginToken", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("AssessmentDate", "datetime2 NULL"),
        ("DurationMinutes", "int NOT NULL DEFAULT (0)"),
        ("PassingMarks", "int NOT NULL DEFAULT (0)"),
        ("TokenUsedAt", "datetime2 NULL")
    };

    foreach (var (col, colDef) in invitationWorkflowColumns)
    {
        var addInvitationColumnSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[AssessmentInvitations]') AND name = N'{col}'
            )
            BEGIN
                ALTER TABLE [dbo].[AssessmentInvitations]
                    ADD [{col}] {colDef};
            END
        ";
        db.Database.ExecuteSqlRaw(addInvitationColumnSql);
    }

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidatePersonalDetails]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidatePersonalDetails] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidatePersonalDetails] PRIMARY KEY,
                [UserId] int NOT NULL,
                [DateOfBirth] datetime2 NULL,
                [Gender] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidatePersonalDetails_Gender] DEFAULT (N''),
                [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidatePersonalDetails_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidatePersonalDetails_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateAddresses]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidateAddresses] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidateAddresses] PRIMARY KEY,
                [UserId] int NOT NULL,
                [AddressLine] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateAddresses_AddressLine] DEFAULT (N''),
                [State] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateAddresses_State] DEFAULT (N''),
                [City] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateAddresses_City] DEFAULT (N''),
                [Country] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateAddresses_Country] DEFAULT (N''),
                [Pincode] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateAddresses_Pincode] DEFAULT (N''),
                [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidateAddresses_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidateAddresses_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    // Replaces the old 1:1 CandidateEducations table (which the registration wizard never
    // actually wrote to) with a 1:many table keyed by education Level, so a candidate can
    // have up to 4 rows: Postgraduate, Undergraduate, Intermediate, Secondary.
    // If an old CandidateEducations table exists from a prior run, it's left in place
    // (harmless, unused) rather than dropped, since this block never deletes data.
    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateEducationRecords]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidateEducationRecords] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidateEducationRecords] PRIMARY KEY,
                [UserId] int NOT NULL,
                [Level] nvarchar(20) NOT NULL CONSTRAINT [DF_CandidateEducationRecords_Level] DEFAULT (N''),
                [DegreeOrCourse] nvarchar(100) NOT NULL CONSTRAINT [DF_CandidateEducationRecords_DegreeOrCourse] DEFAULT (N''),
                [InstituteName] nvarchar(150) NOT NULL CONSTRAINT [DF_CandidateEducationRecords_InstituteName] DEFAULT (N''),
                [BoardOrUniversity] nvarchar(150) NOT NULL CONSTRAINT [DF_CandidateEducationRecords_BoardOrUniversity] DEFAULT (N''),
                [StreamBranch] nvarchar(100) NOT NULL CONSTRAINT [DF_CandidateEducationRecords_StreamBranch] DEFAULT (N''),
                [YearOfPassing] int NULL,
                [MarksValue] decimal(5,2) NULL,
                [MarksType] nvarchar(20) NOT NULL CONSTRAINT [DF_CandidateEducationRecords_MarksType] DEFAULT (N'Percentage'),
                [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_CandidateEducationRecords_Status] DEFAULT (N''),
                [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidateEducationRecords_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidateEducationRecords_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    // One row per level per candidate — composite unique index, not the single-column
    // per-UserId index used by the other (genuinely 1:1) registration tables below.
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[CandidateEducationRecords]')
              AND name = N'IX_CandidateEducationRecords_UserId_Level'
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_CandidateEducationRecords_UserId_Level]
                ON [dbo].[CandidateEducationRecords] ([UserId], [Level]);
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateProfessionalProfiles]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidateProfessionalProfiles] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidateProfessionalProfiles] PRIMARY KEY,
                [UserId] int NOT NULL,
                [Experience] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateProfessionalProfiles_Experience] DEFAULT (N''),
                [CurrentCompany] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateProfessionalProfiles_CurrentCompany] DEFAULT (N''),
                [Skills] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateProfessionalProfiles_Skills] DEFAULT (N''),
                [Certifications] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateProfessionalProfiles_Certifications] DEFAULT (N''),
                [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidateProfessionalProfiles_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidateProfessionalProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateSocialProfiles]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidateSocialProfiles] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidateSocialProfiles] PRIMARY KEY,
                [UserId] int NOT NULL,
                [LinkedInUrl] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateSocialProfiles_LinkedInUrl] DEFAULT (N''),
                [GitHubUrl] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateSocialProfiles_GitHubUrl] DEFAULT (N''),
                [PortfolioUrl] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateSocialProfiles_PortfolioUrl] DEFAULT (N''),
                [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidateSocialProfiles_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidateSocialProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateJobPreferences]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidateJobPreferences] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidateJobPreferences] PRIMARY KEY,
                [UserId] int NOT NULL,
                [PreferredJobLocation] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateJobPreferences_PreferredJobLocation] DEFAULT (N''),
                [ExpectedSalary] decimal(12,2) NULL,
                [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidateJobPreferences_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidateJobPreferences_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateLanguages]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidateLanguages] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidateLanguages] PRIMARY KEY,
                [UserId] int NOT NULL,
                [Name] nvarchar(max) NOT NULL CONSTRAINT [DF_CandidateLanguages_Name] DEFAULT (N''),
                [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidateLanguages_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidateLanguages_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateProfileCompletions]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[CandidateProfileCompletions] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CandidateProfileCompletions] PRIMARY KEY,
                [UserId] int NOT NULL,
                [CompletionPercentage] int NOT NULL,
                [CompletedFields] int NOT NULL,
                [TotalFields] int NOT NULL,
                [CalculatedAt] datetime2 NOT NULL CONSTRAINT [DF_CandidateProfileCompletions_CalculatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_CandidateProfileCompletions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    // ── Backfill: 100% profile completion for already-registered candidates ──
    // CandidateWorkflowService.CompleteResumeAndSkills now forces ProfileCompletion to
    // 100% once registration finishes (nothing further is required of the candidate,
    // so optional gaps like a missing LinkedIn URL or photo shouldn't leave it looking
    // incomplete). That only takes effect for the next candidate who finishes
    // registration though — this backfills everyone who already has a CandidateId
    // (the marker IssueCandidateId sets at the end of registration) but whose stored
    // completion row is still below 100%. Runs after the table above is guaranteed to
    // exist; safe to run every startup — once a row is at 100 this matches zero rows.
    db.Database.ExecuteSqlRaw(@"
        UPDATE cpc
        SET cpc.CompletedFields = cpc.TotalFields,
            cpc.CompletionPercentage = 100
        FROM [dbo].[CandidateProfileCompletions] cpc
        JOIN [dbo].[Users] u ON u.Id = cpc.UserId
        WHERE u.CandidateId <> N''
          AND cpc.CompletionPercentage < 100;
    ");

    var oneToOneRegistrationTables = new[]
    {
        "CandidatePersonalDetails",
        "CandidateAddresses",
        "CandidateProfessionalProfiles",
        "CandidateSocialProfiles",
        "CandidateJobPreferences",
        "CandidateProfileCompletions"
    };

    foreach (var tableName in oneToOneRegistrationTables)
    {
        var createRegistrationIndexSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[{tableName}]')
                  AND name = N'IX_{tableName}_UserId'
            )
            BEGIN
                CREATE UNIQUE INDEX [IX_{tableName}_UserId] ON [dbo].[{tableName}] ([UserId]);
            END
        ";
        db.Database.ExecuteSqlRaw(createRegistrationIndexSql);
    }

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[SystemSettings]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[SystemSettings] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SystemSettings] PRIMARY KEY,
                [Key] nvarchar(120) NOT NULL,
                [Category] nvarchar(120) NOT NULL CONSTRAINT [DF_SystemSettings_Category] DEFAULT (N'General'),
                [Value] nvarchar(2000) NOT NULL CONSTRAINT [DF_SystemSettings_Value] DEFAULT (N''),
                [Description] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_Description] DEFAULT (N''),
                [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_SystemSettings_UpdatedAt] DEFAULT (SYSUTCDATETIME())
            );
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[SystemSettings]')
              AND name = N'IX_SystemSettings_Key'
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [dbo].[SystemSettings] ([Key]);
        END
    ");

    var offerColumns = new[]
    {
        ("BaseSalaryLpa", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("VariablePayLpa", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("BonusLpa", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("CompensationBreakup", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("DigitalSignature", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("SignedBy", "nvarchar(200) NOT NULL DEFAULT (N'')"),
        ("SignedAt", "datetime2 NULL"),
        ("HrApprovalStatus", "nvarchar(50) NOT NULL DEFAULT (N'Pending')"),
        ("HrApprovedBy", "nvarchar(200) NOT NULL DEFAULT (N'')"),
        ("HrApprovedAt", "datetime2 NULL"),
        ("HrApprovalRemarks", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("RejectionReason", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("RejectedAt", "datetime2 NULL"),
        ("OfferValidUntil", "datetime2 NULL"),
        ("BasicMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("HraMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("SpecialAllowanceMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("EmployeeEpfMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("EmployeeEsiMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("TrainingChargesMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("ProfessionalTaxMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("EmployerEpfMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("EmployerEsiMonthly", "decimal(10,2) NOT NULL DEFAULT (0)"),
        ("PerformanceIncentiveAnnual", "decimal(10,2) NOT NULL DEFAULT (0)")
    };

    foreach (var (col, colDef) in offerColumns)
    {
        var addOfferColumnSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[OfferLetters]') AND name = N'{col}'
            )
            BEGIN
                ALTER TABLE [dbo].[OfferLetters]
                    ADD [{col}] {colDef};
            END
        ";
        db.Database.ExecuteSqlRaw(addOfferColumnSql);
    }

    var documentColumns = new[]
    {
        ("VerifiedBy", "nvarchar(200) NOT NULL DEFAULT (N'')"),
        ("FileHash", "nvarchar(128) NOT NULL DEFAULT (N'')")
    };

    foreach (var (col, colDef) in documentColumns)
    {
        var addDocumentColumnSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[CandidateDocuments]') AND name = N'{col}'
            )
            BEGIN
                ALTER TABLE [dbo].[CandidateDocuments]
                    ADD [{col}] {colDef};
            END
        ";
        db.Database.ExecuteSqlRaw(addDocumentColumnSql);
    }

    // ── One active CandidateDocument row per (UserId, DocumentType) ─────────
    // The upload flow (HomeController.UploadDocument) always updates the existing
    // row in place rather than inserting a second one for the same document type,
    // so duplicates should never occur in normal use — but nothing at the database
    // level actually enforced that, leaving a real (if narrow) race-condition
    // window open. Before adding the constraint below, safely resolve any
    // duplicates that already exist: for each (UserId, DocumentType) group, the
    // highest-Id row (the most recent upload) is kept as the active document, and
    // any older duplicates are renamed with a distinguishing suffix rather than
    // deleted — their file and history stay fully intact and inspectable, just no
    // longer competing for the same DocumentType slot. Safe to run on every
    // startup: once no group has more than one row, this UPDATE matches zero rows.
    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[CandidateDocuments]', N'U') IS NOT NULL
        BEGIN
            ;WITH ranked AS (
                SELECT Id,
                       ROW_NUMBER() OVER (PARTITION BY UserId, DocumentType ORDER BY Id DESC) AS rn
                FROM [dbo].[CandidateDocuments]
            )
            UPDATE cd
                SET cd.DocumentType = cd.DocumentType + N' (duplicate #' + CAST(cd.Id AS nvarchar(20)) + N')'
                FROM [dbo].[CandidateDocuments] cd
                INNER JOIN ranked r ON r.Id = cd.Id
                WHERE r.rn > 1;
        END
    ");

    // The old plain (non-unique) index below only sped up lookups — it never
    // stopped a second row for the same (UserId, DocumentType) from being
    // inserted. Drop it in favor of the unique index right after, which covers
    // the same query pattern and actually enforces the one-active-document rule
    // at the database level (defense in depth alongside the application-level
    // checks in HomeController.UploadDocument).
    db.Database.ExecuteSqlRaw(@"
        IF EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[CandidateDocuments]')
              AND name = N'IX_CandidateDocuments_UserId_DocumentType'
              AND is_unique = 0
        )
        BEGIN
            DROP INDEX [IX_CandidateDocuments_UserId_DocumentType] ON [dbo].[CandidateDocuments];
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[CandidateDocuments]')
              AND name = N'IX_CandidateDocuments_UserId_DocumentType'
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_CandidateDocuments_UserId_DocumentType]
                ON [dbo].[CandidateDocuments] ([UserId], [DocumentType]);
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF OBJECT_ID(N'[dbo].[OfferHistories]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[OfferHistories] (
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OfferHistories] PRIMARY KEY,
                [OfferLetterId] int NOT NULL,
                [Action] nvarchar(max) NOT NULL CONSTRAINT [DF_OfferHistories_Action] DEFAULT (N''),
                [Actor] nvarchar(max) NOT NULL CONSTRAINT [DF_OfferHistories_Actor] DEFAULT (N''),
                [Remarks] nvarchar(max) NOT NULL CONSTRAINT [DF_OfferHistories_Remarks] DEFAULT (N''),
                [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_OfferHistories_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [FK_OfferHistories_OfferLetters_OfferLetterId] FOREIGN KEY ([OfferLetterId]) REFERENCES [dbo].[OfferLetters] ([Id]) ON DELETE CASCADE
            );
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[OfferHistories]')
              AND name = N'IX_OfferHistories_OfferLetterId'
        )
        BEGIN
            CREATE INDEX [IX_OfferHistories_OfferLetterId] ON [dbo].[OfferHistories] ([OfferLetterId]);
        END
    ");

    var notificationColumns = new[]
    {
        ("Channel", "nvarchar(30) NOT NULL DEFAULT (N'Email')"),
        ("IsRead", "bit NOT NULL DEFAULT (0)"),
        ("ReadAt", "datetime2 NULL"),
        ("RetryCount", "int NOT NULL DEFAULT (0)"),
        ("MaxRetries", "int NOT NULL DEFAULT (3)"),
        ("LastAttemptAt", "datetime2 NULL"),
        ("NextRetryAt", "datetime2 NULL"),
        ("SentAt", "datetime2 NULL"),
        ("ErrorMessage", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("ProviderMessageId", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("AttachmentFileName", "nvarchar(max) NOT NULL DEFAULT (N'')"),
        ("AttachmentBytes", "varbinary(max) NULL")
    };

    foreach (var (col, colDef) in notificationColumns)
    {
        var addNotificationColumnSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[Notifications]') AND name = N'{col}'
            )
            BEGIN
                ALTER TABLE [dbo].[Notifications]
                    ADD [{col}] {colDef};
            END
        ";
        db.Database.ExecuteSqlRaw(addNotificationColumnSql);
    }

    db.Database.ExecuteSqlRaw(@"
        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Notifications]')
              AND name = N'Status'
              AND max_length = -1
        )
        BEGIN
            ALTER TABLE [dbo].[Notifications] ALTER COLUMN [Status] nvarchar(50) NOT NULL;
        END

        IF EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[Notifications]')
              AND name = N'Channel'
              AND max_length = -1
        )
        BEGIN
            ALTER TABLE [dbo].[Notifications] ALTER COLUMN [Channel] nvarchar(30) NOT NULL;
        END
    ");

    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[Notifications]')
              AND name = N'IX_Notifications_Status_Channel_NextRetryAt'
        )
        BEGIN
            CREATE INDEX [IX_Notifications_Status_Channel_NextRetryAt]
                ON [dbo].[Notifications] ([Status], [Channel], [NextRetryAt]);
        END
    ");

    var boundedIndexedColumns = new[]
    {
        ("Users", "Role", "nvarchar(50) NOT NULL"),
        ("Users", "RecruitmentStage", "nvarchar(80) NOT NULL"),
        ("Exams", "Status", "nvarchar(50) NOT NULL"),
        ("ExamAttempts", "Status", "nvarchar(50) NOT NULL"),
        ("AssessmentInvitations", "Status", "nvarchar(50) NOT NULL"),
        ("InterviewRecords", "Status", "nvarchar(50) NOT NULL"),
        ("CandidateDocuments", "DocumentType", "nvarchar(100) NOT NULL"),
        ("OfferLetters", "Status", "nvarchar(50) NOT NULL"),
        ("Notifications", "Status", "nvarchar(50) NOT NULL"),
        ("Notifications", "Channel", "nvarchar(30) NOT NULL")
    };

    foreach (var (table, column, columnDefinition) in boundedIndexedColumns)
    {
        var boundColumnSql = $@"
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[dbo].[{table}]')
                  AND name = N'{column}'
                  AND max_length = -1
            )
            BEGIN
                ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] {columnDefinition};
            END
        ";
        db.Database.ExecuteSqlRaw(boundColumnSql);
    }

    var performanceIndexes = new[]
    {
        ("Users", "IX_Users_Role_RecruitmentStage", "[Role], [RecruitmentStage]"),
        ("Exams", "IX_Exams_IsActive_IsTemplate_Status", "[IsActive], [IsTemplate], [Status]"),
        ("ExamAttempts", "IX_ExamAttempts_UserId_StartedAt", "[UserId], [StartedAt]"),
        ("ExamAttempts", "IX_ExamAttempts_ExamId_SubmittedAt", "[ExamId], [SubmittedAt]"),
        ("ExamAttempts", "IX_ExamAttempts_Status", "[Status]"),
        ("AssessmentInvitations", "IX_AssessmentInvitations_UserId_Status_ExpiresAt", "[UserId], [Status], [ExpiresAt]"),
        ("AssessmentInvitations", "IX_AssessmentInvitations_ExamId_Status", "[ExamId], [Status]"),
        ("InterviewRecords", "IX_InterviewRecords_UserId_Status_ScheduledAt", "[UserId], [Status], [ScheduledAt]"),
        // CandidateDocuments' UserId+DocumentType index is created separately above
        // as a UNIQUE index (not just a performance one) — see the de-duplication
        // step right before it.
        ("OfferLetters", "IX_OfferLetters_UserId_Status_IssuedAt", "[UserId], [Status], [IssuedAt]"),
        ("Notifications", "IX_Notifications_UserId_IsRead_CreatedAt", "[UserId], [IsRead], [CreatedAt]")
    };

    foreach (var (table, index, columns) in performanceIndexes)
    {
        var createIndexSql = $@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[{table}]')
                  AND name = N'{index}'
            )
            BEGIN
                CREATE INDEX [{index}] ON [dbo].[{table}] ({columns});
            END
        ";
        db.Database.ExecuteSqlRaw(createIndexSql);
    }
#pragma warning restore EF1002

    DbSeeder.Seed(db, seedDemoAccounts: seedDemoAccounts, demoAccountsPassword: demoAccountsPassword);
        }
    }
}
