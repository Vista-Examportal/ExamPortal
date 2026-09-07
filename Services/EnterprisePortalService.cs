using ExamPortal.Models;
using ExamPortal.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ExamPortal.Services
{
    public class EnterprisePortalService
    {
        private readonly IReadRepository<User> _users;
        private readonly IReadRepository<InterviewRecord> _interviews;
        private readonly IReadRepository<OfferLetter> _offers;
        private readonly IReadRepository<NotificationMessage> _notificationMessages;
        private readonly IMemoryCache _cache;
        private readonly ActivityFeedService _activityFeed;

        public EnterprisePortalService(
            IReadRepository<User> users,
            IReadRepository<InterviewRecord> interviews,
            IReadRepository<OfferLetter> offers,
            IReadRepository<NotificationMessage> notificationMessages,
            IMemoryCache cache,
            ActivityFeedService activityFeed)
        {
            _users = users;
            _interviews = interviews;
            _offers = offers;
            _notificationMessages = notificationMessages;
            _cache = cache;
            _activityFeed = activityFeed;
        }

        public PortalDashboardViewModel BuildDashboard(string role) =>
            _cache.GetOrCreate($"portal-dashboard:{role}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(45);
                entry.Size = 1;
                return BuildDashboardSnapshot(role);
            })!;

        // Only HrController.Index() calls BuildDashboard today — AdminController and
        // RecruiterController each build their own dashboard data directly in their
        // Index() actions rather than going through this service (see the Functional
        // Workflow Audit for why). The Admin/Recruiter dashboard builders that used to
        // live here were removed as dead code (verified zero callers); BuildHrDashboard
        // below is the template to follow if a future refactor moves those controllers
        // onto this shared service too.
        private PortalDashboardViewModel BuildDashboardSnapshot(string role)
        {
            if (role != PortalRoles.Hr)
                throw new NotSupportedException($"{nameof(EnterprisePortalService)}.{nameof(BuildDashboard)} only supports the '{PortalRoles.Hr}' role today.");

            var candidates = _users.Query()
                .Include(u => u.Address)
                .Where(u => u.Role == PortalRoles.Candidate)
                .OrderByDescending(u => u.CreatedAt)
                .ToList();

            var upcomingInterviews = _interviews.Query()
                .Include(i => i.User)
                .Where(i => i.Status == "Scheduled" && i.ScheduledAt >= DateTime.UtcNow.AddDays(-1))
                .OrderBy(i => i.ScheduledAt)
                .Take(8)
                .ToList();

            var activeOffers = _offers.Query()
                .Include(o => o.User)
                .Where(o => o.Status == "Issued" || o.Status == "Accepted")
                .OrderByDescending(o => o.IssuedAt)
                .Take(8)
                .ToList();

            var recentNotifications = _notificationMessages.Query()
                .Include(n => n.User)
                .OrderByDescending(n => n.CreatedAt)
                .Take(8)
                .ToList();

            return BuildHrDashboard(candidates, upcomingInterviews, activeOffers, recentNotifications);
        }

        private PortalDashboardViewModel BuildHrDashboard(
            List<User> candidates,
            List<InterviewRecord> upcomingInterviews,
            List<OfferLetter> activeOffers,
            List<NotificationMessage> recentNotifications)
        {
            var hrQueue = candidates
                .Where(c => c.RecruitmentStage is RecruitmentStages.InterviewInProgress
                    or RecruitmentStages.DocumentsRequested
                    or RecruitmentStages.DocumentsVerified
                    or RecruitmentStages.OfferIssued
                    or RecruitmentStages.OfferAccepted
                    or RecruitmentStages.Onboarding)
                .Take(8)
                .ToList();

            return new PortalDashboardViewModel
            {
                PortalName = "HR Portal",
                PortalRole = PortalRoles.Hr,
                WelcomeMessage = "Coordinate interviews, documents, offers, and onboarding with operational clarity.",
                Metrics = new()
                {
                    Metric("Interviews", upcomingInterviews.Count, "fas fa-calendar-check", "primary"),
                    Metric("Docs Requested", candidates.Count(c => c.RecruitmentStage == RecruitmentStages.DocumentsRequested), "fas fa-folder-open", "warning"),
                    Metric("Offers Issued", activeOffers.Count(o => o.Status == "Issued"), "fas fa-file-signature", "success"),
                    Metric("Onboarding", candidates.Count(c => c.RecruitmentStage == RecruitmentStages.Onboarding), "fas fa-door-open", "info")
                },
                // Four cards previously all pointed at the exact same URL (Admin/Pipeline,
                // no fragment) — distinct labels promising distinct destinations that were
                // actually one identical link. Document Verification and Offer Management
                // are folded into a single "Offer" column on that board (see the folding
                // notes in Views/Admin/Pipeline.cshtml), so rather than fake a difference
                // that doesn't exist yet, those two are combined into one honestly-scoped
                // card; Interview Operations and Onboarding do have their own columns, so
                // each now deep-links to its own anchor instead of the top of the page.
                Modules = new()
                {
                    Module("Interview Operations", "Schedule, reschedule, and close interview rounds.", "fas fa-comments", "Admin", "Pipeline", "primary", "pipeline-interview"),
                    Module("Offers & Documents", "Review submitted documents, issue offers, and track acceptance.", "fas fa-file-shield", "Admin", "Pipeline", "warning", "pipeline-offer"),
                    Module("Onboarding", "Move accepted candidates into onboarding.", "fas fa-briefcase", "Admin", "Pipeline", "info", "pipeline-onboarding")
                },
                PriorityCandidates = hrQueue,
                UpcomingInterviews = upcomingInterviews,
                ActiveOffers = activeOffers,
                Notifications = recentNotifications,
                // HR-scoped Recent Activity (offers/documents/onboarding only, no exam
                // attempts — that's Recruiter's concern) and actionable alerts (pending
                // approvals, missing documents, expiring offers, etc.), replacing the
                // raw NotificationMessage dump and the RecentAttempts-based merge the
                // shared _PortalDashboard.cshtml partial used to build view-locally.
                ActivityFeed = _activityFeed.BuildHrFeed(),
                Alerts = _activityFeed.BuildHrAlerts(),
                PipelineCounts = candidates
                    .GroupBy(c => c.RecruitmentStage ?? RecruitmentStages.Registered)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        private static PortalMetric Metric(string label, int value, string icon, string color) =>
            new() { Label = label, Value = value.ToString("N0"), Icon = icon, Color = color };

        private static PortalModule Module(string title, string description, string icon, string controller, string action, string color, string? fragment = null) =>
            new()
            {
                Title = title,
                Description = description,
                Icon = icon,
                Controller = controller,
                Action = action,
                Color = color,
                Fragment = fragment
            };
    }
}
