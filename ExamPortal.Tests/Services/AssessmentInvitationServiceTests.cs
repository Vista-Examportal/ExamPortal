using System;
using System.Linq;
using System.Text.RegularExpressions;
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
    // Regression coverage for the assessment one-time login code: the sign-in page promises it
    // is "sent in the assessment email", so the email must carry it, while the database keeps
    // only a hash, the InApp copy never contains it, and a replacement invitation kills the old
    // code. Same lightweight setup as CandidateWorkflowServiceTests (real services, EF in-memory).
    public class AssessmentInvitationServiceTests
    {
        private static AppDbContext BuildContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static AssessmentInvitationService BuildService(AppDbContext db)
        {
            var emailOptions = Options.Create(new EmailOptions());
            var notifications = new NotificationService(
                db,
                config: null!,
                httpClientFactory: null!,
                emailOptions: emailOptions,
                logger: NullLogger<NotificationService>.Instance);
            return new AssessmentInvitationService(db, notifications, emailOptions);
        }

        private static User MakeCandidate(int id = 1) => new User
        {
            Id = id,
            Username = "VWT20260000" + id,
            CandidateId = "VWT20260000" + id,
            Email = $"candidate{id}@example.com",
            PasswordHash = "not-a-real-hash",
            FullName = "Test Candidate",
            Role = PortalRoles.Candidate,
        };

        private static Exam MakeExam() => new Exam
        {
            Id = 1,
            Title = "Frontend Developer Eligibility Assessment",
            DurationMinutes = 20,
            TotalMarks = 10,
            PassingMarks = 5,
        };

        private static string ExtractCode(string emailBody)
        {
            var match = Regex.Match(emailBody, @"<!--otp-->(\d{6})<!--/otp-->");
            Assert.True(match.Success, "Invitation email body should contain the one-time login code.");
            return match.Groups[1].Value;
        }

        [Fact]
        public void Issue_PutsCodeInEmailOnly_AndStoresOnlyItsHash()
        {
            using var db = BuildContext();
            var service = BuildService(db);
            var candidate = MakeCandidate();

            var invitation = service.Issue(candidate, MakeExam(), "link-token", "https://portal/Account/AssessmentAuth?token=link-token",
                statusesToRevoke: new[] { "Pending" });
            db.SaveChanges();

            var email = db.Notifications.Single(n => n.Channel == "Email");
            var inApp = db.Notifications.Single(n => n.Channel == "InApp");
            var code = ExtractCode(email.Body);

            // Email: labelled code + instructions are present.
            Assert.Contains("ONE-TIME LOGIN CODE", email.Body);
            Assert.Contains("One-Time Login Token", email.Body);

            // InApp copy (shown on dashboards) must never contain the code.
            Assert.DoesNotContain(code, inApp.Body);

            // Database keeps a hash, never the raw code.
            Assert.NotEqual(code, invitation.OneTimeLoginToken);
            Assert.Equal(SecureCodeGenerator.HashToken(code), invitation.OneTimeLoginToken);
            Assert.Equal(64, invitation.OneTimeLoginToken.Length);
        }

        [Fact]
        public void IsOneTimeCodeValid_AcceptsEmailedCode_RejectsWrongUsedAndLegacyValues()
        {
            using var db = BuildContext();
            var service = BuildService(db);
            var invitation = service.Issue(MakeCandidate(), MakeExam(), "t", "l", new[] { "Pending" });
            db.SaveChanges();
            var code = ExtractCode(db.Notifications.Single(n => n.Channel == "Email").Body);
            var wrong = code == "123456" ? "654321" : "123456";

            Assert.True(AssessmentInvitationService.IsOneTimeCodeValid(invitation, code));
            Assert.True(AssessmentInvitationService.IsOneTimeCodeValid(invitation, $"  {code} "));
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(invitation, wrong));
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(invitation, ""));
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(invitation, null));

            // Single use: once marked used, even the right code is refused.
            invitation.TokenUsedAt = DateTime.UtcNow;
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(invitation, code));

            // A legacy plaintext (or blank) stored value never matches anything.
            invitation.TokenUsedAt = null;
            invitation.OneTimeLoginToken = code;
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(invitation, code));
            invitation.OneTimeLoginToken = "";
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(invitation, code));
        }

        [Fact]
        public void Issue_ReplacementInvalidatesPreviousInvitationAndItsCode()
        {
            using var db = BuildContext();
            var service = BuildService(db);
            var candidate = MakeCandidate();
            var exam = MakeExam();

            var first = service.Issue(candidate, exam, "token-1", "l1", new[] { "Pending" });
            db.SaveChanges();
            var firstCode = ExtractCode(db.Notifications.Single(n => n.Channel == "Email").Body);
            Assert.True(AssessmentInvitationService.IsOneTimeCodeValid(first, firstCode));

            var second = service.Issue(candidate, exam, "token-2", "l2", new[] { "Pending" });
            db.SaveChanges();

            // Old invitation is revoked and its code can no longer match.
            Assert.Equal("Revoked", first.Status);
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(first, firstCode));

            // New invitation has its own, different hash bound to its own row.
            Assert.Equal("Pending", second.Status);
            Assert.NotEqual(first.Token, second.Token);
            Assert.Equal(2, db.Notifications.Count(n => n.Channel == "Email"));
            var secondEmail = db.Notifications.Where(n => n.Channel == "Email").OrderByDescending(n => n.Id).First();
            Assert.True(AssessmentInvitationService.IsOneTimeCodeValid(second, ExtractCode(secondEmail.Body)));
        }

        [Fact]
        public void Issue_AdminStyleReplacement_KillsCodeOfOlderAcceptedInvitationToo()
        {
            using var db = BuildContext();
            var service = BuildService(db);
            var candidate = MakeCandidate();
            var exam = MakeExam();

            var first = service.Issue(candidate, exam, "token-1", "l1", new[] { "Pending" });
            db.SaveChanges();
            var firstCode = ExtractCode(db.Notifications.Single(n => n.Channel == "Email").Body);
            first.Status = "Accepted"; // candidate already opened the link
            db.SaveChanges();

            // AdminController.SendInvitation only asks to revoke "Pending", so this one stays Accepted...
            service.Issue(candidate, exam, "token-2", "l2", new[] { "Pending" });
            db.SaveChanges();

            // ...but its one-time code is dead, and link-status behaviour is unchanged.
            Assert.Equal("Accepted", first.Status);
            Assert.False(AssessmentInvitationService.IsOneTimeCodeValid(first, firstCode));
        }

        [Fact]
        public void ScrubOneTimeCode_RemovesCodeButKeepsRestOfEmail()
        {
            using var db = BuildContext();
            var service = BuildService(db);
            service.Issue(MakeCandidate(), MakeExam(), "t", "https://portal/link", new[] { "Pending" });
            db.SaveChanges();
            var body = db.Notifications.Single(n => n.Channel == "Email").Body;
            var code = ExtractCode(body);

            var scrubbed = AssessmentInvitationService.ScrubOneTimeCode(body);

            Assert.DoesNotContain(code, scrubbed);
            Assert.Contains("ONE-TIME LOGIN CODE", scrubbed);
            Assert.Contains("https://portal/link", scrubbed);
        }
    }
}
