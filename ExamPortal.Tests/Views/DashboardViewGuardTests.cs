using System;
using System.IO;
using Xunit;

namespace ExamPortal.Tests.Views
{
    // Source-level guards for dashboard wording/derivations that live only in Razor views (no
    // rendering harness here). They pin the specific defects fixed in the dashboard review:
    // a mislabelled HR bucket, joiners drawn from unaccepted offers, a Recruiter tab that
    // implied it could release offers, and headline numbers counted from capped lists.
    public class DashboardViewGuardTests
    {
        private static string Read(string relativePath)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ExamPortal.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the ExamPortal project folder from the test output directory.");
            var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing view file: {relativePath}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void HrDashboard_InterviewBucket_IsLabelledInProgress_NotCompleted()
        {
            var view = Read("Views/Shared/_PortalDashboard.cshtml");

            // The bucket counts RecruitmentStages.InterviewInProgress — candidates whose rounds
            // are still running — so it must not be called "Interview Completed".
            Assert.DoesNotContain("(\"Interview Completed\"", view);
            Assert.Contains("(\"Interview In Progress\"", view);
        }

        [Fact]
        public void HrDashboard_UpcomingJoiners_ComeFromTheAcceptedOnlyList()
        {
            var view = Read("Views/Shared/_PortalDashboard.cshtml");

            Assert.Contains("var upcomingJoiners = Model.UpcomingJoiners;", view);
            // The old derivation (all Issued + Accepted offers, filtered by date only).
            Assert.DoesNotContain(".Where(o => o.JoiningDate.Date >= SchedulingHelper.BusinessToday)", view);
        }

        [Fact]
        public void HrReports_IsNotTheDashboardAgain()
        {
            var view = Read("Views/Hr/Reports.cshtml");

            Assert.Contains("@model HrReportViewModel", view);
            Assert.DoesNotContain("PortalDashboardViewModel", view);
            Assert.DoesNotContain("Model.Metrics", view);
            Assert.DoesNotContain("Model.PriorityCandidates", view);
        }

        [Fact]
        public void RecruiterOffersTab_IsViewOnly_AndDoesNotImplyReleasingOffers()
        {
            var view = Read("Views/Recruiter/Index.cshtml");

            Assert.DoesNotContain("Offer Release", view);
            Assert.DoesNotContain("Offers you issue", view);
            Assert.DoesNotContain("Issue offers from candidate detail", view);
            Assert.Contains("Offer Status", view);
        }

        [Fact]
        public void RecruiterDashboard_HeadlineCounts_ComeFromControllerCounts()
        {
            var view = Read("Views/Recruiter/Index.cshtml");

            Assert.DoesNotContain("offers.Count(o => o.Status", view);
            Assert.DoesNotContain("invitations.Count(i => effectiveInvStatus", view);
            Assert.Contains("ViewBag.IssuedOffersCount", view);
            Assert.Contains("ViewBag.TodaysInterviews", view);
        }

        [Fact]
        public void AdminDashboard_TileCounts_AreNotTakenFromCappedLists()
        {
            var view = Read("Views/Admin/Index.cshtml");

            Assert.DoesNotContain("reports.Count()", view);
            Assert.DoesNotContain("notifications.Count()", view);
            Assert.DoesNotContain("proctoringLogs.Count()", view);
            Assert.DoesNotContain("candidates.Count()", view);
            Assert.Contains("ViewBag.ReportsCount", view);
        }

        [Fact]
        public void CandidateDashboard_AssessmentStat_UsesTheFullAttemptCount()
        {
            var view = Read("Views/Home/Index.cshtml");

            Assert.DoesNotContain("@Model.Attempts.Count", view);
            Assert.Contains("@Model.AttemptCount", view);
        }

        [Fact]
        public void CandidateDetail_HidesFormsTheRoleCannotSubmit()
        {
            var view = Read("Views/Admin/CandidateDetail.cshtml");

            Assert.Contains("var canScreen = ", view);
            Assert.Contains("var canReviewDocuments = ", view);
            Assert.Contains("@if (!canReviewDocuments)", view);
            Assert.Contains("progress.DocumentsUnlocked && canReviewDocuments", view);
        }
    }
}
