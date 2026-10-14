using System;
using System.Linq;
using ExamPortal.Configuration;
using ExamPortal.Controllers;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExamPortal.Tests.Controllers
{
    // The Recruiter dashboard's headline numbers must be full database counts. The lists the
    // view renders are capped to the 12 most recent (Take(12)), so counting those lists made
    // every figure top out at 12 and left "Used" links permanently at 0.
    public class RecruiterDashboardTests
    {
        private static AppDbContext BuildContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static RecruiterController BuildController(AppDbContext db)
        {
            var emailOptions = Options.Create(new EmailOptions());
            var notifications = new NotificationService(db, config: null!, httpClientFactory: null!,
                emailOptions: emailOptions, logger: NullLogger<NotificationService>.Instance);
            return new RecruiterController(db, new AssessmentInvitationService(db, notifications, emailOptions), new ActivityFeedService(db));
        }

        private static AppDbContext Seed()
        {
            var db = BuildContext();
            db.Users.Add(new User { Id = 1, Username = "c1", Email = "c1@example.com", PasswordHash = "x", FullName = "Cand One", Role = PortalRoles.Candidate });
            db.Exams.Add(new Exam { Id = 1, Title = "Backend Assessment", IsActive = true });

            for (var i = 1; i <= 15; i++)
                db.OfferLetters.Add(new OfferLetter { Id = i, UserId = 1, Status = "Issued", Designation = "Engineer", IssuedAt = DateTime.UtcNow.AddMinutes(-i), JoiningDate = DateTime.UtcNow.AddDays(30) });
            for (var i = 16; i <= 18; i++)
                db.OfferLetters.Add(new OfferLetter { Id = i, UserId = 1, Status = "Accepted", Designation = "Engineer", IssuedAt = DateTime.UtcNow.AddDays(-5), JoiningDate = DateTime.UtcNow.AddDays(30) });

            var id = 1;
            for (var i = 0; i < 14; i++) // pending and still valid
                db.AssessmentInvitations.Add(new AssessmentInvitation { Id = id++, UserId = 1, ExamId = 1, Status = "Pending", SentAt = DateTime.UtcNow.AddHours(-i), ExpiresAt = DateTime.UtcNow.AddDays(2) });
            for (var i = 0; i < 2; i++)  // Status still reads Pending but the link has expired
                db.AssessmentInvitations.Add(new AssessmentInvitation { Id = id++, UserId = 1, ExamId = 1, Status = "Pending", SentAt = DateTime.UtcNow.AddDays(-9), ExpiresAt = DateTime.UtcNow.AddDays(-2) });
            db.AssessmentInvitations.Add(new AssessmentInvitation { Id = id++, UserId = 1, ExamId = 1, Status = "Accepted", SentAt = DateTime.UtcNow.AddHours(-1), ExpiresAt = DateTime.UtcNow.AddDays(2) });
            for (var i = 0; i < 2; i++)
                db.AssessmentInvitations.Add(new AssessmentInvitation { Id = id++, UserId = 1, ExamId = 1, Status = "Used", SentAt = DateTime.UtcNow.AddDays(-6), ExpiresAt = DateTime.UtcNow.AddDays(-3) });

            // 15 stale "Scheduled" interviews fill the 12-row UpcomingInterviews cap...
            for (var i = 1; i <= 15; i++)
                db.InterviewRecords.Add(new InterviewRecord { Id = i, UserId = 1, Status = "Scheduled", ScheduledAt = DateTime.UtcNow.AddDays(-30 + i) });
            // ...and two interviews are genuinely today (IST calendar day).
            var todayStartUtc = SchedulingHelper.BusinessDayStartUtc(SchedulingHelper.BusinessToday);
            db.InterviewRecords.Add(new InterviewRecord { Id = 100, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddHours(12) });
            db.InterviewRecords.Add(new InterviewRecord { Id = 101, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddHours(4) });
            db.SaveChanges();
            return db;
        }

        [Fact]
        public void Index_OfferCounts_AreFullCounts_NotTheTwelveRowDisplayList()
        {
            using var db = Seed();
            var controller = BuildController(db);

            controller.Index();

            Assert.Equal(15, (int)controller.ViewBag.IssuedOffersCount);
            Assert.Equal(3, (int)controller.ViewBag.AcceptedOffersCount);
        }

        [Fact]
        public void Index_AssessmentLinkCounts_UseEffectiveStatus_AndCountEveryInvitation()
        {
            using var db = Seed();
            var controller = BuildController(db);

            controller.Index();

            Assert.Equal(14, (int)controller.ViewBag.PendingLinkCount);   // 2 expired "Pending" rows are not live links
            Assert.Equal(1, (int)controller.ViewBag.AcceptedLinkCount);
            Assert.Equal(2, (int)controller.ViewBag.UsedLinkCount);
            Assert.Equal(2, (int)controller.ViewBag.OtherLinkCount);      // the expired ones
        }

        [Fact]
        public void Index_TodaysInterviews_AreFoundEvenWhenOlderScheduledRowsFillTheDisplayCap()
        {
            using var db = Seed();
            var controller = BuildController(db);

            controller.Index();

            var today = (System.Collections.Generic.List<InterviewRecord>)controller.ViewBag.TodaysInterviews;
            Assert.Equal(new[] { 101, 100 }, today.Select(i => i.Id).ToArray());   // soonest first
        }

        // ── Upcoming interviews: not before today (IST); older "Scheduled" ones are overdue ──

        [Fact]
        public void Index_UpcomingInterviews_ExcludeScheduledInterviewsFromEarlierDays()
        {
            using var db = Seed();   // 15 stale Scheduled interviews (15–29 days ago) + 2 today
            var yesterdayUtc = SchedulingHelper.UpcomingInterviewCutoffUtc.AddHours(-3);
            db.InterviewRecords.Add(new InterviewRecord { Id = 200, UserId = 1, Status = "Scheduled", ScheduledAt = yesterdayUtc });
            db.InterviewRecords.Add(new InterviewRecord { Id = 201, UserId = 1, Status = "Scheduled", ScheduledAt = DateTime.UtcNow.AddDays(3) });
            db.SaveChanges();
            var controller = BuildController(db);

            controller.Index();

            var upcoming = (System.Collections.Generic.List<InterviewRecord>)controller.ViewBag.UpcomingInterviews;
            // Today's two and the future one; nothing dated before today, however old.
            Assert.Equal(new[] { 101, 100, 201 }, upcoming.Select(i => i.Id).ToArray());
        }

        [Fact]
        public void RecruiterAlerts_FlagPastInterviewsStillMarkedScheduled_ButNotTodayOrFuture()
        {
            using var db = BuildContext();
            db.Users.Add(new User { Id = 1, Username = "c1", Email = "c1@example.com", PasswordHash = "x", FullName = "Cand One", Role = PortalRoles.Candidate });
            var todayStartUtc = SchedulingHelper.UpcomingInterviewCutoffUtc;
            db.InterviewRecords.AddRange(
                new InterviewRecord { Id = 1, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddDays(-1) },
                new InterviewRecord { Id = 2, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddDays(-9) },
                new InterviewRecord { Id = 3, UserId = 1, Status = "Scheduled", ScheduledAt = todayStartUtc.AddHours(2) },   // today
                new InterviewRecord { Id = 4, UserId = 1, Status = "Scheduled", ScheduledAt = DateTime.UtcNow.AddDays(2) }, // future
                new InterviewRecord { Id = 5, UserId = 1, Status = "Completed", Outcome = "Passed", ScheduledAt = todayStartUtc.AddDays(-4) });
            db.SaveChanges();

            var alerts = new ActivityFeedService(db).BuildRecruiterAlerts(take: 20);

            var alert = Assert.Single(alerts, a => a.Title == "Interviews awaiting an outcome");
            Assert.StartsWith("2 interview(s)", alert.Detail);
        }

        // ── Registered-date filter (dateFrom / dateTo) ───────────────────────────────

        private static DateTime Ist(int y, int m, int d, int h, int min) =>
            SchedulingHelper.ConvertWallClockToUtc(new DateTime(y, m, d, h, min, 0), SchedulingHelper.BusinessTimeZoneId);

        private static AppDbContext SeedRegistrations()
        {
            var db = BuildContext();
            DateTime[] registered =
            {
                Ist(2026, 3, 10, 12, 0),    // 1: before the range
                Ist(2026, 3, 15, 0, 10),    // 2: first minutes of the From day (IST) — included
                Ist(2026, 3, 20, 9, 0),     // 3: inside
                Ist(2026, 3, 25, 23, 30),   // 4: last minutes of the To day (IST) — included
                Ist(2026, 3, 26, 0, 30),    // 5: just after the To day — excluded
                Ist(2026, 4, 2, 9, 0),      // 6: after the range
            };
            for (var i = 0; i < registered.Length; i++)
                db.Users.Add(new User
                {
                    Id = i + 1, Username = $"c{i + 1}", Email = $"c{i + 1}@example.com", PasswordHash = "x",
                    FullName = $"Candidate {i + 1}", Role = PortalRoles.Candidate, CreatedAt = registered[i]
                });
            db.SaveChanges();
            return db;
        }

        private static int[] SearchResultIds(RecruiterController controller) =>
            ((PagedResult<User>)controller.ViewBag.CandidateSearchResults).Items.Select(u => u.Id).OrderBy(i => i).ToArray();

        [Fact]
        public void Index_RegisteredDateRange_IsInclusiveOfBothWholeIstDays()
        {
            using var db = SeedRegistrations();
            var controller = BuildController(db);

            controller.Index(dateFrom: "2026-03-15", dateTo: "2026-03-25");

            Assert.Equal(new[] { 2, 3, 4 }, SearchResultIds(controller));
        }

        [Fact]
        public void Index_RegisteredFromOnly_AndToOnly_EachBoundOneSide()
        {
            using var db = SeedRegistrations();
            var from = BuildController(db);
            from.Index(dateFrom: "2026-03-26");
            Assert.Equal(new[] { 5, 6 }, SearchResultIds(from));

            var to = BuildController(db);
            to.Index(dateTo: "2026-03-14");
            Assert.Equal(new[] { 1 }, SearchResultIds(to));
        }

        [Fact]
        public void Index_RegisteredRange_WithReversedDates_IsTreatedAsTheSameRange()
        {
            using var db = SeedRegistrations();
            var controller = BuildController(db);

            controller.Index(dateFrom: "2026-03-25", dateTo: "2026-03-15");

            Assert.Equal(new[] { 2, 3, 4 }, SearchResultIds(controller));
        }

        [Fact]
        public void Index_UnparseableDates_AreIgnored_NotAnError()
        {
            using var db = SeedRegistrations();
            var controller = BuildController(db);

            controller.Index(dateFrom: "not-a-date", dateTo: "31/12/2026");

            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, SearchResultIds(controller));
        }
    }
}
