using System;
using System.Linq;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class LoginLockoutTests
    {
        private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static User NewUser() => new() { Id = 1, Username = "u", Email = "u@example.com", PasswordHash = "x" };

        [Fact]
        public void NineFailures_DoNotLock_TenthLocksForFifteenMinutes()
        {
            var user = NewUser();
            for (var i = 0; i < 9; i++)
                Assert.False(LoginLockout.RegisterFailure(user, Now));
            Assert.False(LoginLockout.IsLockedOut(user, Now, out _));

            Assert.True(LoginLockout.RegisterFailure(user, Now));
            Assert.True(LoginLockout.IsLockedOut(user, Now, out var remaining));
            Assert.Equal(TimeSpan.FromMinutes(15), remaining);
        }

        [Fact]
        public void LockoutExpires_AndCountStartsOver()
        {
            var user = NewUser();
            for (var i = 0; i < 10; i++) LoginLockout.RegisterFailure(user, Now);

            var later = Now.AddMinutes(15).AddSeconds(1);
            Assert.False(LoginLockout.IsLockedOut(user, later, out _));

            // After expiry a full 10 fresh failures are required to lock again.
            for (var i = 0; i < 9; i++) Assert.False(LoginLockout.RegisterFailure(user, later));
            Assert.True(LoginLockout.RegisterFailure(user, later));
        }

        [Fact]
        public void Reset_ClearsCountSoStreakIsConsecutiveOnly()
        {
            var user = NewUser();
            for (var i = 0; i < 9; i++) LoginLockout.RegisterFailure(user, Now);

            Assert.True(LoginLockout.Reset(user));
            Assert.Equal(0, user.FailedLoginCount);
            Assert.False(LoginLockout.Reset(user)); // nothing left to change

            for (var i = 0; i < 9; i++) Assert.False(LoginLockout.RegisterFailure(user, Now));
        }

        [Fact]
        public void Message_ShowsRemainingMinutes_RoundedUp()
        {
            Assert.Contains("15 minutes", LoginLockout.Message(TimeSpan.FromMinutes(15)));
            Assert.Contains("1 minute.", LoginLockout.Message(TimeSpan.FromSeconds(20)));
            Assert.Contains("3 minutes", LoginLockout.Message(TimeSpan.FromSeconds(121)));
        }

        [Fact]
        public void LockoutState_IsPersistedOnTheUserRow()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

            using (var db = new AppDbContext(options))
            {
                var user = NewUser();
                db.Users.Add(user);
                for (var i = 0; i < 10; i++) LoginLockout.RegisterFailure(user, Now);
                db.SaveChanges();
            }

            using (var db = new AppDbContext(options))
            {
                var user = db.Users.Single();
                Assert.True(LoginLockout.IsLockedOut(user, Now.AddMinutes(1), out var remaining));
                Assert.Equal(TimeSpan.FromMinutes(14), remaining);
            }
        }

        // ── AssessmentAuth counter (tracked separately from normal login) ──

        [Fact]
        public void Assessment_TenthFailureLocksForFifteenMinutes_NinthDoesNot()
        {
            var user = NewUser();
            for (var i = 0; i < 9; i++)
                Assert.False(LoginLockout.RegisterAssessmentFailure(user, Now));
            Assert.False(LoginLockout.IsAssessmentLockedOut(user, Now, out _));

            Assert.True(LoginLockout.RegisterAssessmentFailure(user, Now));
            Assert.True(LoginLockout.IsAssessmentLockedOut(user, Now, out var remaining));
            Assert.Equal(TimeSpan.FromMinutes(15), remaining);
            Assert.False(LoginLockout.IsAssessmentLockedOut(user, Now.AddMinutes(15).AddSeconds(1), out _));
        }

        [Fact]
        public void Assessment_AndLoginCounters_AreIndependent()
        {
            var user = NewUser();
            for (var i = 0; i < 10; i++) LoginLockout.RegisterAssessmentFailure(user, Now);

            // Assessment lockout does not lock normal login, and its failures don't count toward it.
            Assert.False(LoginLockout.IsLockedOut(user, Now, out _));
            Assert.Equal(0, user.FailedLoginCount);

            for (var i = 0; i < 5; i++) LoginLockout.RegisterFailure(user, Now);
            Assert.Equal(5, user.FailedLoginCount);

            // A successful assessment auth clears only the assessment state.
            Assert.True(LoginLockout.ResetAssessment(user));
            Assert.False(LoginLockout.IsAssessmentLockedOut(user, Now, out _));
            Assert.Equal(5, user.FailedLoginCount);

            // And a successful normal login clears only the login state.
            for (var i = 0; i < 3; i++) LoginLockout.RegisterAssessmentFailure(user, Now);
            LoginLockout.Reset(user);
            Assert.Equal(0, user.FailedLoginCount);
            Assert.Equal(3, user.AssessmentFailedCount);
        }

        [Fact]
        public void Assessment_LockedStateIsPersistedAndMessageIsGeneric()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

            using (var db = new AppDbContext(options))
            {
                var user = NewUser();
                db.Users.Add(user);
                for (var i = 0; i < 10; i++) LoginLockout.RegisterAssessmentFailure(user, Now);
                db.SaveChanges();
            }

            using (var db = new AppDbContext(options))
            {
                var user = db.Users.Single();
                Assert.True(LoginLockout.IsAssessmentLockedOut(user, Now.AddMinutes(1), out var remaining));
                Assert.False(LoginLockout.IsLockedOut(user, Now.AddMinutes(1), out _));
                var message = LoginLockout.AssessmentMessage(remaining);
                Assert.Contains("14 minutes", message);
                Assert.DoesNotContain(user.Email, message);
            }
        }
    }
}
