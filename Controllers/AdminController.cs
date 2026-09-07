using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
    [Authorize(Roles = PortalRoles.BackOfficeRoles)]
    // Split across AdminController.cs (this file: shared fields/constructor,
    // small cross-cutting helpers, and Admin-only system/staff actions) plus
    // AdminController.Candidates.cs, .Interviews.cs, .Documents.cs, .Offers.cs,
    // and .Assessments.cs. All are the same partial class — same fields, same DB
    // context, same authorization — split purely for file-size readability.
    public partial class AdminController : Controller
    {
        private readonly AppDbContext _db;
        private readonly NotificationService _notifications;
        private readonly OfferLetterPdfService _offerPdf;
        private readonly AssessmentInvitationService _invitations;
        private readonly ActivityFeedService _activityFeed;
        private readonly AssessmentScoringService _scoring;

        public AdminController(AppDbContext db, NotificationService notifications,
            OfferLetterPdfService offerPdf, AssessmentInvitationService invitations,
            ActivityFeedService activityFeed, AssessmentScoringService scoring)
        {
            _db = db;
            _notifications = notifications;
            _offerPdf = offerPdf;
            _invitations = invitations;
            _activityFeed = activityFeed;
            _scoring = scoring;
        }

        /// <summary>
        /// Caps a free-text admin input to a reasonable max length, trimming whitespace first.
        /// Delegates to the shared TextUtils.Clamp (trim=true — admin-entered text is trimmed
        /// before truncating, unlike ExamController's Clamp, which preserves whitespace since
        /// it clamps exam-answer text/code). Kept as a local wrapper across all of this
        /// controller's partial files (AdminController.*.cs) so none of the ~55 call sites
        /// needed to change when TextUtils.Clamp was extracted.
        /// </summary>
        private static string Clamp(string? value, int maxLength) => TextUtils.Clamp(value, maxLength, trim: true);


        private void EnsureDefaultSystemSettings()
        {
            if (_db.SystemSettings.Any()) return;

            _db.SystemSettings.AddRange(
                new SystemSetting { Key = "PortalName", Category = "Branding", Value = "VISTAWAYS TECH Recruitment Portal", Description = "Name shown across admin and recruitment workflows." },
                new SystemSetting { Key = "DefaultAssessmentExpiryDays", Category = "Assessment", Value = "7", Description = "Default secure link expiry in days." },
                new SystemSetting { Key = "SupportEmail", Category = "Email", Value = "support@vistawaystech.com", Description = "Support mailbox used in candidate communications." },
                new SystemSetting { Key = "RequireProctoringByDefault", Category = "Assessment", Value = "true", Description = "Default proctoring requirement for new assessments." }
            );
            _db.SaveChanges();
        }


        private void RecordAdminAudit(string action, string entityName, int? entityId, string details)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Actor = User.Identity?.Name ?? "Admin",
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                Details = details
            });
        }


        private static string StaffIdentifier(string role)
        {
            var prefix = role switch
            {
                PortalRoles.Admin => "STAFF-ADM",
                PortalRoles.Recruiter => "STAFF-REC",
                PortalRoles.Hr => "STAFF-HR",
                _ => "STAFF"
            };
            return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        }

        private static readonly string[] AdminAssignableRoles =
        {
            PortalRoles.Admin,
            PortalRoles.Recruiter,
            PortalRoles.Hr,
            PortalRoles.Candidate
        };


        // FormatInterviewSchedule moved to Services/SchedulingHelper.cs (shared, not Admin-only).

        private static string NormalizeCandidateStatus(string status)
        {
            status = Clamp(status, 100);
            return status switch
            {
                RecruitmentStages.Shortlisted => RecruitmentStages.Shortlisted,
                RecruitmentStages.InterviewInProgress => RecruitmentStages.InterviewInProgress,
                RecruitmentStages.DocumentsRequested => RecruitmentStages.DocumentsRequested,
                RecruitmentStages.DocumentsVerified => RecruitmentStages.DocumentsVerified,
                RecruitmentStages.OfferIssued => RecruitmentStages.OfferIssued,
                RecruitmentStages.Rejected => RecruitmentStages.Rejected,
                _ => ""
            };
        }


        // ── Dashboard ─────────────────────────────────────────────────────────

        [Authorize(Roles = PortalRoles.Admin)]
        public IActionResult Index()
        {
            EnsureDefaultSystemSettings();

            var candidates = _db.Users
                .AsNoTracking()
                .Include(u => u.ProfileCompletion)
                .Where(u => u.Role == PortalRoles.Candidate)
                .OrderByDescending(u => u.CreatedAt)
                .Take(40)
                .ToList();

            var staffUsers = _db.Users
                .AsNoTracking()
                .Where(u => u.Role != PortalRoles.Candidate)
                .OrderBy(u => u.Role)
                .ThenBy(u => u.FullName)
                .ToList();

            var vm = new AdminDashboardViewModel
            {
                TotalUsers = _db.Users.Count(u => u.Role == PortalRoles.Candidate),
                TotalExams = _db.Exams.Count(),
                TotalAttempts = _db.ExamAttempts.Count(),
                Exams = _db.Exams.AsNoTracking().Include(e => e.Questions).ToList(),
                RecentAttempts = _db.ExamAttempts.AsNoTracking().Include(a => a.User).Include(a => a.Exam)
                    .OrderByDescending(a => a.StartedAt).Take(10).ToList()
            };

            ViewBag.StaffUsers = staffUsers;
            ViewBag.Recruiters = staffUsers.Where(u => u.Role == PortalRoles.Recruiter).ToList();
            ViewBag.Candidates = candidates;
            ViewBag.Interviews = _db.InterviewRecords.AsNoTracking().Include(i => i.User)
                .OrderByDescending(i => i.ScheduledAt ?? i.CreatedAt)
                .Take(25)
                .ToList();
            ViewBag.Offers = _db.OfferLetters.AsNoTracking().Include(o => o.User)
                .OrderByDescending(o => o.IssuedAt)
                .Take(25)
                .ToList();
            ViewBag.AuditLogs = _db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.CreatedAt)
                .Take(30)
                .ToList();
            ViewBag.SystemSettings = _db.SystemSettings.AsNoTracking()
                .OrderBy(s => s.Category)
                .ThenBy(s => s.Key)
                .ToList();
            ViewBag.Notifications = _db.Notifications.AsNoTracking().Include(n => n.User)
                .OrderByDescending(n => n.CreatedAt)
                .Take(25)
                .ToList();
            ViewBag.Reports = _db.AssessmentReports.AsNoTracking().Include(r => r.Exam)
                .OrderByDescending(r => r.GeneratedAt)
                .Take(25)
                .ToList();
            ViewBag.ProctoringLogs = _db.ProctoringLogs.AsNoTracking().Include(p => p.Attempt).ThenInclude(a => a!.User)
                .Include(p => p.Attempt).ThenInclude(a => a!.Exam)
                .OrderByDescending(p => p.OccurredAt)
                .Take(20)
                .ToList();
            ViewBag.PipelineCounts = _db.Users.AsNoTracking()
                .Where(u => u.Role == PortalRoles.Candidate)
                .GroupBy(u => u.RecruitmentStage)
                .Select(g => new { Stage = g.Key, Count = g.Count() })
                .ToDictionary(g => g.Stage, g => g.Count);
            ViewBag.ActiveInvitations = _db.AssessmentInvitations.Count(i => i.Status == "Pending" && i.ExpiresAt > DateTime.UtcNow);
            ViewBag.AverageScore = _db.ExamAttempts.AsNoTracking().Where(a => a.SubmittedAt != null).Select(a => (decimal?)a.PercentageScore).Average() ?? 0;
            ViewBag.ShortlistedCount = _db.Users.Count(u => u.Role == PortalRoles.Candidate && u.RecruitmentStage == RecruitmentStages.Shortlisted);
            ViewBag.PendingOffersCount = _db.OfferLetters.Count(o => o.Status == "Issued");
            ViewBag.MonthlyHiredCount = _db.OfferLetters.Count(o => o.AcceptedAt.HasValue
                && o.AcceptedAt.Value.Month == DateTime.UtcNow.Month
                && o.AcceptedAt.Value.Year == DateTime.UtcNow.Year);
            ViewBag.TotalAuditLogsCount = _db.AuditLogs.Count();

            vm.ActivityFeed = _activityFeed.BuildAdminFeed();
            vm.Alerts = _activityFeed.BuildAdminAlerts();

            return View(vm);
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin)]
        public IActionResult CreateStaffUser(string fullName, string email, string phone, string role, string password)
        {
            fullName = Clamp(fullName, 160);
            email = Clamp(email, 150).ToLowerInvariant();
            phone = Clamp(phone, 20);
            role = Clamp(role, 50);

            if (!AdminAssignableRoles.Contains(role) || role == PortalRoles.Candidate)
            {
                TempData["Error"] = "Choose a valid staff role.";
                return RedirectToAction("Index");
            }
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || password.Length < 8)
            {
                TempData["Error"] = "Staff name, email, and an 8+ character password are required.";
                return RedirectToAction("Index");
            }
            if (_db.Users.Any(u => u.Email == email || u.Username == email))
            {
                TempData["Error"] = "A user with this email already exists.";
                return RedirectToAction("Index");
            }

            var names = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var user = new User
            {
                FullName = fullName,
                FirstName = names.FirstOrDefault() ?? fullName,
                LastName = names.Length > 1 ? names[1] : "",
                Username = email,
                Email = email,
                MobileNumber = phone,
                PasswordHash = AppDbContext.HashPassword(password),
                Role = role,
                CandidateId = StaffIdentifier(role),
                IsEmailVerified = true,
                IsMobileVerified = true,
                RecruitmentStage = "N/A"
            };

            _db.Users.Add(user);
            RecordAdminAudit("CreateStaffUser", nameof(User), null, $"Created {role} account for {email}.");
            _db.SaveChanges();

            TempData["Success"] = $"{role} user created successfully.";
            return RedirectToAction("Index");
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin)]
        public IActionResult UpdateUserRole(int userId, string role)
        {
            role = Clamp(role, 50);
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();
            if (!AdminAssignableRoles.Contains(role))
            {
                TempData["Error"] = "Invalid role selected.";
                return RedirectToAction("Index");
            }
            if (user.Username == User.Identity?.Name && role != PortalRoles.Admin)
            {
                TempData["Error"] = "You cannot remove your own admin role while signed in.";
                return RedirectToAction("Index");
            }

            var oldRole = user.Role;
            user.Role = role;
            if (role == PortalRoles.Candidate && user.RecruitmentStage == "N/A")
            {
                user.RecruitmentStage = RecruitmentStages.Registered;
            }
            else if (role != PortalRoles.Candidate)
            {
                user.RecruitmentStage = "N/A";
            }

            RecordAdminAudit("UpdateUserRole", nameof(User), user.Id, $"Changed {user.Email} from {oldRole} to {role}.");
            _db.SaveChanges();

            TempData["Success"] = "Role updated successfully.";
            return RedirectToAction("Index");
        }


        // Fills the gap CreateStaffUser/UpdateUserRole left: name/email/phone could be set once
        // at creation but never corrected afterward — a typo'd email (which doubles as the
        // staff member's login Username) was permanently unfixable through the app. Deliberately
        // doesn't touch Role (UpdateUserRole already owns that, with its own safeguards) or
        // Password (kept a separate, more sensitive operation).
        [Authorize(Roles = PortalRoles.Admin)]
        public IActionResult EditStaffUser(int id)
        {
            var user = _db.Users.Find(id);
            if (user == null || user.Role == PortalRoles.Candidate) return NotFound();

            var model = new EditStaffUserViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.MobileNumber,
                Role = user.Role,
            };
            return View(model);
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin)]
        public IActionResult EditStaffUser(EditStaffUserViewModel model)
        {
            var user = _db.Users.Find(model.Id);
            if (user == null || user.Role == PortalRoles.Candidate) return NotFound();

            model.Email = Clamp(model.Email, 150).ToLowerInvariant();
            model.FullName = Clamp(model.FullName, 160);
            model.Phone = Clamp(model.Phone, 20);
            model.Role = user.Role; // never trust the hidden field over the real record

            if (!ModelState.IsValid)
            {
                return View(model);
            }
            // Same uniqueness check CreateStaffUser already does, just excluding this user's
            // own current row — otherwise saving the form without changing the email would
            // always fail as "already in use by yourself".
            if (_db.Users.Any(u => u.Id != user.Id && (u.Email == model.Email || u.Username == model.Email)))
            {
                ModelState.AddModelError("", "A user with this email already exists.");
                return View(model);
            }

            var oldEmail = user.Email;
            var names = model.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            user.FullName = model.FullName;
            user.FirstName = names.FirstOrDefault() ?? model.FullName;
            user.LastName = names.Length > 1 ? names[1] : "";
            // Username mirrors Email — see CreateStaffUser, where the two are set identically —
            // so they're kept in sync here too rather than letting them drift apart, which
            // would break login (sign-in uses Username, not Email, to look the account up).
            user.Email = model.Email;
            user.Username = model.Email;
            user.MobileNumber = model.Phone;

            RecordAdminAudit("EditStaffUser", nameof(User), user.Id,
                oldEmail == model.Email ? $"Updated details for {model.Email}." : $"Updated details for {oldEmail} (email changed to {model.Email}).");
            _db.SaveChanges();

            TempData["Success"] = $"{user.FullName}'s details were updated.";
            return RedirectToAction("Index");
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin)]
        public IActionResult UpdateSystemSetting(int id, string value)
        {
            var setting = _db.SystemSettings.Find(id);
            if (setting == null) return NotFound();

            setting.Value = Clamp(value, 2000);
            setting.UpdatedAt = DateTime.UtcNow;
            RecordAdminAudit("UpdateSystemSetting", nameof(SystemSetting), setting.Id, $"Updated {setting.Key}.");
            _db.SaveChanges();

            TempData["Success"] = "System setting updated.";
            return RedirectToAction("Index");
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin)]
        public async Task<IActionResult> RetryFailedNotifications(int? notificationId = null)
        {
            await _notifications.RetryFailedAsync(notificationId);
            TempData["Success"] = notificationId.HasValue
                ? "Notification queued for retry."
                : "Failed notifications queued for retry.";
            return RedirectToAction("Index");
        }


        /// <summary>
        /// Manually triggers the email dispatch queue.
        /// Useful in development to verify credentials without waiting 30 s for the background service.
        /// GET /Admin/DispatchNotifications
        /// </summary>
        public async Task<IActionResult> DispatchNotifications()
        {
            int sent = await _notifications.DispatchPendingAsync(HttpContext.RequestAborted);
            TempData["Success"] = sent == 0
                ? "No pending notifications found."
                : $"{sent} notification(s) dispatched successfully.";
            return RedirectToAction("Index");
        }

    }
}
