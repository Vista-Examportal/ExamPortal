using System.Linq;
using ExamPortal.Models;
using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    // Each role's sidebar must expose the destinations its workflow needs; a destination that
    // lives only inside an in-page tab bar was easy to miss (the Recruiter's offer tracking and
    // communications, the Admin's candidate/assessment/interview/offer work areas).
    public class SidebarNavMapTests
    {
        private static SidebarNavItem[] Items(string role) =>
            SidebarNavMap.For(role).SelectMany(s => s.Items).ToArray();

        private static SidebarNavItem Find(string role, string label) =>
            Items(role).Single(i => i.Label == label);

        [Fact]
        public void Recruiter_SeesOfferStatusAndCommunications_AsInPageTabs()
        {
            var offers = Find(PortalRoles.Recruiter, "Offer Status");
            Assert.Equal(("Recruiter", "Index", "offers"), (offers.Controller, offers.Action, offers.Tab));

            var comms = Find(PortalRoles.Recruiter, "Communications");
            Assert.Equal(("Recruiter", "Index", "communications"), (comms.Controller, comms.Action, comms.Tab));
        }

        [Fact]
        public void Recruiter_OfferEntryIsNotLabelledAsReleasingOffers()
        {
            // Offers are prepared/approved/released by HR/Admin — the recruiter entry is tracking only.
            Assert.DoesNotContain(Items(PortalRoles.Recruiter), i => i.Label.Contains("Release"));
            Assert.DoesNotContain(Items(PortalRoles.Recruiter), i => i.Label == "Offers");
        }

        [Theory]
        [InlineData("Candidates", "Admin", "Index", "candidates")]
        [InlineData("Assessments", "Admin", "Index", "assessments")]
        [InlineData("Interviews", "Admin", "Index", "interviews")]
        [InlineData("Offers", "Admin", "Index", "offers")]
        [InlineData("Pipeline", "Admin", "Pipeline", null)]
        public void Admin_SeesEveryWorkAreaInTheSidebar(string label, string controller, string action, string? tab)
        {
            var item = Find(PortalRoles.Admin, label);
            Assert.Equal((controller, action, tab), (item.Controller, item.Action, item.Tab));
        }

        [Fact]
        public void Admin_StillSeesPeopleAndSystemEntries()
        {
            var labels = Items(PortalRoles.Admin).Select(i => i.Label).ToArray();
            foreach (var expected in new[] { "Dashboard", "Users", "Recruiters", "HR Staff", "Jobs", "Reports", "Audit Logs", "Settings" })
                Assert.Contains(expected, labels);
        }

        [Fact]
        public void Candidate_SeesRequestedDocumentsAndNotifications()
        {
            var docs = Find(PortalRoles.Candidate, "Documents");
            Assert.Equal(("Home", "Index", "documents-status"), (docs.Controller, docs.Action, docs.Fragment));

            var notes = Find(PortalRoles.Candidate, "Notifications");
            Assert.Equal(("Home", "Index", "notifications"), (notes.Controller, notes.Action, notes.Fragment));
        }

        [Fact]
        public void Hr_KeepsHiringModulesAndAReportsPage()
        {
            var labels = Items(PortalRoles.Hr).Select(i => i.Label).ToArray();
            foreach (var expected in new[] { "Dashboard", "Candidates", "Interviews", "Offers", "Onboarding", "Reports" })
                Assert.Contains(expected, labels);
            var reports = Find(PortalRoles.Hr, "Reports");
            Assert.Equal(("Hr", "Reports"), (reports.Controller, reports.Action));
        }
    }
}
