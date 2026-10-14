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
        private readonly IReadRepository<CandidateDocument> _documents;
        private readonly IMemoryCache _cache;
        private readonly ActivityFeedService _activityFeed;

        public EnterprisePortalService(
            IReadRepository<User> users,
            IReadRepository<InterviewRecord> interviews,
            IReadRepository<OfferLetter> offers,
            IReadRepository<NotificationMessage> notificationMessages,
            IReadRepository<CandidateDocument> documents,
            IMemoryCache cache,
            ActivityFeedService activityFeed)
        {
            _users = users;
            _interviews = interviews;
            _offers = offers;
            _notificationMessages = notificationMessages;
            _documents = documents;
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

            // Display lists are capped (Take) — the headline counts are NOT derived from them:
            // each is a full database count, so a busy week shows 23 interviews, not "8".
            var interviewCutoff = SchedulingHelper.UpcomingInterviewCutoffUtc;
            var upcomingInterviewQuery = _interviews.Query()
                .Where(i => i.Status == "Scheduled" && i.ScheduledAt >= interviewCutoff);
            var upcomingInterviewCount = upcomingInterviewQuery.Count();
            var upcomingInterviews = upcomingInterviewQuery
                .Include(i => i.User)
                .OrderBy(i => i.ScheduledAt)
                .Take(8)
                .ToList();

            var issuedOfferCount = _offers.Query().Count(o => o.Status == "Issued");
            var activeOffers = _offers.Query()
                .Include(o => o.User)
                .Where(o => o.Status == "Issued" || o.Status == "Accepted")
                .OrderByDescending(o => o.IssuedAt)
                .Take(8)
                .ToList();

            var upcomingJoiners = QueryUpcomingJoiners(6);

            var recentNotifications = _notificationMessages.Query()
                .Include(n => n.User)
                .OrderByDescending(n => n.CreatedAt)
                .Take(8)
                .ToList();

            return BuildHrDashboard(candidates, upcomingInterviews, upcomingInterviewCount,
                activeOffers, issuedOfferCount, upcomingJoiners, recentNotifications);
        }

        /// <summary>Accepted offers whose joining date (an IST calendar date) is today or later,
        /// soonest first. Only Accepted — an Issued offer can still be declined or expire.</summary>
        private List<OfferLetter> QueryUpcomingJoiners(int take)
        {
            var today = SchedulingHelper.BusinessToday;
            return _offers.Query()
                .Include(o => o.User)
                .Where(o => o.Status == "Accepted" && o.JoiningDate >= today)
                .OrderBy(o => o.JoiningDate)
                .Take(take)
                .ToList();
        }

        private PortalDashboardViewModel BuildHrDashboard(
            List<User> candidates,
            List<InterviewRecord> upcomingInterviews,
            int upcomingInterviewCount,
            List<OfferLetter> activeOffers,
            int issuedOfferCount,
            List<OfferLetter> upcomingJoiners,
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
                    Metric("Interviews", upcomingInterviewCount, "fas fa-calendar-check", "primary"),
                    Metric("Docs Requested", candidates.Count(c => c.RecruitmentStage == RecruitmentStages.DocumentsRequested), "fas fa-folder-open", "warning"),
                    Metric("Offers Issued", issuedOfferCount, "fas fa-file-signature", "success"),
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
                UpcomingJoiners = upcomingJoiners,
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

        /// <summary>
        /// HR → Reports. Aggregate reporting over the whole pipeline (see
        /// <see cref="HrReportViewModel"/>); deliberately different from the dashboard's
        /// headline cards and priority queue. All figures are full database counts.
        /// </summary>
        public HrReportViewModel BuildHrReport()
        {
            var stageCounts = _users.Query()
                .Where(u => u.Role == PortalRoles.Candidate)
                .GroupBy(u => u.RecruitmentStage)
                .Select(g => new { Stage = g.Key, Count = g.Count() })
                .ToList()
                .GroupBy(x => string.IsNullOrEmpty(x.Stage) ? RecruitmentStages.Registered : x.Stage)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
            var totalCandidates = stageCounts.Values.Sum();

            int? Pct(int part, int whole) => whole > 0 ? (int)Math.Round(part * 100.0 / whole) : null;

            var funnelStages = RecruitmentStages.Ordered
                .Concat(new[] { RecruitmentStages.OfferDeclined, RecruitmentStages.Rejected })
                .Distinct();
            var funnel = funnelStages
                .Select(s => new HrReportRow
                {
                    Label = RecruitmentStages.FriendlyLabel(s),
                    Count = stageCounts.TryGetValue(s, out var c) ? c : 0,
                    Percent = Pct(stageCounts.TryGetValue(s, out var c2) ? c2 : 0, totalCandidates)
                })
                .ToList();

            var interviewStatus = _interviews.Query()
                .GroupBy(i => i.Status)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Key, x => x.Count);
            int IStatus(string s) => interviewStatus.TryGetValue(s, out var v) ? v : 0;
            var totalInterviews = interviewStatus.Values.Sum();

            var outcomes = _interviews.Query()
                .Where(i => i.Status == "Completed")
                .GroupBy(i => i.Outcome)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Key, x => x.Count);
            int Outcome(string o) => outcomes.TryGetValue(o, out var v) ? v : 0;
            var passed = Outcome("Passed");
            var failed = Outcome("Failed");
            var completedInterviews = IStatus("Completed");
            var otherOutcomes = completedInterviews - passed - failed;

            var docStatus = _documents.Query()
                .GroupBy(d => d.VerificationStatus)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Key, x => x.Count);
            int Doc(string s) => docStatus.TryGetValue(s, out var v) ? v : 0;
            var totalDocs = docStatus.Values.Sum();

            var offerStatus = _offers.Query()
                .GroupBy(o => o.Status)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Key, x => x.Count);
            int Offer(string s) => offerStatus.TryGetValue(s, out var v) ? v : 0;
            var totalOffers = offerStatus.Values.Sum();
            var resolved = Offer("Accepted") + Offer("Declined") + Offer("Expired");

            var today = SchedulingHelper.BusinessToday;
            var joiningNext30 = _offers.Query()
                .Count(o => o.Status == "Accepted" && o.JoiningDate >= today && o.JoiningDate < today.AddDays(30));

            return new HrReportViewModel
            {
                TotalCandidates = totalCandidates,
                Funnel = funnel,
                InterviewStatuses = new[] { "Scheduled", "Completed", "Missed", "Cancelled" }
                    .Select(s => new HrReportRow { Label = s, Count = IStatus(s), Percent = Pct(IStatus(s), totalInterviews) })
                    .ToList(),
                InterviewOutcomes = new List<HrReportRow>
                {
                    new() { Label = "Passed", Count = passed, Percent = Pct(passed, completedInterviews) },
                    new() { Label = "Failed", Count = failed, Percent = Pct(failed, completedInterviews) },
                    new() { Label = "On hold / other", Count = Math.Max(0, otherOutcomes), Percent = Pct(Math.Max(0, otherOutcomes), completedInterviews) },
                },
                InterviewPassRatePercent = Pct(passed, passed + failed),
                DocumentStatuses = new[] { "Pending", "Verified", "Rejected" }
                    .Select(s => new HrReportRow { Label = s, Count = Doc(s), Percent = Pct(Doc(s), totalDocs) })
                    .ToList(),
                OfferStatuses = new[] { "PendingHRApproval", "Issued", "Accepted", "Declined", "Expired", "Rejected", "Revoked" }
                    .Select(s => new HrReportRow { Label = s == "PendingHRApproval" ? "Awaiting HR approval" : s, Count = Offer(s), Percent = Pct(Offer(s), totalOffers) })
                    .ToList(),
                OfferAcceptanceRatePercent = Pct(Offer("Accepted"), resolved),
                OffersAwaitingApproval = Offer("PendingHRApproval"),
                JoiningNext30Days = joiningNext30,
                UpcomingJoiners = QueryUpcomingJoiners(10)
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
