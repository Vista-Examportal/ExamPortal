using ExamPortal.Models;

namespace ExamPortal.Services
{
    /// <summary>
    /// Per-account password lockout. State lives on the User row so it survives restarts
    /// and is shared by all app instances. This complements, not replaces, the per-IP
    /// "AuthSensitive" rate limit. Two independent counters exist:
    ///   - normal Login:    FailedLoginCount / LockoutEndUtc
    ///   - AssessmentAuth:  AssessmentFailedCount / AssessmentLockoutEndUtc
    /// Pure logic over the User entity; the caller is responsible for SaveChanges.
    /// </summary>
    public static class LoginLockout
    {
        public const int MaxFailedAttempts = 10;
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        // ── Normal password login ──

        /// <summary>True (with the time left) if the account is currently locked.</summary>
        public static bool IsLockedOut(User user, DateTime utcNow, out TimeSpan remaining) =>
            CheckLocked(user.LockoutEndUtc, utcNow, out remaining);

        /// <summary>Records one wrong password. Returns true if this attempt triggered a lockout.
        /// A lockout that has already expired starts the count over from zero.</summary>
        public static bool RegisterFailure(User user, DateTime utcNow)
        {
            var count = user.FailedLoginCount;
            var end = user.LockoutEndUtc;
            var locked = AddFailure(ref count, ref end, utcNow);
            user.FailedLoginCount = count;
            user.LockoutEndUtc = end;
            return locked;
        }

        /// <summary>Clears state after a successful password login. Returns true if anything changed.</summary>
        public static bool Reset(User user)
        {
            if (user.FailedLoginCount == 0 && user.LockoutEndUtc == null) return false;
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            return true;
        }

        // ── Assessment authentication (tracked separately) ──

        public static bool IsAssessmentLockedOut(User user, DateTime utcNow, out TimeSpan remaining) =>
            CheckLocked(user.AssessmentLockoutEndUtc, utcNow, out remaining);

        public static bool RegisterAssessmentFailure(User user, DateTime utcNow)
        {
            var count = user.AssessmentFailedCount;
            var end = user.AssessmentLockoutEndUtc;
            var locked = AddFailure(ref count, ref end, utcNow);
            user.AssessmentFailedCount = count;
            user.AssessmentLockoutEndUtc = end;
            return locked;
        }

        /// <summary>Clears only the assessment-auth counter; normal-login state is untouched.</summary>
        public static bool ResetAssessment(User user)
        {
            if (user.AssessmentFailedCount == 0 && user.AssessmentLockoutEndUtc == null) return false;
            user.AssessmentFailedCount = 0;
            user.AssessmentLockoutEndUtc = null;
            return true;
        }

        // ── Messages ──

        public static string Message(TimeSpan remaining) =>
            $"Too many failed sign-in attempts. This account is temporarily locked. Please try again in {Minutes(remaining)}.";

        /// <summary>Shown only to someone presenting this candidate's own invitation link.</summary>
        public static string AssessmentMessage(TimeSpan remaining) =>
            $"Too many failed attempts. Assessment sign-in is temporarily locked. Please try again in {Minutes(remaining)}.";

        // ── Shared core ──

        private static string Minutes(TimeSpan remaining)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
        }

        private static bool CheckLocked(DateTime? end, DateTime utcNow, out TimeSpan remaining)
        {
            if (end is { } e && e > utcNow)
            {
                remaining = e - utcNow;
                return true;
            }
            remaining = TimeSpan.Zero;
            return false;
        }

        private static bool AddFailure(ref int count, ref DateTime? end, DateTime utcNow)
        {
            if (end is { } e && e <= utcNow)
            {
                end = null;
                count = 0;
            }

            count++;
            if (count < MaxFailedAttempts) return false;

            end = utcNow + LockoutDuration;
            count = 0;
            return true;
        }
    }
}
