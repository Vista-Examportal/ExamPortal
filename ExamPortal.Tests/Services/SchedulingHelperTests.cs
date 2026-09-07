using System;
using System.Linq;
using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class SchedulingHelperTests
    {
        [Fact]
        public void ConvertWallClockToUtc_NullTimeZone_TreatsValueAsUtc()
        {
            var wallClock = new DateTime(2026, 1, 15, 10, 0, 0);

            var result = SchedulingHelper.ConvertWallClockToUtc(wallClock, null);

            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), result);
        }

        [Fact]
        public void ConvertWallClockToUtc_ExplicitUtc_TreatsValueAsUtc()
        {
            var wallClock = new DateTime(2026, 1, 15, 10, 0, 0);

            var result = SchedulingHelper.ConvertWallClockToUtc(wallClock, "UTC");

            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), result);
        }

        [Fact]
        public void ConvertWallClockToUtc_IndiaStandardTime_AppliesFixedOffset()
        {
            // Asia/Kolkata is UTC+5:30 year-round (no DST), so this is a safe fixed-offset
            // assertion regardless of which date is picked — a wall clock of 10:00 IST
            // should become 04:30 UTC the same day.
            var wallClock = new DateTime(2026, 1, 15, 10, 0, 0);

            var result = SchedulingHelper.ConvertWallClockToUtc(wallClock, "Asia/Kolkata");

            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(new DateTime(2026, 1, 15, 4, 30, 0, DateTimeKind.Utc), result);
        }

        [Fact]
        public void ConvertWallClockToUtc_UnrecognizedTimeZone_FallsBackToUtc()
        {
            // A bad/unknown zone id should never throw — it should behave like the old
            // (UTC-assuming) code did, per the method's own doc comment.
            var wallClock = new DateTime(2026, 1, 15, 10, 0, 0);

            var result = SchedulingHelper.ConvertWallClockToUtc(wallClock, "Not/ARealTimeZone");

            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), result);
        }

        [Fact]
        public void ConvertUtcToWallClock_NullTimeZone_ReturnsValueUnchanged()
        {
            var utc = new DateTime(2026, 1, 15, 4, 30, 0, DateTimeKind.Utc);

            var result = SchedulingHelper.ConvertUtcToWallClock(utc, null);

            Assert.Equal(DateTimeKind.Unspecified, result.Kind);
            Assert.Equal(new DateTime(2026, 1, 15, 4, 30, 0), result);
        }

        [Fact]
        public void ConvertUtcToWallClock_ExplicitUtc_ReturnsValueUnchanged()
        {
            var utc = new DateTime(2026, 1, 15, 4, 30, 0, DateTimeKind.Utc);

            var result = SchedulingHelper.ConvertUtcToWallClock(utc, "UTC");

            Assert.Equal(new DateTime(2026, 1, 15, 4, 30, 0), result);
        }

        [Fact]
        public void ConvertUtcToWallClock_IndiaStandardTime_AppliesFixedOffset()
        {
            // Inverse of the ConvertWallClockToUtc IST test above: 04:30 UTC should read as
            // 10:00 on an IST clock (UTC+5:30, no DST, so safe to hardcode regardless of date).
            var utc = new DateTime(2026, 1, 15, 4, 30, 0, DateTimeKind.Utc);

            var result = SchedulingHelper.ConvertUtcToWallClock(utc, "Asia/Kolkata");

            Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0), result);
        }

        [Fact]
        public void ConvertUtcToWallClock_UnrecognizedTimeZone_FallsBackToUtcValueUnchanged()
        {
            var utc = new DateTime(2026, 1, 15, 4, 30, 0, DateTimeKind.Utc);

            var result = SchedulingHelper.ConvertUtcToWallClock(utc, "Not/ARealZone");

            Assert.Equal(new DateTime(2026, 1, 15, 4, 30, 0), result);
        }

        [Fact]
        public void ConvertUtcToWallClock_RoundTripsWithConvertWallClockToUtc()
        {
            // The whole point of the inverse function: converting there and back through the
            // same zone should reproduce the original wall-clock value exactly.
            var original = new DateTime(2026, 6, 1, 9, 15, 0);

            var utc = SchedulingHelper.ConvertWallClockToUtc(original, "Asia/Kolkata");
            var roundTripped = SchedulingHelper.ConvertUtcToWallClock(utc, "Asia/Kolkata");

            Assert.Equal(original, roundTripped);
        }

        [Fact]
        public void FormatInterviewSchedule_DateDisplay_UsesExpectedFormat()
        {
            var scheduledAt = new DateTime(2026, 3, 10, 14, 30, 0);

            var (dateDisplay, _, _) = SchedulingHelper.FormatInterviewSchedule(scheduledAt, "Jane Doe", "");

            Assert.Equal(scheduledAt.ToString("dddd, dd MMMM yyyy 'at' HH:mm"), dateDisplay);
        }

        [Fact]
        public void FormatInterviewSchedule_EmptyInterviewerNames_FallsBackToPlaceholder()
        {
            var (_, interviewersLine, _) = SchedulingHelper.FormatInterviewSchedule(DateTime.UtcNow, "", "");

            Assert.Equal("To be confirmed", interviewersLine);
        }

        [Fact]
        public void FormatInterviewSchedule_WhitespaceInterviewerNames_FallsBackToPlaceholder()
        {
            var (_, interviewersLine, _) = SchedulingHelper.FormatInterviewSchedule(DateTime.UtcNow, "   ", "");

            Assert.Equal("To be confirmed", interviewersLine);
        }

        [Fact]
        public void FormatInterviewSchedule_NonEmptyInterviewerNames_PassesThrough()
        {
            var (_, interviewersLine, _) = SchedulingHelper.FormatInterviewSchedule(DateTime.UtcNow, "Jane Doe, John Smith", "");

            Assert.Equal("Jane Doe, John Smith", interviewersLine);
        }

        [Fact]
        public void FormatInterviewSchedule_EmptyPanelMembers_FallsBackToInterviewersLine()
        {
            var (_, interviewersLine, panelLine) = SchedulingHelper.FormatInterviewSchedule(DateTime.UtcNow, "Jane Doe", "");

            Assert.Equal(interviewersLine, panelLine);
            Assert.Equal("Jane Doe", panelLine);
        }

        [Fact]
        public void FormatInterviewSchedule_EmptyPanelAndInterviewers_BothFallBackToPlaceholder()
        {
            var (_, interviewersLine, panelLine) = SchedulingHelper.FormatInterviewSchedule(DateTime.UtcNow, "", "");

            Assert.Equal("To be confirmed", interviewersLine);
            Assert.Equal("To be confirmed", panelLine);
        }

        [Fact]
        public void FormatInterviewSchedule_NonEmptyPanelMembers_PassesThroughIndependently()
        {
            var (_, interviewersLine, panelLine) = SchedulingHelper.FormatInterviewSchedule(
                DateTime.UtcNow, "Jane Doe", "Panel: Jane Doe, Priya Rao, Alex Kim");

            Assert.Equal("Jane Doe", interviewersLine);
            Assert.Equal("Panel: Jane Doe, Priya Rao, Alex Kim", panelLine);
        }

        // ── FormatMeetingSection ────────────────────────────────────────────────

        [Fact]
        public void FormatMeetingSection_InPerson_ShowsLocation()
        {
            var result = SchedulingHelper.FormatMeetingSection("In-Person", "Building 4, Hyderabad", "");

            Assert.Contains("Location", result);
            Assert.Contains("Building 4, Hyderabad", result);
            Assert.DoesNotContain("Meeting Link", result);
        }

        [Fact]
        public void FormatMeetingSection_InPerson_NoLocation_ShowsPlaceholder()
        {
            var result = SchedulingHelper.FormatMeetingSection("In-Person", "", "");

            Assert.Contains("To be confirmed", result);
        }

        [Fact]
        public void FormatMeetingSection_Online_ShowsMeetingLinkAndId()
        {
            var result = SchedulingHelper.FormatMeetingSection("Online", "https://meet.example.com/abc", "123-456");

            Assert.Contains("Meeting Link", result);
            Assert.Contains("https://meet.example.com/abc", result);
            Assert.Contains("Meeting ID", result);
            Assert.Contains("123-456", result);
        }

        [Fact]
        public void FormatMeetingSection_Online_NoLink_ShowsPlaceholder()
        {
            var result = SchedulingHelper.FormatMeetingSection("Online", "", "");

            Assert.Contains("To be shared separately", result);
        }

        // ── BuildRescheduleEmailBody ────────────────────────────────────────────
        // Regression coverage for the restored "Interview Rescheduled" candidate email
        // (previously removed — see AdminController.RescheduleInterview).

        [Fact]
        public void BuildRescheduleEmailBody_ContainsNewScheduleDetails()
        {
            var scheduledAt = new DateTime(2026, 9, 1, 15, 0, 0);

            var body = SchedulingHelper.BuildRescheduleEmailBody(
                "Priya Rao", "VWT202600042", "Technical",
                scheduledAt, "Asia/Kolkata", 45, "Online",
                "https://meet.example.com/xyz", "999-000", "Jane Doe", "Jane Doe, Alex Kim", "");

            Assert.Contains("Priya Rao", body);
            Assert.Contains("VWT202600042", body);
            Assert.Contains("Technical", body);
            Assert.Contains(scheduledAt.ToString("dddd, dd MMMM yyyy 'at' HH:mm"), body);
            Assert.Contains("Asia/Kolkata", body);
            Assert.Contains("45 minutes", body);
            Assert.Contains("https://meet.example.com/xyz", body);
            Assert.Contains("999-000", body);
            Assert.Contains("Jane Doe, Alex Kim", body);
        }

        [Fact]
        public void BuildRescheduleEmailBody_NeverMentionsOldOrStaleWording()
        {
            // Every value passed in below is treated as the caller's "new" schedule —
            // this test just guards against the email ever reintroducing "previous"/"old"
            // framing, which would risk confusing a candidate about which time is current.
            var body = SchedulingHelper.BuildRescheduleEmailBody(
                "Priya Rao", "VWT202600042", "Technical",
                DateTime.UtcNow, "UTC", 30, "Online", "", "", "", "", "");

            Assert.DoesNotContain("previously", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("old details", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildRescheduleEmailBody_WithNotes_IncludesUpdatedInstructions()
        {
            var body = SchedulingHelper.BuildRescheduleEmailBody(
                "Priya Rao", "VWT202600042", "Technical",
                DateTime.UtcNow, "UTC", 30, "Online", "", "", "", "",
                "Please bring your laptop.");

            Assert.Contains("Updated Instructions", body);
            Assert.Contains("Please bring your laptop.", body);
        }

        [Fact]
        public void BuildRescheduleEmailBody_NoInternalEvaluationFields()
        {
            var body = SchedulingHelper.BuildRescheduleEmailBody(
                "Priya Rao", "VWT202600042", "Technical",
                DateTime.UtcNow, "UTC", 30, "Online", "", "", "", "", "");

            // The method signature itself has no Feedback/Rating/Remarks/Outcome
            // parameters, but this asserts the rendered text stays clear of those words
            // too, so a future edit can't quietly reintroduce them.
            Assert.DoesNotContain("Feedback", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Rating", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Score", body, StringComparison.OrdinalIgnoreCase);
        }

        // ── BuildCancellationEmailBody ──────────────────────────────────────────
        // Regression coverage for the restored "Interview Cancelled" candidate email
        // (previously removed — see AdminController.CancelInterview).

        [Fact]
        public void BuildCancellationEmailBody_ContainsCandidateAndRoundAndOldSchedule()
        {
            var previouslyScheduledAt = new DateTime(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc);

            var body = SchedulingHelper.BuildCancellationEmailBody("Priya Rao", "VWT202600042", "Technical", previouslyScheduledAt);

            Assert.Contains("Priya Rao", body);
            Assert.Contains("VWT202600042", body);
            Assert.Contains("Technical", body);
            Assert.Contains(previouslyScheduledAt.ToString("dddd, dd MMMM yyyy 'at' HH:mm"), body);
            Assert.Contains("cancelled", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildCancellationEmailBody_NullSchedule_StillProducesValidNeutralBody()
        {
            // InterviewRecord.ScheduledAt is a nullable DateTime? in the data model, so the
            // builder must tolerate a null previouslyScheduledAtUtc without throwing.
            var body = SchedulingHelper.BuildCancellationEmailBody("Priya Rao", "VWT202600042", "Technical", null);

            Assert.Contains("Priya Rao", body);
            Assert.Contains("Technical", body);
            Assert.Contains("cancelled", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildCancellationEmailBody_HasNoParameterForInternalReason()
        {
            // The method intentionally has no `reason`/`notes` parameter at all — this is
            // a compile-time guarantee that an internal cancellation reason can never leak
            // into the candidate email through this builder, regardless of what an admin
            // types into the (internal-only) "Cancellation Reason" field in the UI.
            var method = typeof(SchedulingHelper).GetMethod(nameof(SchedulingHelper.BuildCancellationEmailBody));
            var parameterNames = method!.GetParameters().Select(p => p.Name).ToArray();

            Assert.DoesNotContain("reason", parameterNames);
            Assert.DoesNotContain("notes", parameterNames);
            Assert.Equal(4, parameterNames.Length);
        }

        // ── IsValidWindow ────────────────────────────────────────────────────────
        // Covers AdminController.EditSchedule's inverted-window check (shared by both
        // Admin and Recruiter — see PortalRoles.Admin + "," + PortalRoles.Recruiter on
        // that action) now that it's extracted here instead of living inline in the
        // controller, so this rule is tested independently of a live HTTP request.

        [Fact]
        public void IsValidWindow_EndAfterStart_IsValid()
        {
            var start = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

            Assert.True(SchedulingHelper.IsValidWindow(start, end));
        }

        [Fact]
        public void IsValidWindow_EndBeforeStart_IsInvalid()
        {
            var start = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

            Assert.False(SchedulingHelper.IsValidWindow(start, end));
        }

        [Fact]
        public void IsValidWindow_EndEqualsStart_IsInvalid()
        {
            // A zero-length window would make IsAssessmentOpen() permanently false for it,
            // same as an inverted one — both must be rejected, not just a strictly negative gap.
            var same = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

            Assert.False(SchedulingHelper.IsValidWindow(same, same));
        }

        [Fact]
        public void IsValidWindow_NullStart_IsValid()
        {
            // Matches EditSchedule/CreateExam's own behavior: the inline check they used to
            // run was skipped entirely whenever either bound was unset, so an unset Start
            // must not be flagged as conflicting with a set End.
            var end = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

            Assert.True(SchedulingHelper.IsValidWindow(null, end));
        }

        [Fact]
        public void IsValidWindow_NullEnd_IsValid()
        {
            var start = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

            Assert.True(SchedulingHelper.IsValidWindow(start, null));
        }

        [Fact]
        public void IsValidWindow_BothNull_IsValid()
        {
            Assert.True(SchedulingHelper.IsValidWindow(null, null));
        }
    }
}
