using System;
using System.Collections.Generic;
using ExamPortal.Models;
using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class InterviewProgressionHelperTests
    {
        private static InterviewRecord MakeInterview(int id, string round, string status, string outcome = "") =>
            new()
            {
                Id = id,
                UserId = 1,
                Round = round,
                Status = status,
                Outcome = outcome,
            };

        // ── No interviews yet ───────────────────────────────────────────────────

        [Fact]
        public void GetProgress_NoInterviews_NextRoundIsTechnical()
        {
            var progress = InterviewProgressionHelper.GetProgress(new List<InterviewRecord>());

            Assert.Equal("Technical", progress.NextRoundToSchedule);
            Assert.Null(progress.RoundInProgress);
            Assert.Null(progress.FailedRound);
            Assert.False(progress.DocumentsUnlocked);
            Assert.False(progress.CanOptionallyScheduleFinal);
            Assert.Equal(new[] { "Technical", "Managerial", "HR" }, progress.RequiredRounds);
        }

        [Fact]
        public void IsValidRoundToSchedule_NoInterviews_OnlyTechnicalIsValid()
        {
            var interviews = new List<InterviewRecord>();

            Assert.True(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "Technical"));
            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "Managerial"));
            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "HR"));
            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "Final"));
        }

        // ── TEST 1: Technical PASS → Managerial available, Documents still locked ──

        [Fact]
        public void GetProgress_TechnicalPassed_ManagerialNextAndDocumentsLocked()
        {
            var interviews = new List<InterviewRecord> { MakeInterview(1, "Technical", "Completed", "Passed") };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("Managerial", progress.NextRoundToSchedule);
            Assert.False(progress.DocumentsUnlocked);
            Assert.Null(progress.FailedRound);
        }

        // ── TEST 2: Technical FAIL → nothing further can be scheduled ──────────────

        [Fact]
        public void GetProgress_TechnicalFailed_BlocksManagerialHrAndDocuments()
        {
            var interviews = new List<InterviewRecord> { MakeInterview(1, "Technical", "Completed", "Failed") };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("Technical", progress.FailedRound);
            Assert.Null(progress.NextRoundToSchedule);
            Assert.False(progress.DocumentsUnlocked);
            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "Managerial"));
            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "HR"));
        }

        // ── TEST 3: Technical PASS → Managerial PASS → HR available, Documents locked ──

        [Fact]
        public void GetProgress_TechnicalAndManagerialPassed_HrNextAndDocumentsLocked()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Passed"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("HR", progress.NextRoundToSchedule);
            Assert.False(progress.DocumentsUnlocked);
        }

        // ── TEST 4: Managerial FAIL → HR cannot be scheduled, documents cannot be requested ──

        [Fact]
        public void GetProgress_ManagerialFailed_BlocksHrAndDocuments()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Failed"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("Managerial", progress.FailedRound);
            Assert.Null(progress.NextRoundToSchedule);
            Assert.False(progress.DocumentsUnlocked);
            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "HR"));
        }

        // ── TEST 5: Technical PASS → Managerial PASS → HR PASS → Documents unlocked ──

        [Fact]
        public void GetProgress_AllThreeRoundsPassed_DocumentsUnlocked()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Passed"),
                MakeInterview(3, "HR", "Completed", "Passed"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.True(progress.DocumentsUnlocked);
            Assert.Null(progress.NextRoundToSchedule);
            Assert.Null(progress.FailedRound);
            Assert.True(progress.CanOptionallyScheduleFinal);
        }

        // ── TEST 6: Technical PASS → Managerial PASS → HR FAIL → Documents stay locked ──

        [Fact]
        public void GetProgress_HrFailed_DocumentsStayLocked()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Passed"),
                MakeInterview(3, "HR", "Completed", "Failed"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("HR", progress.FailedRound);
            Assert.False(progress.DocumentsUnlocked);
        }

        // ── TEST 7: Final configured/required → Documents unlock only after Final PASS ──

        [Fact]
        public void GetProgress_FinalScheduled_BecomesPartOfRequiredSequence()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Passed"),
                MakeInterview(3, "HR", "Completed", "Passed"),
                MakeInterview(4, "Final", "Scheduled"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal(new[] { "Technical", "Managerial", "HR", "Final" }, progress.RequiredRounds);
            Assert.Equal("Final", progress.RoundInProgress);
            Assert.False(progress.DocumentsUnlocked);
        }

        [Fact]
        public void GetProgress_FinalPassed_DocumentsUnlocked()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Passed"),
                MakeInterview(3, "HR", "Completed", "Passed"),
                MakeInterview(4, "Final", "Completed", "Passed"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.True(progress.DocumentsUnlocked);
        }

        [Fact]
        public void IsValidRoundToSchedule_FinalOnlyValidOnceBaseThreePassed()
        {
            var beforeAllPassed = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
            };
            var afterAllPassed = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Passed"),
                MakeInterview(3, "HR", "Completed", "Passed"),
            };

            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(beforeAllPassed, "Final"));
            Assert.True(InterviewProgressionHelper.IsValidRoundToSchedule(afterAllPassed, "Final"));
        }

        // ── TEST 8 / TEST 9: cannot skip ahead to HR or to Documents after Technical PASS ──

        [Fact]
        public void IsValidRoundToSchedule_TechnicalPassed_CannotJumpToHr()
        {
            var interviews = new List<InterviewRecord> { MakeInterview(1, "Technical", "Completed", "Passed") };

            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "HR"));
        }

        [Fact]
        public void GetProgress_TechnicalPassedOnly_DocumentsNotUnlocked()
        {
            var interviews = new List<InterviewRecord> { MakeInterview(1, "Technical", "Completed", "Passed") };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.False(progress.DocumentsUnlocked);
        }

        // ── TEST 10 / TEST 11: a Scheduled/Missed/Cancelled round blocks the next one ──

        [Fact]
        public void GetProgress_ManagerialScheduled_BlocksHrFromBeingNext()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Scheduled"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("Managerial", progress.RoundInProgress);
            Assert.Null(progress.NextRoundToSchedule);
            Assert.False(InterviewProgressionHelper.IsValidRoundToSchedule(interviews, "HR"));
        }

        [Fact]
        public void GetProgress_HrCancelled_AwaitingRescheduleAndDoesNotAdvance()
        {
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Passed"),
                MakeInterview(2, "Managerial", "Completed", "Passed"),
                MakeInterview(3, "HR", "Cancelled"),
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("HR", progress.RoundAwaitingReschedule);
            Assert.Null(progress.NextRoundToSchedule);
            Assert.False(progress.DocumentsUnlocked);
        }

        [Fact]
        public void GetProgress_TechnicalMissed_AwaitingReschedule()
        {
            var interviews = new List<InterviewRecord> { MakeInterview(1, "Technical", "Missed") };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("Technical", progress.RoundAwaitingReschedule);
            Assert.False(progress.DocumentsUnlocked);
        }

        // ── Backward compatibility: irregular/legacy data shouldn't crash or misbehave ──

        [Fact]
        public void GetProgress_LegacyOutOfOrderData_DoesNotThrowAndReportsSensibly()
        {
            // e.g. a candidate scheduled straight into HR before this fix existed, with
            // Managerial never touched at all.
            var interviews = new List<InterviewRecord> { MakeInterview(1, "HR", "Scheduled") };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Equal("Technical", progress.NextRoundToSchedule); // Technical was never even started
            Assert.False(progress.DocumentsUnlocked);
        }

        [Fact]
        public void GetProgress_DuplicateRecordsForSameRound_UsesMostRecentById()
        {
            // Defensive: even though normal operation creates at most one InterviewRecord
            // per round, a stray duplicate shouldn't produce an inconsistent result.
            var interviews = new List<InterviewRecord>
            {
                MakeInterview(1, "Technical", "Completed", "Failed"),
                MakeInterview(2, "Technical", "Completed", "Passed"), // later/authoritative record
            };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Null(progress.FailedRound);
            Assert.Equal("Managerial", progress.NextRoundToSchedule);
        }

        // ── On Hold outcome: not a pass, not a fail — blocks progress without rejecting ──

        [Fact]
        public void GetProgress_OnHoldOutcome_BlocksNextRoundWithoutFailing()
        {
            var interviews = new List<InterviewRecord> { MakeInterview(1, "Technical", "Completed", "On Hold") };

            var progress = InterviewProgressionHelper.GetProgress(interviews);

            Assert.Null(progress.FailedRound);
            Assert.Equal("Technical", progress.RoundInProgress);
            Assert.Null(progress.NextRoundToSchedule);
            Assert.False(progress.DocumentsUnlocked);
        }
    }
}
