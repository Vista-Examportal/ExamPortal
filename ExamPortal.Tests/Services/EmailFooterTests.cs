using System;
using System.Linq;
using ExamPortal.Configuration;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExamPortal.Tests.Services
{
    // The company footer (logo, VISTAWAYSTECH LLP, address, website) added to outgoing application
    // emails. It is applied when the email is built/sent, so the stored notification bodies and the
    // InApp channel must stay exactly as they were.
    public class EmailFooterTests
    {
        private static AppDbContext BuildContext() =>
            new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static NotificationService BuildNotifications(AppDbContext db, IOptions<EmailOptions> options) =>
            new NotificationService(
                db,
                config: null!,
                httpClientFactory: null!,
                emailOptions: options,
                logger: NullLogger<NotificationService>.Instance);

        private static User MakeCandidate() => new User
        {
            Id = 1,
            Username = "VWT202600001",
            CandidateId = "VWT202600001",
            Email = "candidate@example.com",
            PasswordHash = "not-a-real-hash",
            FullName = "Test Candidate",
            Role = PortalRoles.Candidate,
        };

        // ── EmailFooter.BuildHtml ──────────────────────────────────────────────

        [Fact]
        public void BuildHtml_HasCompanyNameAddressWebsiteAndLogoWithAltText()
        {
            var html = EmailFooter.BuildHtml();

            // the logo already carries the company name, so it isn't repeated as text
            Assert.DoesNotContain("VISTAWAYSTECH LLP", html);
            Assert.Contains("621, 6th Floor, Manjeera Majestic Commercials,", html);
            Assert.Contains("Opp. JNTU, KPHB, Hyderabad-500072", html);
            Assert.Contains("href=\"https://www.vistawaystech.com\"", html);
            Assert.Contains(">www.vistawaystech.com</a>", html);

            // Logo is an inline (cid:) image with alt text, so the footer reads fine with images blocked.
            Assert.Contains("src=\"cid:vwtech-logo\"", html);
            Assert.Contains("alt=\"VISTAWAYS TECH — Innovation. Integration. Impact.\"", html);
        }

        [Fact]
        public void BuildHtml_LogoHasWhiteBacking_SoItStaysLegibleInDarkModeMailApps()
        {
            var html = EmailFooter.BuildHtml();

            // the logo PNG is transparent with dark lettering; both the image and its cell carry a white background
            Assert.Contains("bgcolor=\"#ffffff\"", html);
            Assert.Matches("<img[^>]*background-color:#ffffff", html);
        }

        [Theory]
        [InlineData("Vijayakiron")]
        [InlineData("Abbineni")]
        [InlineData("7995654433")]
        [InlineData("49194111")]
        [InlineData("MOBILE")]
        public void BuildHtml_ContainsNoIndividualNameOrMobileNumbers(string forbidden)
        {
            Assert.DoesNotContain(forbidden, EmailFooter.BuildHtml(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RemoveLogo_ReplacesImageWithCompanyNameText_KeepsTheTextFooter()
        {
            var stripped = EmailFooter.RemoveLogo(EmailFooter.BuildHtml());

            Assert.DoesNotContain("<img", stripped);
            Assert.DoesNotContain("cid:vwtech-logo", stripped);
            Assert.Contains("VISTAWAYSTECH LLP", stripped);
            Assert.Contains("Opp. JNTU, KPHB, Hyderabad-500072", stripped);
            Assert.Contains("www.vistawaystech.com", stripped);
        }

        // ── Shared layout (every generic notification email) ──────────────────

        [Fact]
        public void BuildEmailBody_AddsFooterBetweenMessageAndExistingLegalFooter_WithoutChangingExistingContent()
        {
            var html = NotificationService.BuildEmailBody(
                "candidate@example.com", "sender@example.com", "Verify your VISTAWAYS TECH email",
                "Your email verification OTP is 123456.", new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc));

            // existing content is all still there
            Assert.Contains("Your email verification OTP is 123456.", html);
            Assert.Contains("VISTAWAYS TECH Recruitment Team", html);
            Assert.Contains("This email was sent to", html);
            Assert.Contains("All rights reserved.", html);

            // footer present, positioned after the message and before the existing grey footer
            var message = html.IndexOf("Your email verification OTP is 123456.", StringComparison.Ordinal);
            var company = html.IndexOf("Opp. JNTU, KPHB, Hyderabad-500072", StringComparison.Ordinal);
            var legal = html.IndexOf("This email was sent to", StringComparison.Ordinal);
            Assert.True(message < company && company < legal, "footer should sit between the message and the legal footer");
            Assert.Contains("cid:vwtech-logo", html);
        }

        [Fact]
        public void BuildEmailBody_WithFooterDisabled_HasNoCompanyFooter()
        {
            var html = NotificationService.BuildEmailBody(
                "info@vistawaystech.com", "sender@example.com", "Contact Us: Someone", "Hello",
                DateTime.UtcNow, includeCompanyFooter: false);

            Assert.DoesNotContain("VISTAWAYSTECH LLP", html);
            Assert.DoesNotContain("cid:vwtech-logo", html);
            Assert.Contains("Hello", html);
        }

        [Fact]
        public void BuildEmailBody_StillHtmlEncodesTheMessageBody()
        {
            var html = NotificationService.BuildEmailBody(
                "a@b.com", "s@b.com", "Subject", "<script>alert(1)</script>", DateTime.UtcNow);

            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.DoesNotContain("<script>", html);
        }

        // ── Stored bodies / in-app notifications are not touched ───────────────

        [Fact]
        public void Queue_StoresPlainBodiesForBothChannels_NoFooterInDatabaseOrInApp()
        {
            using var db = BuildContext();
            var notifications = BuildNotifications(db, Options.Create(new EmailOptions()));

            notifications.Queue(MakeCandidate(), null, "Email Verification OTP", "Verify", "Your OTP is 123456.");
            db.SaveChanges();

            foreach (var row in db.Notifications)
            {
                Assert.Equal("Your OTP is 123456.", row.Body);
                Assert.DoesNotContain("VISTAWAYSTECH", row.Body);
            }
        }

        // ── Assessment invitation (has its own layout) ─────────────────────────

        [Fact]
        public void AssessmentInvitation_EmailHasFooter_InAppSummaryDoesNot()
        {
            using var db = BuildContext();
            var emailOptions = Options.Create(new EmailOptions());
            var service = new AssessmentInvitationService(db, BuildNotifications(db, emailOptions), emailOptions);
            var exam = new Exam { Id = 1, Title = "Frontend Developer Assessment", DurationMinutes = 20, TotalMarks = 10, PassingMarks = 5 };

            service.Issue(MakeCandidate(), exam, "link-token", "https://portal/Account/AssessmentAuth?token=link-token",
                statusesToRevoke: new[] { "Pending" });
            db.SaveChanges();

            var email = db.Notifications.Single(n => n.Channel == "Email").Body;
            var inApp = db.Notifications.Single(n => n.Channel == "InApp").Body;

            Assert.Contains("Opp. JNTU, KPHB, Hyderabad-500072", email);
            Assert.Contains("cid:vwtech-logo", email);
            Assert.DoesNotContain("VISTAWAYSTECH", inApp);
            Assert.DoesNotContain("cid:", inApp);
        }
    }
}
