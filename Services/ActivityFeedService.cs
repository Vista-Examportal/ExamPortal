using ExamPortal.Data;
using ExamPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamPortal.Services
{
    /// <summary>
    /// Builds the "Recent Activity" timeline and the "Notifications" (actionable alert)
    /// panel for each portal role, in one place.
    ///
    /// Why this exists: before this service, each dashboard (Admin/Index.cshtml,
    /// Recruiter/Index.cshtml, and the HR shared partial) built its own "recent activity"
    /// list with near-identical, copy-pasted view-local LINQ, and all three rendered the
    /// same raw NotificationMessage (outbound email) log as "Notifications" — just with a
    /// different .Take(n). That meant: (a) three copies of the same merge logic to keep in
    /// sync, and (b) every role saw a slice of the same generic feed instead of the
    /// activity/alerts that are actually relevant to their job.
    ///
    /// Design rule followed throughout: every ActivityItem is built from an entity that
    /// already carries a real timestamp for that specific event (e.g. OfferLetter.IssuedAt
    /// for "offer generated"). Events with no reliable timestamp on the schema today
    /// (e.g. "candidate shortlisted" — RecruitmentStage has no StageChangedAt) are
    /// deliberately left out of the timeline rather than faked with a wrong/borrowed
    /// timestamp; they still surface as counts/alerts where useful.
    /// </summary>
    public class ActivityFeedService
    {
        private readonly AppDbContext _db;

        /// <summary>Admin-only audit actions. AuditLogs also contains candidate/exam
        /// events (e.g. "Start Assessment", written by AuditService from ExamController)
        /// — this allowlist is what keeps those out of the Admin "Recent Activity" feed.</summary>
        private static readonly string[] AdminAuditActions =
        {
            "CreateStaffUser", "UpdateUserRole", "UpdateSystemSetting"
        };

        public ActivityFeedService(AppDbContext db) => _db = db;

        // ── Admin ────────────────────────────────────────────────────────────

        public List<ActivityItem> BuildAdminFeed(int take = 8)
        {
            return _db.AuditLogs
                .AsNoTracking()
                .Where(a => AdminAuditActions.Contains(a.Action))
                .OrderByDescending(a => a.CreatedAt)
                .Take(take)
                .ToList()
                .Select(a => new ActivityItem
                {
                    When = a.CreatedAt,
                    Icon = a.Action switch
                    {
                        "CreateStaffUser" => "user-plus",
                        "UpdateUserRole" => "shield",
                        "UpdateSystemSetting" => "settings",
                        _ => "activity"
                    },
                    Text = $"{a.Action switch
                    {
                        "CreateStaffUser" => "Staff account created",
                        "UpdateUserRole" => "User role changed",
                        "UpdateSystemSetting" => "System setting updated",
                        _ => a.Action
                    }}",
                    Sub = $"{a.Actor} · {a.Details}"
                })
                .ToList();
        }

        public List<PortalAlert> BuildAdminAlerts(int take = 8)
        {
            var alerts = new List<PortalAlert>();
            var since = DateTime.UtcNow.AddDays(-2);

            // Failed logins — logged by AccountController.Login/AssessmentAuth via
            // AuditService.Record(action: "FailedLogin"). Grouped by attempted
            // identifier so a burst against one account reads as one alert, flagged
            // as a possible brute-force attempt at 3+ attempts.
            var failedLogins = _db.AuditLogs
                .AsNoTracking()
                .Where(a => a.Action == "FailedLogin" && a.CreatedAt >= since)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();

            foreach (var group in failedLogins.GroupBy(a => a.Actor).OrderByDescending(g => g.Max(a => a.CreatedAt)).Take(5))
            {
                var count = group.Count();
                var latest = group.Max(a => a.CreatedAt);
                alerts.Add(new PortalAlert
                {
                    When = latest,
                    Icon = "shield-alert",
                    Title = count >= 3 ? "Possible brute-force attempt" : "Failed login attempt",
                    Detail = count == 1
                        ? $"1 failed login for \"{group.Key}\""
                        : $"{count} failed logins for \"{group.Key}\" in the last 48h",
                    Severity = count >= 3 ? "danger" : "warning"
                });
            }

            // Security alerts — high-severity proctoring events (already tracked,
            // reused here rather than duplicated; also shown in detail on the
            // Recruiter dashboard as "Suspicious Activities").
            var highSeverity = _db.ProctoringLogs
                .AsNoTracking()
                .Include(p => p.Attempt).ThenInclude(a => a!.User)
                .Where(p => p.Severity >= 4 && p.OccurredAt >= since)
                .OrderByDescending(p => p.OccurredAt)
                .Take(5)
                .ToList();
            foreach (var log in highSeverity)
            {
                alerts.Add(new PortalAlert
                {
                    When = log.OccurredAt,
                    Icon = "triangle-alert",
                    Title = "High-severity proctoring event",
                    Detail = $"{log.EventType} · {log.Attempt?.User?.FullName}",
                    Severity = "danger",
                    ActionController = "Recruiter",
                    ActionAction = "Index"
                });
            }

            // Platform health — honest placeholders. There is no user-approval
            // workflow (CreateStaffUser provisions immediately) and no backup
            // subsystem in this app yet; showing these as "not configured" rather
            // than fabricating a green checkmark.
            alerts.Add(new PortalAlert
            {
                When = DateTime.UtcNow,
                Icon = "user-check",
                Title = "Pending user approvals",
                Detail = "No approval workflow configured — staff accounts are created immediately by an Admin.",
                Severity = "info"
            });
            alerts.Add(new PortalAlert
            {
                When = DateTime.UtcNow,
                Icon = "database",
                Title = "Backup status",
                Detail = "Not configured — no scheduled backup job exists yet for this environment.",
                Severity = "info"
            });

            return alerts
                .OrderByDescending(a => a.Severity == "danger" ? 2 : a.Severity == "warning" ? 1 : 0)
                .ThenByDescending(a => a.When)
                .Take(take)
                .ToList();
        }

        // ── Recruiter ────────────────────────────────────────────────────────

        public List<ActivityItem> BuildRecruiterFeed(int take = 8)
        {
            var feed = new List<ActivityItem>();
            var since = DateTime.UtcNow.AddDays(-14);

            foreach (var u in _db.Users.AsNoTracking()
                         .Where(u => u.Role == PortalRoles.Candidate && u.CreatedAt >= since)
                         .OrderByDescending(u => u.CreatedAt).Take(take))
                feed.Add(new ActivityItem { When = u.CreatedAt, Icon = "user-plus", Text = "New candidate applied", Sub = u.FullName });

            foreach (var i in _db.AssessmentInvitations.AsNoTracking().Include(i => i.User).Include(i => i.Exam)
                         .Where(i => i.SentAt >= since)
                         .OrderByDescending(i => i.SentAt).Take(take))
                feed.Add(new ActivityItem { When = i.SentAt, Icon = "send", Text = "Assessment assigned", Sub = $"{i.User?.FullName} · {i.Exam?.Title}" });

            foreach (var a in _db.ExamAttempts.AsNoTracking().Include(a => a.User).Include(a => a.Exam)
                         .Where(a => a.SubmittedAt != null && a.SubmittedAt >= since)
                         .OrderByDescending(a => a.SubmittedAt).Take(take))
                feed.Add(new ActivityItem { When = a.SubmittedAt!.Value, Icon = "clipboard-check", Text = "Assessment submitted", Sub = $"{a.User?.FullName} · {a.Exam?.Title}" });

            foreach (var iv in _db.InterviewRecords.AsNoTracking().Include(iv => iv.User)
                         .Where(iv => iv.CreatedAt >= since)
                         .OrderByDescending(iv => iv.CreatedAt).Take(take))
                feed.Add(new ActivityItem { When = iv.CreatedAt, Icon = "calendar-clock", Text = $"Interview scheduled ({iv.Round})", Sub = iv.User?.FullName ?? "" });

            return feed.OrderByDescending(f => f.When).Take(take).ToList();
        }

        public List<PortalAlert> BuildRecruiterAlerts(int take = 8)
        {
            var alerts = new List<PortalAlert>();

            var newApplications = _db.Users.AsNoTracking()
                .Count(u => u.Role == PortalRoles.Candidate && u.CreatedAt >= DateTime.UtcNow.AddDays(-3));
            if (newApplications > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "user-plus",
                    Title = "New applications received",
                    Detail = $"{newApplications} candidate(s) registered in the last 3 days",
                    Severity = "info", ActionController = "Recruiter", ActionAction = "Index"
                });

            var awaitingReview = _db.ExamAttempts.AsNoTracking()
                .Count(a => a.SubmittedAt != null && a.EligibilityStatus == "Pending");
            if (awaitingReview > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "clipboard-list",
                    Title = "Assessments awaiting review",
                    Detail = $"{awaitingReview} submitted attempt(s) not yet evaluated",
                    Severity = "warning", ActionController = "Recruiter", ActionAction = "Index"
                });

            var needsFeedback = _db.InterviewRecords.AsNoTracking()
                .Count(iv => iv.Status == "Completed" && iv.Outcome == "");
            if (needsFeedback > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "message-square-warning",
                    Title = "Interviews requiring feedback",
                    Detail = $"{needsFeedback} completed interview(s) missing an outcome",
                    Severity = "warning", ActionController = "Recruiter", ActionAction = "Index"
                });

            var closingSoon = _db.Exams.AsNoTracking()
                .Count(e => e.IsActive && !e.IsTemplate && e.EndDateTime != null
                    && e.EndDateTime > DateTime.UtcNow && e.EndDateTime < DateTime.UtcNow.AddDays(3));
            if (closingSoon > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "hourglass",
                    Title = "Jobs closing soon",
                    Detail = $"{closingSoon} active assessment(s) closing within 3 days",
                    Severity = "warning", ActionController = "Recruiter", ActionAction = "Index"
                });

            var awaitingAction = _db.Users.AsNoTracking()
                .Count(u => u.Role == PortalRoles.Candidate && u.RecruitmentStage == RecruitmentStages.ProfileUnderReview);
            if (awaitingAction > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "user-cog",
                    Title = "Candidates awaiting action",
                    Detail = $"{awaitingAction} profile(s) pending review",
                    Severity = "info", ActionController = "Recruiter", ActionAction = "Index"
                });

            return alerts.Take(take).ToList();
        }

        // ── HR ───────────────────────────────────────────────────────────────

        public List<ActivityItem> BuildHrFeed(int take = 8)
        {
            var feed = new List<ActivityItem>();
            var since = DateTime.UtcNow.AddDays(-21);

            foreach (var o in _db.OfferLetters.AsNoTracking().Include(o => o.User)
                         .Where(o => o.IssuedAt >= since)
                         .OrderByDescending(o => o.IssuedAt).Take(take))
                feed.Add(new ActivityItem { When = o.IssuedAt, Icon = "file-check", Text = "Offer generated", Sub = $"{o.User?.FullName} · {o.Designation}" });

            foreach (var o in _db.OfferLetters.AsNoTracking().Include(o => o.User)
                         .Where(o => o.AcceptedAt != null && o.AcceptedAt >= since)
                         .OrderByDescending(o => o.AcceptedAt).Take(take))
                feed.Add(new ActivityItem { When = o.AcceptedAt!.Value, Icon = "handshake", Text = "Offer accepted", Sub = $"{o.User?.FullName} · {o.Designation}" });

            foreach (var d in _db.CandidateDocuments.AsNoTracking().Include(d => d.User)
                         .Where(d => d.UploadedAt >= since)
                         .OrderByDescending(d => d.UploadedAt).Take(take))
                feed.Add(new ActivityItem { When = d.UploadedAt, Icon = "file-up", Text = $"{d.DocumentType} document uploaded", Sub = d.User?.FullName ?? "" });

            foreach (var d in _db.CandidateDocuments.AsNoTracking().Include(d => d.User)
                         .Where(d => d.VerificationStatus == "Verified" && d.VerifiedAt != null && d.VerifiedAt >= since)
                         .OrderByDescending(d => d.VerifiedAt).Take(take))
                feed.Add(new ActivityItem { When = d.VerifiedAt!.Value, Icon = "shield-check", Text = "Background verification completed", Sub = d.User?.FullName ?? "" });

            return feed.OrderByDescending(f => f.When).Take(take).ToList();
        }

        public List<PortalAlert> BuildHrAlerts(int take = 8)
        {
            var alerts = new List<PortalAlert>();

            var pendingApprovals = _db.OfferLetters.AsNoTracking().Count(o => o.Status == "Issued");
            if (pendingApprovals > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "file-check",
                    Title = "Pending offer approvals",
                    Detail = $"{pendingApprovals} offer(s) issued, awaiting candidate response",
                    Severity = "info", ActionController = "Admin", ActionAction = "Pipeline", ActionFragment = "pipeline-offer"
                });

            var candidatesAtDocStage = _db.Users.AsNoTracking()
                .Where(u => u.Role == PortalRoles.Candidate && u.RecruitmentStage == RecruitmentStages.DocumentsRequested)
                .Select(u => u.Id)
                .ToList();
            var uploadedTypesByUser = _db.CandidateDocuments.AsNoTracking()
                .Where(d => candidatesAtDocStage.Contains(d.UserId))
                .Select(d => new { d.UserId, d.DocumentType })
                .ToList()
                .GroupBy(d => d.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.DocumentType).ToHashSet());

            var missingDocs = candidatesAtDocStage.Count(uid =>
                !DocumentTypes.Required.All(rt => uploadedTypesByUser.TryGetValue(uid, out var types) && types.Contains(rt)));
            if (missingDocs > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "folder-open",
                    Title = "Missing candidate documents",
                    Detail = $"{missingDocs} candidate(s) still need to submit documents",
                    Severity = "warning", ActionController = "Admin", ActionAction = "Pipeline", ActionFragment = "pipeline-offer"
                });

            var awaitingVerification = candidatesAtDocStage.Count - missingDocs;
            if (awaitingVerification > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "shield-check",
                    Title = "Documents awaiting verification",
                    Detail = $"{awaitingVerification} candidate(s) submitted all documents — verify them to advance to Offer",
                    Severity = "info", ActionController = "Admin", ActionAction = "Pipeline", ActionFragment = "pipeline-offer"
                });

            var verifiedToday = _db.CandidateDocuments.AsNoTracking()
                .Count(d => d.VerificationStatus == "Verified" && d.VerifiedAt != null && d.VerifiedAt.Value.Date == DateTime.UtcNow.Date);
            if (verifiedToday > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "shield-check",
                    Title = "Background verification completed",
                    Detail = $"{verifiedToday} document(s) verified today",
                    Severity = "success"
                });

            var onboardingToday = _db.OfferLetters.AsNoTracking().Include(o => o.User)
                .Where(o => o.Status == "Accepted" && o.JoiningDate.Date == DateTime.UtcNow.Date)
                .ToList();
            if (onboardingToday.Count > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "door-open",
                    Title = "Onboarding starting today",
                    Detail = string.Join(", ", onboardingToday.Select(o => o.User?.FullName)),
                    Severity = "success", ActionController = "Admin", ActionAction = "Pipeline", ActionFragment = "pipeline-onboarding"
                });

            var expiringOffers = _db.OfferLetters.AsNoTracking()
                .Count(o => o.Status == "Issued" && o.OfferValidUntil != null
                    && o.OfferValidUntil > DateTime.UtcNow && o.OfferValidUntil < DateTime.UtcNow.AddDays(3));
            if (expiringOffers > 0)
                alerts.Add(new PortalAlert
                {
                    When = DateTime.UtcNow, Icon = "alarm-clock",
                    Title = "Offer expiration reminders",
                    Detail = $"{expiringOffers} offer(s) expiring within 3 days",
                    Severity = "warning", ActionController = "Admin", ActionAction = "Pipeline", ActionFragment = "pipeline-offer"
                });

            return alerts.Take(take).ToList();
        }

        // ── Candidate ────────────────────────────────────────────────────────

        /// <summary>Built from data the caller (HomeController) already loaded for this
        /// specific candidate — kept as a pure builder over passed-in lists rather than
        /// re-querying, since HomeController.Index already scopes everything to UserId.</summary>
        public List<ActivityItem> BuildCandidateFeed(
            List<AssessmentInvitation> invitations,
            List<ExamAttempt> attempts,
            List<InterviewRecord> interviews,
            OfferLetter? offer,
            int take = 8)
        {
            var feed = new List<ActivityItem>();

            foreach (var i in invitations)
                feed.Add(new ActivityItem { When = i.SentAt, Icon = "send", Text = "Assessment invitation received", Sub = i.Exam?.Title ?? "" });

            foreach (var a in attempts.Where(a => a.SubmittedAt != null))
                feed.Add(new ActivityItem { When = a.SubmittedAt!.Value, Icon = "clipboard-check", Text = "Assessment completed", Sub = a.Exam?.Title ?? "" });

            foreach (var iv in interviews)
            {
                feed.Add(new ActivityItem { When = iv.CreatedAt, Icon = "calendar-clock", Text = $"Interview scheduled ({iv.Round})", Sub = iv.Status });
                // Sub is intentionally a fixed neutral label, NOT iv.Outcome — Outcome
                // (Passed/Failed/On Hold) is internal evaluation data that must not
                // reach the candidate's own activity feed.
                if (iv.CompletedAt != null)
                    feed.Add(new ActivityItem { When = iv.CompletedAt.Value, Icon = "calendar-check", Text = $"Interview completed ({iv.Round})", Sub = "Awaiting recruitment update" });
            }

            if (offer != null)
            {
                feed.Add(new ActivityItem { When = offer.IssuedAt, Icon = "file-check", Text = "Offer received", Sub = offer.Designation });
                if (offer.AcceptedAt != null)
                    feed.Add(new ActivityItem { When = offer.AcceptedAt.Value, Icon = "handshake", Text = "Offer accepted", Sub = offer.Designation });
            }

            return feed.OrderByDescending(f => f.When).Take(take).ToList();
        }
    }
}
