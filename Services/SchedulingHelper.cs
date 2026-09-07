namespace ExamPortal.Services
{
    /// <summary>
    /// Shared wall-clock/time-zone and interview-schedule-email formatting helpers.
    /// Extracted from <c>AdminController</c> (where they were private statics) so they're
    /// available to any future controller/service that needs to schedule an exam or interview —
    /// e.g. if HR or Recruiter ever get their own dedicated scheduling endpoint instead of
    /// posting into AdminController — without being re-copied.
    /// </summary>
    public static class SchedulingHelper
    {
        /// <summary>
        /// Converts a wall-clock date/time — as picked from a plain datetime-local field, with
        /// no offset info of its own — into a true UTC instant, given the IANA time zone the
        /// admin/recruiter intended it to represent (e.g. "Asia/Kolkata" for IST). Storing the
        /// naive value directly, as earlier code did, meant every later `DateTime.UtcNow`
        /// comparison (dashboard "upcoming" filters, exam-open gating) silently assumed the
        /// value was already UTC, which is wrong whenever a non-UTC zone is selected.
        /// Falls back to treating the value as UTC if the zone id is missing/unrecognized,
        /// so a bad value never throws — it just behaves like the old (UTC-assuming) code did.
        /// </summary>
        public static DateTime ConvertWallClockToUtc(DateTime wallClock, string? timeZoneId)
        {
            var naive = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
            if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId == "UTC")
                return DateTime.SpecifyKind(naive, DateTimeKind.Utc);

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTimeToUtc(naive, tz);
            }
            catch (TimeZoneNotFoundException) { return DateTime.SpecifyKind(naive, DateTimeKind.Utc); }
            catch (InvalidTimeZoneException) { return DateTime.SpecifyKind(naive, DateTimeKind.Utc); }
        }

        /// <summary>
        /// The inverse of ConvertWallClockToUtc — given a stored UTC instant (e.g.
        /// Exam.StartDateTime/EndDateTime) and an IANA zone id, returns the wall-clock value an
        /// admin in that zone would read on their clock, for pre-filling an editable
        /// datetime-local field. Exam doesn't persist which zone was originally used to create
        /// it (only the resulting UTC instant), so the edit form has no way to reconstruct the
        /// exact original input — it shows/accepts times in whichever zone is currently
        /// selected (defaulting to Asia/Kolkata, same default as the create form) instead. Same
        /// safe fallback behavior as ConvertWallClockToUtc: a bad/missing zone id never throws,
        /// it just treats the value as already being in that zone's wall-clock (i.e. returns it
        /// unchanged), matching how a "UTC" selection behaves.
        /// </summary>
        public static DateTime ConvertUtcToWallClock(DateTime utc, string? timeZoneId)
        {
            var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId == "UTC")
                return DateTime.SpecifyKind(asUtc, DateTimeKind.Unspecified);

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(asUtc, tz), DateTimeKind.Unspecified);
            }
            catch (TimeZoneNotFoundException) { return DateTime.SpecifyKind(asUtc, DateTimeKind.Unspecified); }
            catch (InvalidTimeZoneException) { return DateTime.SpecifyKind(asUtc, DateTimeKind.Unspecified); }
        }

        /// <summary>
        /// Shared interview-email formatting used by ScheduleInterview/RescheduleInterview.
        /// Only covers the parts that were byte-for-byte identical between the two actions;
        /// the meeting-section formatting is left to each call site since it differs
        /// slightly (whitespace) between the two and merging it would change output text.
        /// </summary>
        public static (string DateDisplay, string InterviewersLine, string PanelLine) FormatInterviewSchedule(
            DateTime scheduledAt, string interviewerNames, string panelMembers)
        {
            var dateDisplay = scheduledAt.ToString("dddd, dd MMMM yyyy 'at' HH:mm");
            var interviewersLine = string.IsNullOrWhiteSpace(interviewerNames) ? "To be confirmed" : interviewerNames;
            var panelLine = string.IsNullOrWhiteSpace(panelMembers) ? interviewersLine : panelMembers;
            return (dateDisplay, interviewersLine, panelLine);
        }

        /// <summary>Shared "meeting link / location" line used by the schedule and
        /// reschedule candidate emails.</summary>
        public static string FormatMeetingSection(string format, string meetingLink, string meetingId) =>
            format == "In-Person"
                ? $"📍 Location: {(string.IsNullOrWhiteSpace(meetingLink) ? "To be confirmed" : meetingLink)}"
                : $"🔗 Meeting Link: {(string.IsNullOrWhiteSpace(meetingLink) ? "To be shared separately" : meetingLink)}" +
                  (string.IsNullOrWhiteSpace(meetingId) ? "" : $"\n🆔 Meeting ID:   {meetingId}");

        /// <summary>
        /// Candidate-facing "Interview Rescheduled" email body, used by
        /// AdminController.RescheduleInterview. Every value taken here is the NEW
        /// schedule the caller is applying — never a value read back off the stored
        /// InterviewRecord — so the candidate is never shown stale/old details.
        /// Contains no internal evaluation info (Feedback/Rating/Remarks/Outcome are
        /// never passed to this method).
        /// </summary>
        public static string BuildRescheduleEmailBody(
            string candidateFullName, string candidateId, string round,
            DateTime scheduledAt, string timeZone, int durationMinutes, string format,
            string meetingLink, string meetingId, string interviewerNames, string panelMembers, string notes)
        {
            var (dateDisplay, interviewersLine, panelLine) = FormatInterviewSchedule(scheduledAt, interviewerNames, panelMembers);
            var meetingSection = FormatMeetingSection(format, meetingLink, meetingId);
            // Candidate-facing "updated instructions" — the same role as ScheduleInterview's
            // own "Additional Notes" field, not an internal-only note.
            var updatedInstructionsSection = string.IsNullOrWhiteSpace(notes) ? "" : $"📝 Updated Instructions:\n{notes}\n\n";

            return
                $"Dear {candidateFullName} (ID: {candidateId}),\n\n" +
                $"Your {round} round interview with VISTAWAYS TECH has been rescheduled. " +
                $"Please note the new details below.\n\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"  UPDATED INTERVIEW DETAILS\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"📅 Date & Time : {dateDisplay} ({timeZone})\n" +
                $"⏱  Duration    : {durationMinutes} minutes\n" +
                $"🖥  Format      : {format}\n" +
                $"{meetingSection}\n" +
                $"👥 Interviewer(s): {interviewersLine}\n\n" +
                $"👥 Panel Members : {panelLine}\n\n" +
                updatedInstructionsSection +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                $"Please reply to this email to confirm your availability or to request a further " +
                $"reschedule at least 24 hours in advance.\n\n" +
                $"We look forward to speaking with you.\n\n" +
                $"Best regards,\n" +
                $"VISTAWAYS TECH Recruitment Team";
        }

        /// <summary>
        /// Candidate-facing "Interview Cancelled" email body, used by
        /// AdminController.CancelInterview. Deliberately takes no `notes`/reason
        /// parameter — the admin-entered cancellation reason is internal-only and must
        /// never be included in a candidate email (it may contain confidential context,
        /// e.g. internal evaluation notes).
        /// </summary>
        public static string BuildCancellationEmailBody(
            string candidateFullName, string candidateId, string round, DateTime? previouslyScheduledAtUtc)
        {
            var scheduleLine = previouslyScheduledAtUtc.HasValue
                ? $"previously scheduled for {previouslyScheduledAtUtc.Value:dddd, dd MMMM yyyy 'at' HH:mm} UTC, "
                : "";

            return
                $"Dear {candidateFullName} (ID: {candidateId}),\n\n" +
                $"Your {round} round interview with VISTAWAYS TECH, {scheduleLine}has been cancelled.\n\n" +
                $"Our recruitment team will be in touch if a new interview needs to be scheduled.\n\n" +
                $"If you have any questions, please reply to this email.\n\n" +
                $"Best regards,\n" +
                $"VISTAWAYS TECH Recruitment Team";
        }

        /// <summary>
        /// True if an assessment's Start/End window is a valid (non-inverted, non-zero-length)
        /// range. Either side being null is treated as valid — an unset bound doesn't conflict
        /// with anything, matching how CreateExam/EditSchedule already skip this check when
        /// Start or End hasn't been provided. Used by AdminController.EditSchedule (shared by
        /// both Admin and Recruiter — see PortalRoles.Admin + "," + PortalRoles.Recruiter on
        /// that action) so this one rule can't drift between the two callers, and so it's
        /// covered by SchedulingHelperTests instead of only being reachable through a live
        /// controller request.
        /// </summary>
        public static bool IsValidWindow(DateTime? startUtc, DateTime? endUtc) =>
            !startUtc.HasValue || !endUtc.HasValue || endUtc.Value > startUtc.Value;
    }
}
