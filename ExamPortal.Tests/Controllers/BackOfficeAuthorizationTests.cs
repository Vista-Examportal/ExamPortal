using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ExamPortal.Controllers;
using ExamPortal.Data;
using ExamPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExamPortal.Tests.Controllers
{
    // Pins which roles may invoke each shared back-office action (AdminController is the shared
    // host for the Admin, Recruiter and HR working pages, so its class-level [Authorize] lets
    // all three in and each action narrows it). These are the role boundaries the dashboards
    // and CandidateDetail forms are built around — if one changes, the matching view gating
    // (canScreen / canReviewDocuments / canManageOffers) must change with it.
    public class BackOfficeAuthorizationTests
    {
        private static readonly string[] AdminRecruiter = { PortalRoles.Admin, PortalRoles.Recruiter };
        private static readonly string[] AdminHr = { PortalRoles.Admin, PortalRoles.Hr };
        private static readonly string[] AllBackOffice = { PortalRoles.Admin, PortalRoles.Hr, PortalRoles.Recruiter };
        private static readonly string[] AdminOnly = { PortalRoles.Admin };

        private static string[] EffectiveRoles(Type controller, MethodInfo action)
        {
            var attrs = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
            if (attrs.Count == 0)
                attrs = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
            var roles = attrs.Select(a => a.Roles).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
            Assert.False(string.IsNullOrWhiteSpace(roles), $"{controller.Name}.{action.Name} has no role-based [Authorize].");
            return roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(r => r, StringComparer.Ordinal).ToArray();
        }

        private static void AssertRoles(Type controller, string actionName, string[] expected)
        {
            var overloads = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == actionName)
                .ToList();
            Assert.NotEmpty(overloads);
            foreach (var method in overloads)
                Assert.Equal(expected.OrderBy(r => r, StringComparer.Ordinal), EffectiveRoles(controller, method));
        }

        // ── Candidate screening & assessment invitations: Recruiter workflow, not HR ──

        [Theory]
        [InlineData("ReviewProfile")]
        [InlineData("SendInvitation")]
        [InlineData("ResendInvitation")]
        [InlineData("Shortlist")]
        [InlineData("SendShortlistEmail")]
        public void ScreeningActions_AreAdminAndRecruiterOnly(string action) =>
            AssertRoles(typeof(AdminController), action, AdminRecruiter);

        // ── Document review: HR workflow, not Recruiter ──────────────────────────────

        [Theory]
        [InlineData("RequestDocuments")]
        [InlineData("VerifyDocument")]
        public void DocumentReviewActions_AreAdminAndHrOnly(string action) =>
            AssertRoles(typeof(AdminController), action, AdminHr);

        // ── Offers & onboarding: Admin/HR only (Recruiter has view-only visibility) ──

        [Theory]
        [InlineData("IssueOffer")]
        [InlineData("ApproveOffer")]
        [InlineData("RejectOffer")]
        [InlineData("BeginOnboarding")]
        public void OfferMutations_AreAdminAndHrOnly(string action) =>
            AssertRoles(typeof(AdminController), action, AdminHr);

        [Fact]
        public void OfferLetterDownload_IsVisibleToAllBackOfficeRoles() =>
            AssertRoles(typeof(AdminController), "DownloadOfferLetter", AllBackOffice);

        // ── Interviews are shared by Recruiter and HR (and Admin) ────────────────────

        [Theory]
        [InlineData("ScheduleInterview")]
        [InlineData("CompleteInterview")]
        [InlineData("MarkInterviewMissed")]
        [InlineData("CancelInterview")]
        [InlineData("AssignInterviewPanel")]
        [InlineData("RescheduleInterview")]
        public void InterviewActions_AreSharedByAllBackOfficeRoles(string action) =>
            AssertRoles(typeof(AdminController), action, AllBackOffice);

        // ── Assessments & exams: Admin + Recruiter ───────────────────────────────────

        [Theory]
        [InlineData("CreateExam")]
        [InlineData("ToggleExam")]
        [InlineData("EditSchedule")]
        [InlineData("ExamResults")]
        [InlineData("GradeAttempt")]
        public void AssessmentManagement_IsAdminAndRecruiterOnly(string action) =>
            AssertRoles(typeof(AdminController), action, AdminRecruiter);

        // ── Operational tools: Admin only ────────────────────────────────────────────

        [Theory]
        [InlineData("Index")]
        [InlineData("CreateStaffUser")]
        [InlineData("UpdateUserRole")]
        [InlineData("EditStaffUser")]
        [InlineData("UpdateSystemSetting")]
        [InlineData("RetryFailedNotifications")]
        [InlineData("DispatchNotifications")]
        public void AdminTools_AreAdminOnly(string action) =>
            AssertRoles(typeof(AdminController), action, AdminOnly);

        [Fact]
        public void RoleDashboards_AreRestrictedToTheirOwnRole()
        {
            AssertRoles(typeof(HrController), "Index", new[] { PortalRoles.Hr });
            AssertRoles(typeof(HrController), "Reports", new[] { PortalRoles.Hr });
            AssertRoles(typeof(RecruiterController), "Index", new[] { PortalRoles.Recruiter });
            AssertRoles(typeof(HomeController), "Index", new[] { PortalRoles.Candidate });
        }

        // ── Secure file access: recruiters only get resume / photo / certification files ─

        private static AppDbContext BuildContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static async Task<bool> CanAccess(AppDbContext db, string role, string path, int? userId = null)
        {
            var claims = new List<Claim> { new(ClaimTypes.Role, role) };
            if (userId.HasValue) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
            var controller = new SecureFilesController(db, files: null!)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
                }
            };
            var method = typeof(SecureFilesController).GetMethod("CanAccessAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return await (Task<bool>)method.Invoke(controller, new object[] { path })!;
        }

        private static AppDbContext SeedFiles()
        {
            var db = BuildContext();
            db.Users.Add(new User
            {
                Id = 1, Username = "c1", Email = "c1@example.com", PasswordHash = "x", FullName = "Cand One",
                ResumePath = "/secure-files/resumes/cv.pdf", ProfilePhotoPath = "/secure-files/photos/me.png"
            });
            db.CandidateDocuments.AddRange(
                new CandidateDocument { Id = 1, UserId = 1, DocumentType = DocumentTypes.Aadhar, FilePath = "/secure-files/docs/aadhar.pdf" },
                new CandidateDocument { Id = 2, UserId = 1, DocumentType = "Certification", FilePath = "/secure-files/docs/cert.pdf" });
            db.SaveChanges();
            return db;
        }

        [Fact]
        public async Task Recruiter_CanOpenResumePhotoAndCertification()
        {
            using var db = SeedFiles();
            Assert.True(await CanAccess(db, PortalRoles.Recruiter, "/secure-files/resumes/cv.pdf"));
            Assert.True(await CanAccess(db, PortalRoles.Recruiter, "/secure-files/photos/me.png"));
            Assert.True(await CanAccess(db, PortalRoles.Recruiter, "/secure-files/docs/cert.pdf"));
        }

        [Fact]
        public async Task Recruiter_CannotOpenIdentityDocuments()
        {
            using var db = SeedFiles();
            Assert.False(await CanAccess(db, PortalRoles.Recruiter, "/secure-files/docs/aadhar.pdf"));
            Assert.False(await CanAccess(db, PortalRoles.Recruiter, "/secure-files/docs/unknown.pdf"));
        }

        [Fact]
        public async Task HrAndAdmin_CanOpenIdentityDocuments()
        {
            using var db = SeedFiles();
            Assert.True(await CanAccess(db, PortalRoles.Hr, "/secure-files/docs/aadhar.pdf"));
            Assert.True(await CanAccess(db, PortalRoles.Admin, "/secure-files/docs/aadhar.pdf"));
        }

        [Fact]
        public async Task Candidate_CanOpenOnlyTheirOwnFiles()
        {
            using var db = SeedFiles();
            Assert.True(await CanAccess(db, PortalRoles.Candidate, "/secure-files/docs/aadhar.pdf", userId: 1));
            Assert.False(await CanAccess(db, PortalRoles.Candidate, "/secure-files/docs/aadhar.pdf", userId: 2));
        }

        [Fact]
        public void RecruiterVisibleDocumentTypes_ExcludeEveryRequiredIdentityProof()
        {
            foreach (var required in DocumentTypes.Required)
                Assert.False(DocumentTypes.IsRecruiterVisible(required), $"{required} must stay HR/Admin only.");
            Assert.True(DocumentTypes.IsRecruiterVisible("Resume"));
            Assert.False(DocumentTypes.IsRecruiterVisible(null));
        }
    }
}
