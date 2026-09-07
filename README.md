# ExamPortal — ASP.NET Core 8 MVC

A full-stack recruitment & assessment portal built with ASP.NET Core 8, Entity Framework Core, and SQL Server.

---

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 8.0+ |
| SQL Server | LocalDB (included with VS) or SQL Server Express |
| Visual Studio | 2022 (any edition) **or** VS Code + C# Dev Kit |

---

## Quick Start

### Option 1 — Visual Studio 2022

1. Open `ExamPortal.sln` or `ExamPortal.csproj` directly.
2. Set the demo-accounts password once (see [Login Credentials](#login-credentials-development) below) — required before the first run, or seeding fails with a config error.
3. Press **F5** — the app builds, creates the database, seeds demo data, and opens the browser.

### Option 2 — VS Code

```bash
cd ExamPortal
dotnet restore
dotnet run
```

Then open **http://localhost:5200** in your browser.

---

## First-Time Database Setup

No migration commands are needed. On first run, `Program.cs` automatically:

1. Creates the SQL Server `EXAMDB` database via `EnsureCreated()`.
2. Seeds an admin user, a candidate, and 3 sample assessments.

> ⚠️ Do **not** run `Add-Migration` or `Update-Database` — see `Migrations/README.md` for details.

---

## Login Credentials (Development)

Demo accounts (`admin`, `recruiter`, `hr`, candidate `VWT202600001`) are seeded only when
running in the `Development` environment, and only once a password is configured — the
app fails fast with a clear error if seeding is enabled but no password is set. No demo
password is ever hardcoded or committed.

Set the password once, locally, before running the app:

```bash
cd ExamPortal
dotnet user-secrets set "DemoAccounts:Password" "<choose-a-password>"
```

(Alternatively, set the `DemoAccounts__Password` environment variable.) All four demo
accounts share this password. Production never seeds demo accounts and never reads this
setting, regardless of whether it's configured.

---

## Email Notifications (Gmail SMTP)

Email is optional for local development. To enable it:

1. Add a Gmail App Password (Google Account → Security → App Passwords).
2. Update `appsettings.json` (or use `dotnet user-secrets` to avoid committing it):

```json
"Email": {
  "SenderEmail": "youraddress@gmail.com",
  "SenderName": "VISTAWAYS TECH",
  "Password": "your-16-char-app-password",
  "SmtpHost": "smtp.gmail.com",
  "SmtpPort": 587,
  "EnableSsl": true
}
```

If not configured, the notification dispatch loop logs an error and retries with backoff; the app continues normally.

---

## Testing

`ExamPortal.Tests` covers the pure-logic services and security-relevant middleware —
the pieces that are easy to get subtly wrong and don't need a live database to test
(candidate ID generation uses EF Core's in-memory provider instead of SQL Server, so
the whole suite runs with no external dependencies):

- `Services/` — `TextUtils.Clamp`, `SecureCodeGenerator` (OTP/token generation,
  SHA-256 hashing against known test vectors), `SchedulingHelper` (wall-clock →
  UTC time zone conversion, interview email formatting), `ClaimsPrincipalExtensions`,
  `CandidateIdService` (sequential ID generation, year-prefix handling).
- `Middleware/` — `IpAllowListMiddleware` (exact IP, CIDR ranges, the `localhost`
  shorthand, protected-path scoping) and `SecurityHeadersMiddleware` (response
  headers, via a minimal in-memory `TestServer`).

Run the whole suite:

```bash
dotnet test
```

This is a starting point, not full coverage — controllers, EF Core queries against a
real SQL Server, the exam-taking flow, and the recruitment pipeline state machine
aren't covered yet and would be the natural next additions.

---



```
ExamPortal/
├── Controllers/
│   ├── AdminController.cs      # Recruitment pipeline management (Admin/Recruiter/HR)
│   ├── RecruiterController.cs  # Recruiter workbench (search, assign, schedule)
│   ├── HrController.cs         # HR portal dashboard
│   ├── AccountController.cs    # Login, register, offer acceptance
│   ├── ExamController.cs       # Assessment taking & submission
│   ├── HomeController.cs       # Candidate dashboard
│   └── SecureFilesController.cs # Ownership-checked private file serving
├── Models/
│   ├── Models.cs                # Core entities + view-models
│   └── PortalModels.cs          # Portal roles, recruitment stages, registration DTOs
├── Data/
│   ├── AppDbContext.cs          # EF Core context + seeder
│   └── AppDbContextFactory.cs  # Design-time factory for tooling
├── Repositories/                # Generic read-only repository pattern
├── Services/
│   ├── NotificationService.cs  # Queued multi-channel notifications + Gmail SMTP
│   ├── EnterprisePortalService.cs # Per-role dashboard builder (cached)
│   ├── EligibilityService.cs   # Candidate eligibility evaluation
│   ├── AssessmentScoringService.cs
│   ├── CandidateRegistrationService.cs / CandidateWorkflowService.cs
│   ├── AuditService.cs
│   ├── FileStorageService.cs   # Private upload storage + SHA-256 hashing
│   └── OfferLetterPdfService.cs  # QuestPDF offer letter generation
├── Views/                       # Razor views
├── wwwroot/                     # Static assets
├── Program.cs                   # App startup + DB init
└── appsettings.json
```

---

## Key Features

- 16-step recruitment pipeline (Register → Onboard)
- Secure time-limited assessment invitations (token links)
- Proctoring: full-screen enforcement, copy-paste block, webcam, screen monitoring
- Coding questions with auto-save
- Offer letter PDF generation (QuestPDF)
- Gmail SMTP email notifications
- Rate-limited login (10 req/min per IP)
- Admin dashboard with pipeline kanban view
- Role-specific enterprise portals for Admin, Recruiter, Candidate, and HR users
