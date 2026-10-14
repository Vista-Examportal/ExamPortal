using System;
using System.Linq;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Repositories;
using ExamPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ExamPortal.Tests.Services
{
    // HR dashboard + report accuracy: headline counts are full database counts (not the length
    // of an 8-row display list), "Upcoming Joiners" are accepted offers only, and HR Reports is
    // real aggregate reporting. Real service over EF in-memory, like the other service tests.
    public class EnterprisePortalServiceTests
    {
        private static AppDbContext BuildContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static EnterprisePortalService BuildService(AppDbContext db) => new(
            new EfReadRepository<User>(db),
            new EfReadRepository<InterviewRecord>(db),
            new EfReadRepository<OfferLetter>(db),
            new EfReadRepository<NotificationMessage>(db),
            new EfReadRepository<CandidateDocument>(db),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            new ActivityFeedService(db));

        private static User Candidate(int id, string stage) => new()
        {
            Id = id, Username = $"c{id}", Email = $"c{id}@example.com", PasswordHash = "x",
            FullName = $"Candidate {id}", Role = PortalRoles.Candidate, RecruitmentStage = stage
        };

        private static OfferLetter Offer(int id, int userId, string status, DateTime joining, DateTime? issuedAt = null) => new()
        {
            Id = id, UserId = userId, Status = status, JoiningDate = joining, Designation = "Engineer",
            IssuedAt = issuedAt ?? DateTime.UtcNow
        };

        private static string MetricValue(PortalDashboardViewModel vm, string label) =>
            vm.Metrics.Single(m => m.Label == label).Value;

        // ── Headline counts are not capped display lists ─────────────────────────────

        [Fact]
        public void Dashboard_InterviewAndOfferMetrics_AreFullCounts_NotTheEightRowDisplayCap()
        {
            using var db = BuildContext();
            db.Users.Add(Candidate(1, RecruitmentStages.InterviewInProgress));
            for (var i = 1; i <= 12; i++)
                db.InterviewRecords.Add(new InterviewRecord { Id = i, UserId = 1, Status = "Scheduled", ScheduledAt = DateTime.UtcNow.AddDays(i) });
            for (var i = 1; i <= 10; i++)
                db.OfferLetters.Add(Offer(i, 1, "Issued", DateTime.UtcNow.AddDays(30), DateTime.UtcNow.AddMinutes(-i)));
            db.SaveChanges();

            var vm = BuildService(db).BuildDashboard(PortalRoles.Hr);

            Assert.Equal("12", MetricValue(vm, "Interviews"));
            Assert.Equal("10", MetricValue(vm, "Offers Issued"));
            // The display lists stay capped.
            Assert.Equal(8, vm.UpcomingInterviews.Count);
            Assert.Equal(8, vm.ActiveOffers.Count);
        }

        // ── Upcoming joiners: accepted offers only ───────────────────────────────────

        [Fact]
        public void UpcomingJoiners_IncludeOnlyAcceptedOffers_JoiningTodayOrLater()
        {
            using var db = BuildContext();
            var today = SchedulingHelper.BusinessToday;
            db.Users.Add(Candidate(1, RecruitmentStages.OfferAccepted));
            db.OfferLetters.AddRange(
                Offer(1, 1, "Issued", today.AddDays(5)),     // not accepted yet — must not appear
                Offer(2, 1, "Accepted", today.AddDays(10)),  // appears, second
                Offer(3, 1, "Accepted", today.AddDays(-3)),  // already joined — must not appear
                Offer(4, 1, "Accepted", today),              // joins today — appears, first
                Offer(5, 1, "Declined", today.AddDays(7)),   // declined — must not appear
                Offer(6, 1, "Expired", today.AddDays(8)));   // expired — must not appear
            // Eight newer Issued offers push the accepted ones out of the dashboard's capped
            // ActiveOffers list; the joiners list must not depend on that list.
            for (var i = 10; i < 18; i++)
                db.OfferLetters.Add(Offer(i, 1, "Issued", today.AddDays(20), DateTime.UtcNow.AddHours(1)));
            db.SaveChanges();

            var vm = BuildService(db).BuildDashboard(PortalRoles.Hr);

            Assert.Equal(new[] { 4, 2 }, vm.UpcomingJoiners.Select(o => o.Id).ToArray());
            Assert.All(vm.UpcomingJoiners, o => Assert.Equal("Accepted", o.Status));
        }

        // ── HR alerts: approval vs candidate-response are different things ───────────

        [Fact]
        public void HrAlerts_SeparateOffersAwaitingHrApprovalFromOffersAwaitingTheCandidate()
        {
            using var db = BuildContext();
            db.Users.Add(Candidate(1, RecruitmentStages.OfferIssued));
            db.OfferLetters.AddRange(
                Offer(1, 1, "PendingHRApproval", DateTime.UtcNow.AddDays(30)),
                Offer(2, 1, "Issued", DateTime.UtcNow.AddDays(30)),
                Offer(3, 1, "Issued", DateTime.UtcNow.AddDays(30)));
            db.SaveChanges();

            var alerts = new ActivityFeedService(db).BuildHrAlerts();

            Assert.Contains(alerts, a => a.Title == "Offers awaiting HR approval" && a.Detail.StartsWith("1 offer"));
            Assert.Contains(alerts, a => a.Title == "Offers awaiting candidate response" && a.Detail.StartsWith("2 offer"));
            Assert.DoesNotContain(alerts, a => a.Title == "Pending offer approvals");
        }

        // ── HR Reports: real aggregate reporting ─────────────────────────────────────

        [Fact]
        public void HrReport_AggregatesFunnelInterviewsDocumentsAndOffers_FromFullCounts()
        {
            using var db = BuildContext();
            var today = SchedulingHelper.BusinessToday;
            db.Users.AddRange(
                Candidate(1, RecruitmentStages.InterviewInProgress),
                Candidate(2, RecruitmentStages.InterviewInProgress),
                Candidate(3, RecruitmentStages.OfferAccepted),
                Candidate(4, RecruitmentStages.Rejected));
            db.InterviewRecords.AddRange(
                new InterviewRecord { Id = 1, UserId = 1, Status = "Completed", Outcome = "Passed" },
                new InterviewRecord { Id = 2, UserId = 1, Status = "Completed", Outcome = "Passed" },
                new InterviewRecord { Id = 3, UserId = 2, Status = "Completed", Outcome = "Failed" },
                new InterviewRecord { Id = 4, UserId = 2, Status = "Completed", Outcome = "On Hold" },
                new InterviewRecord { Id = 5, UserId = 1, Status = "Scheduled", ScheduledAt = DateTime.UtcNow.AddDays(2) },
                new InterviewRecord { Id = 6, UserId = 2, Status = "Missed" });
            db.CandidateDocuments.AddRange(
                new CandidateDocument { Id = 1, UserId = 3, VerificationStatus = "Pending" },
                new CandidateDocument { Id = 2, UserId = 3, VerificationStatus = "Pending" },
                new CandidateDocument { Id = 3, UserId = 3, VerificationStatus = "Verified" },
                new CandidateDocument { Id = 4, UserId = 3, VerificationStatus = "Rejected" });
            db.OfferLetters.AddRange(
                Offer(1, 3, "PendingHRApproval", today.AddDays(40)),
                Offer(2, 3, "Accepted", today.AddDays(5)),    // joins within 30 days
                Offer(3, 3, "Accepted", today.AddDays(60)),   // joins later
                Offer(4, 3, "Declined", today.AddDays(20)),
                Offer(5, 3, "Issued", today.AddDays(20)),
                Offer(6, 3, "Expired", today.AddDays(20)));
            db.SaveChanges();

            var report = BuildService(db).BuildHrReport();

            Assert.Equal(4, report.TotalCandidates);
            Assert.Equal(2, report.Funnel.Single(r => r.Label == "Interview In Progress").Count);
            Assert.Equal(50, report.Funnel.Single(r => r.Label == "Interview In Progress").Percent);
            Assert.Equal(1, report.Funnel.Single(r => r.Label == "Rejected").Count);

            Assert.Equal(4, report.InterviewStatuses.Single(r => r.Label == "Completed").Count);
            Assert.Equal(1, report.InterviewStatuses.Single(r => r.Label == "Scheduled").Count);
            Assert.Equal(1, report.InterviewStatuses.Single(r => r.Label == "Missed").Count);
            Assert.Equal(2, report.InterviewOutcomes.Single(r => r.Label == "Passed").Count);
            Assert.Equal(1, report.InterviewOutcomes.Single(r => r.Label == "On hold / other").Count);
            Assert.Equal(67, report.InterviewPassRatePercent);          // 2 passed / (2 passed + 1 failed)

            Assert.Equal(2, report.DocumentStatuses.Single(r => r.Label == "Pending").Count);
            Assert.Equal(1, report.DocumentStatuses.Single(r => r.Label == "Verified").Count);
            Assert.Equal(1, report.DocumentStatuses.Single(r => r.Label == "Rejected").Count);

            Assert.Equal(1, report.OfferStatuses.Single(r => r.Label == "Awaiting HR approval").Count);
            Assert.Equal(2, report.OfferStatuses.Single(r => r.Label == "Accepted").Count);
            Assert.Equal(50, report.OfferAcceptanceRatePercent);        // 2 accepted / (2 + 1 declined + 1 expired)
            Assert.Equal(1, report.OffersAwaitingApproval);
            Assert.Equal(1, report.JoiningNext30Days);
            Assert.Equal(new[] { 2, 3 }, report.UpcomingJoiners.Select(o => o.Id).ToArray());
        }

        [Fact]
        public void HrReport_WithNoData_HasNoRatesAndNoFabricatedFigures()
        {
            using var db = BuildContext();

            var report = BuildService(db).BuildHrReport();

            Assert.Equal(0, report.TotalCandidates);
            Assert.Null(report.InterviewPassRatePercent);
            Assert.Null(report.OfferAcceptanceRatePercent);
            Assert.All(report.Funnel, r => Assert.Equal(0, r.Count));
            Assert.Empty(report.UpcomingJoiners);
        }

        // ── Upcoming interviews: not before today (IST); older "Scheduled" ones are overdue ──

        [Fact]
        public void Dashboard_UpcomingInterviews_ExcludeScheduledInterviewsFromBeforeToday_AndAlertOnThem()
        {
            using var db = BuildContext();
            db.Users.Add(Candidate(1, RecruitmentStages.InterviewInProgress));
            var todayStartUtc = SchedulingHelper.UpcomingInterviewCutoffUtc;
            db.InterviewRecords.AddRange(
                new InterviewRecord { Id = 1, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddHours(-2) },  // yesterday
                new InterviewRecord { Id = 2, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddDays(-12) },  // long overdue
                new InterviewRecord { Id = 3, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddHours(3) },   // today
                new InterviewRecord { Id = 4, UserId = 1, Status = "Scheduled", ScheduledAt = DateTime.UtcNow.AddDays(4) }); // future
            db.SaveChanges();

            var vm = BuildService(db).BuildDashboard(PortalRoles.Hr);

            Assert.Equal("2", MetricValue(vm, "Interviews"));
            Assert.Equal(new[] { 3, 4 }, vm.UpcomingInterviews.Select(i => i.Id).ToArray());
            var alert = Assert.Single(vm.Alerts, a => a.Title == "Interviews awaiting an outcome");
            Assert.StartsWith("2 interview(s)", alert.Detail);
        }
    }
}
