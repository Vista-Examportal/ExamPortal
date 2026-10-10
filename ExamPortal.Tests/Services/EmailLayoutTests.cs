using System;
using System.Linq;
using System.Net;
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
    // Layout consistency for candidate-facing emails: readable line spacing (1.5), paragraph gaps of
    // 12-16px, OTP code/expiry prominent, company footer directly after the message, and nothing
    // that makes Gmail fold the footer under "Show trimmed content". Wording is never touched.
    public class EmailLayoutTests
    {
        private static string Build(string body, bool footer = true) =>
            NotificationService.BuildEmailBody(
                "candidate@example.com", "sender@example.com", "Subject", body,
                new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc), footer);

        private const string OtpMessage =
            "Dear Test Candidate,\n\nYour Candidate ID is VWT202600001.\n\nYour email verification OTP is 123456.\n\n" +
            "This OTP expires in 15 minutes.\n\nUse your Candidate ID with your password for portal login.\n\nVISTAWAYS TECH Recruitment Team";

        [Fact]
        public void BlankLines_BecomeParagraphsWith14pxGaps_AndLineHeightIs1_5()
        {
            var html = Build("Dear A,\n\nFirst paragraph.\nSecond line of it.\n\nBest regards,\nTeam");

            Assert.Contains("line-height:1.5", html);
            Assert.DoesNotContain("<pre", html);
            Assert.Contains("<p style=\"margin:0 0 14px;white-space:pre-wrap;\">Dear A,</p>", html);
            // single line breaks stay inside their paragraph, exactly as written
            Assert.Contains(">First paragraph.\nSecond line of it.</p>", html);
            Assert.Contains(">Best regards,\nTeam</p>", html);
        }

        [Fact]
        public void WordingIsPreserved_ForEveryParagraph_AndStillHtmlEncoded()
        {
            var body = "Dear <b>A</b> & B,\n\n━━━━━━━━━━\n  SUBMISSION CONFIRMATION\n━━━━━━━━━━\n• Item one\n• Item two\n\nBye";
            var html = Build(body);

            foreach (var paragraph in body.Split("\n\n"))
                Assert.Contains(WebUtility.HtmlEncode(paragraph), html);
            Assert.DoesNotContain("<b>A</b>", html);
        }

        [Fact]
        public void OtpEmail_ShowsCodeAndExpiryProminently_WithoutChangingTheText()
        {
            var html = Build(OtpMessage);

            Assert.Contains("Your email verification OTP is <strong", html);
            Assert.Matches("font-size:28px;letter-spacing:6px[^>]*>123456</strong>", html);
            Assert.Contains("<p style=\"margin:0 0 14px;font-weight:bold;color:#1a1a2e;\">This OTP expires in 15 minutes.</p>", html);
            // the other paragraphs keep their normal style and exact wording
            Assert.Contains(">Your Candidate ID is VWT202600001.</p>", html);
            Assert.Contains(">VISTAWAYS TECH Recruitment Team</p>", html);
        }

        [Fact]
        public void CompanyFooter_FollowsTheMessageDirectly_NoSeparatorOrQuotedBlock()
        {
            var html = Build(OtpMessage);

            const string lastLine = ">VISTAWAYS TECH Recruitment Team</p>";
            var messageEnd = html.IndexOf(lastLine, StringComparison.Ordinal) + lastLine.Length;
            var logo = html.IndexOf("<img", StringComparison.Ordinal);
            var legal = html.IndexOf("This email was sent to", StringComparison.Ordinal);
            Assert.True(messageEnd > lastLine.Length && messageEnd < logo && logo < legal);

            // nothing readable sits between the end of the message and the logo
            var between = System.Text.RegularExpressions.Regex.Replace(
                html.Substring(messageEnd, logo - messageEnd), "<[^>]*>", "").Trim();
            Assert.Equal("", between);
        }

        [Theory]
        [InlineData("blockquote")]
        [InlineData("gmail_quote")]
        [InlineData("<hr")]
        [InlineData("\n-- \n")]
        [InlineData("\n--\n")]
        public void Emails_ContainNothingGmailTreatsAsQuotedOrSignatureText(string marker)
        {
            Assert.DoesNotContain(marker, Build(OtpMessage), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ContactFormRelay_KeepsItsOriginalLayout()
        {
            var html = Build("Name: A\n\nMessage: Hi", footer: false);

            Assert.Contains("line-height:1.7", html);
            Assert.Contains("<pre style=\"white-space:pre-wrap;font-family:inherit;margin:0;\">Name: A\n\nMessage: Hi</pre>", html);
            Assert.DoesNotContain("cid:vwtech-logo", html);
        }

        // ── Assessment invitation (its own HTML layout) ────────────────────────

        [Fact]
        public void AssessmentInvitation_UsesTheSameSpacingAndFooterPlacement()
        {
            using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var emailOptions = Options.Create(new EmailOptions());
            var notifications = new NotificationService(db, config: null!, httpClientFactory: null!,
                emailOptions: emailOptions, logger: NullLogger<NotificationService>.Instance);
            var service = new AssessmentInvitationService(db, notifications, emailOptions);
            var candidate = new User { Id = 1, Username = "VWT1", CandidateId = "VWT1", Email = "c@example.com",
                PasswordHash = "x", FullName = "Test Candidate", Role = PortalRoles.Candidate };
            var exam = new Exam { Id = 1, Title = "Assessment", DurationMinutes = 20, TotalMarks = 10, PassingMarks = 5 };

            service.Issue(candidate, exam, "t", "https://portal/link", new[] { "Pending" });
            db.SaveChanges();
            var html = db.Notifications.Single(n => n.Channel == "Email").Body;

            Assert.Contains("font-size:14px;line-height:1.5;", html);
            Assert.DoesNotContain("line-height:1.6", html);
            Assert.DoesNotContain("margin:0 0 8px;\">You will log in", html);

            var contact = html.IndexOf("please contact our recruitment team", StringComparison.Ordinal);
            var logo = html.IndexOf("cid:vwtech-logo", StringComparison.Ordinal);
            var legal = html.IndexOf("All rights reserved.", StringComparison.Ordinal);
            Assert.True(contact > 0 && contact < logo && logo < legal);
            Assert.DoesNotContain("blockquote", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("gmail_quote", html, StringComparison.OrdinalIgnoreCase);
        }
    }
}
