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
            Assert.Contains("Subject:</td>", html);          // internal relay keeps its From/To/Date/Subject box
            Assert.DoesNotContain("display:none", html);     // ...and has no preview text
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

        // ── Header box, preview text, links, plain text, logo file ────────────

        [Fact]
        public void CandidateEmails_DropTheFromToDateSubjectBox()
        {
            var html = Build("Dear A,\n\nHello.");

            Assert.DoesNotContain("From:</td>", html);
            Assert.DoesNotContain("Date:</td>", html);
            Assert.DoesNotContain("Subject:</td>", html);
            Assert.Contains("This email was sent to", html); // recipient-specific line stays
        }

        [Fact]
        public void OtpEmail_HasPreviewTextWithCodeAndExpiry_BeforeAnyVisibleContent()
        {
            var html = Build(OtpMessage);

            var hidden = html.IndexOf("display:none", StringComparison.Ordinal);
            var preview = html.IndexOf("Your email verification OTP is 123456. This OTP expires in 15 minutes.", StringComparison.Ordinal);
            var header = html.IndexOf("<h1", StringComparison.Ordinal);
            Assert.True(hidden > 0 && hidden < preview && preview < header);
        }

        [Fact]
        public void PreviewText_SkipsGreeting_AndIsShortened()
        {
            var longText = new string('x', 300);
            var html = Build("Dear A,\n\n" + longText);

            Assert.Contains(new string('x', 110) + "…", html);
            Assert.DoesNotContain(new string('x', 111) + "…", html);
            Assert.DoesNotMatch("display:none[^>]*>Dear A", html);
        }

        [Fact]
        public void LinkOnItsOwnLine_BecomesAButton_WithTheUrlStillVisible()
        {
            const string url = "https://portal.example.com/Account/ResetPassword?token=abc&email=a%40b.com";
            var html = Build("Dear A,\n\nClick the link below.\n\n" + url + "\n\nBye");

            var encoded = System.Net.WebUtility.HtmlEncode(url);
            Assert.Contains($"<a href=\"{encoded}\" style=\"display:inline-block;", html);
            Assert.Contains(">Reset password</a>", html);
            Assert.Contains($">{encoded}</a></p>", html);                 // full URL visible and clickable
            Assert.Contains(">Click the link below.</p>", html);          // surrounding wording untouched
        }

        [Fact]
        public void UrlsInsideAParagraph_AreLinked_ButOtherSchemesAreNot()
        {
            var html = Build("Meeting Link: https://meet.example.com/abc, see you.\njavascript:alert(1)");

            Assert.Contains("<a href=\"https://meet.example.com/abc\"", html);
            Assert.Contains(">https://meet.example.com/abc</a>, see you.", html); // trailing comma stays outside the link
            Assert.DoesNotContain("href=\"javascript", html);
        }

        [Fact]
        public void PlainTextAlternative_ForMessages_HasSameWordingAndTheCompanyFooter()
        {
            var text = EmailPlainText.FromMessage(OtpMessage, "candidate@example.com");

            Assert.Contains("Your email verification OTP is 123456.", text);
            Assert.Contains("This OTP expires in 15 minutes.", text);
            Assert.Contains("VISTAWAYSTECH LLP", text);
            Assert.Contains("Opp. JNTU, KPHB, Hyderabad-500072", text);
            Assert.Contains("www.vistawaystech.com", text);
            Assert.Contains("Email: info@vistawaystech.com", text);
            Assert.Contains("This email was sent to candidate@example.com", text);
            Assert.DoesNotContain("<", text);
            Assert.Contains("\r\n", text);
        }

        private static (string Html, AssessmentInvitation Invitation) BuildInvitation()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var emailOptions = Options.Create(new EmailOptions());
            var notifications = new NotificationService(db, config: null!, httpClientFactory: null!,
                emailOptions: emailOptions, logger: NullLogger<NotificationService>.Instance);
            var service = new AssessmentInvitationService(db, notifications, emailOptions);
            var candidate = new User { Id = 1, Username = "VWT1", CandidateId = "VWT1", Email = "c@example.com",
                PasswordHash = "x", FullName = "Test Candidate", Role = PortalRoles.Candidate };
            var exam = new Exam { Id = 1, Title = "Assessment", DurationMinutes = 20, TotalMarks = 10, PassingMarks = 5 };
            var invitation = service.Issue(candidate, exam, "t", "https://portal/link", new[] { "Pending" });
            db.SaveChanges();
            return (db.Notifications.Single(n => n.Channel == "Email").Body, invitation);
        }

        [Fact]
        public void PlainTextAlternative_ForTheInvitation_KeepsTheCodeAndLink_AndHasNoMarkup()
        {
            var (html, _) = BuildInvitation();
            var code = System.Text.RegularExpressions.Regex.Match(html, @"<!--otp-->(\d{6})<!--/otp-->").Groups[1].Value;

            var text = EmailPlainText.FromHtml(html);

            Assert.Contains(code, text);
            Assert.Contains("ONE-TIME LOGIN CODE", text);
            Assert.Contains("https://portal/link", text);
            Assert.Contains("VISTAWAYSTECH LLP", text);
            Assert.Contains("Opp. JNTU, KPHB, Hyderabad-500072", text);
            Assert.DoesNotContain("<", text);
            Assert.DoesNotContain("&nbsp;", text);
            Assert.DoesNotContain("otp-->", text);
        }

        [Fact]
        public void Invitation_ShowsExpiryInUtcAndIst_AndHasPreviewTextWithoutTheCode()
        {
            var (html, invitation) = BuildInvitation();
            var code = System.Text.RegularExpressions.Regex.Match(html, @"<!--otp-->(\d{6})<!--/otp-->").Groups[1].Value;

            var expected = $"{invitation.ExpiresAt:dd MMM yyyy, HH:mm} UTC ({invitation.ExpiresAt.AddMinutes(330):dd MMM yyyy, HH:mm} IST)";
            Assert.Contains(expected, html);

            var hidden = html.IndexOf("display:none", StringComparison.Ordinal);
            var header = html.IndexOf("<!-- Header -->", StringComparison.Ordinal);
            Assert.True(hidden > 0 && hidden < header);
            Assert.True(html.IndexOf(code, StringComparison.Ordinal) > header, "the one-time code must not appear in the preview text");
        }

        [Fact]
        public void EmailLogo_IsTheSmallCopy_AndExistsInWwwroot()
        {
            Assert.Equal("logo-email.png", EmailFooter.LogoFileName);

            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "ExamPortal.csproj"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var file = new System.IO.FileInfo(System.IO.Path.Combine(dir!.FullName, "wwwroot", EmailFooter.LogoFileName));
            Assert.True(file.Exists, "wwwroot/logo-email.png is missing");
            Assert.InRange(file.Length, 1_000, 100_000);
        }
    }
}
