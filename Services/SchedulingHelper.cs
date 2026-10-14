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
            string candidateFullName, string candidateId, string round, DateTime? previouslyScheduledAtUtc,
            string? timeZoneId = null)
        {
            // With a saved zone, show the original time in that zone with its label (same shape
            // as the schedule/reschedule emails); without one, fall back to the UTC wording.
            var scheduleLine = !previouslyScheduledAtUtc.HasValue
                ? ""
                : string.IsNullOrWhiteSpace(timeZoneId)
                    ? $"previously scheduled for {previouslyScheduledAtUtc.Value:dddd, dd MMMM yyyy 'at' HH:mm} UTC, "
                    : $"previously scheduled for {FormatScheduledTimeWithZone(previouslyScheduledAtUtc, timeZoneId, "dddd, dd MMMM yyyy 'at' HH:mm")}, ";

            return
                $"Dear {candidateFullName} (ID: {candidateId}),\n\n" +
                $"Your {round} round interview with VISTAWAYS TECH, {scheduleLine}has been cancelled.\n\n" +
                $"Our recruitment team will be in touch if a new interview needs to be scheduled.\n\n" +
                $"If you have any questions, please reply to this email.\n\n" +
                $"Best regards,\n" +
                $"VISTAWAYS TECH Recruitment Team";
        }

        /// <summary>The application's established business time zone (the default offered on every
        /// scheduling form — see Views/Shared/_TimeZoneOptions.cshtml). Offer deadlines are
        /// date-only inputs, so they are interpreted in this zone.</summary>
        public const string BusinessTimeZoneId = "Asia/Kolkata";

        /// <summary>Short label shown next to business-time-zone values in the portal.</summary>
        public const string BusinessTimeZoneLabel = "IST";

        /// <summary>A stored UTC instant as business-time-zone wall-clock time.</summary>
        public static DateTime ToBusinessTime(DateTime utc) => ConvertUtcToWallClock(utc, BusinessTimeZoneId);

        /// <summary>The current business-time-zone calendar date (what "today" means to HR,
        /// recruiters and candidates) — not the server's date, which is UTC on most hosts.</summary>
        public static DateTime BusinessToday => ToBusinessTime(DateTime.UtcNow).Date;

        /// <summary>
        /// Earliest scheduled time that still counts as an "upcoming" interview: the start of
        /// today in the business time zone. Interviews earlier today stay listed (they are
        /// today's schedule); anything before today that is still "Scheduled" is overdue — it
        /// needs an outcome recorded, it is not upcoming. Used by both the HR and Recruiter
        /// dashboards so the two agree (they previously used a rolling 24 hours and no bound
        /// at all, respectively).
        /// </summary>
        public static DateTime UpcomingInterviewCutoffUtc => BusinessDayStartUtc(BusinessToday);

        /// <summary>The UTC instant at which the given business-time-zone calendar date begins,
        /// for translatable "that whole day" range queries.</summary>
        public static DateTime BusinessDayStartUtc(DateTime businessDate) =>
            ConvertWallClockToUtc(businessDate.Date, BusinessTimeZoneId);

        /// <summary>
        /// Portal display of a stored UTC instant in the business time zone. The label
        /// ("IST") is appended whenever the format shows a time of day, so a time is never
        /// shown unlabelled; date-only formats stay label-free. Replaces direct
        /// <c>.ToLocalTime()</c> calls, which rendered in whatever zone the web server
        /// happened to run in (UTC on most hosts) with no label.
        /// </summary>
        public static string FormatBusinessTime(DateTime? utc, string format = "dd MMM yyyy HH:mm")
        {
            if (!utc.HasValue) return "";
            var text = ToBusinessTime(utc.Value).ToString(format);
            return format.Contains('H') || format.Contains('h') ? $"{text} {BusinessTimeZoneLabel}" : text;
        }

        /// <summary>Email form of a UTC instant: UTC first, with the same moment in IST
        /// (identical to the invitation email's "Link Expires" / "Available From" rows).</summary>
        public static string FormatUtcWithIst(DateTime utc) =>
            $"{utc:dd MMM yyyy, HH:mm} UTC ({utc.AddMinutes(330):dd MMM yyyy, HH:mm} IST)";

        /// <summary>
        /// Converts the date-only "Offer Valid Until" value picked by HR into the true UTC instant
        /// at which the offer stops being acceptable: the END of that calendar day (23:59:59) in the
        /// business time zone. Previously the bare date (00:00) was stored and compared against
        /// DateTime.UtcNow, so the offer lapsed at the START of its displayed last date.
        /// </summary>
        public static DateTime ConvertOfferDateToDeadlineUtc(DateTime pickedDate) =>
            ConvertWallClockToUtc(pickedDate.Date.AddHours(23).AddMinutes(59).AddSeconds(59), BusinessTimeZoneId);

        /// <summary>
        /// Returns a stored OfferLetter.OfferValidUntil as a UTC instant. Values saved by
        /// ConvertOfferDateToDeadlineUtc are already instants (never exactly midnight, because
        /// 23:59:59 IST is 18:29:59 UTC). Rows saved before that change hold the raw picked date at
        /// exactly 00:00:00 — those are interpreted as a date-only deadline and normalised to the end
        /// of that day in the business time zone, so no data migration is needed.
        /// </summary>
        public static DateTime NormalizeOfferDeadlineUtc(DateTime stored) =>
            stored.TimeOfDay == TimeSpan.Zero
                ? ConvertOfferDateToDeadlineUtc(stored)
                : DateTime.SpecifyKind(stored, DateTimeKind.Utc);

        /// <summary>True once the offer deadline has passed. A null deadline never expires
        /// (unchanged from before).</summary>
        public static bool IsOfferExpired(DateTime? storedDeadline, DateTime utcNow) =>
            storedDeadline.HasValue && utcNow > NormalizeOfferDeadlineUtc(storedDeadline.Value);

        /// <summary>The offer deadline as business-time-zone wall-clock time (for display).</summary>
        public static DateTime OfferDeadlineWallClock(DateTime storedDeadline) =>
            ConvertUtcToWallClock(NormalizeOfferDeadlineUtc(storedDeadline), BusinessTimeZoneId);

        /// <summary>
        /// Formats a stored UTC instant as wall-clock time in the zone it was scheduled in, followed
        /// by that zone's label — the same "date/time (Zone)" shape the interview emails use
        /// (see ScheduleInterview / BuildRescheduleEmailBody). A blank or unrecognised zone is
        /// shown as UTC, which is how ConvertUtcToWallClock treats it.
        /// </summary>
        public static string FormatScheduledTimeWithZone(DateTime? utc, string? timeZoneId, string format = "dd MMM yyyy HH:mm")
        {
            if (!utc.HasValue) return "";
            var label = IsKnownTimeZone(timeZoneId) ? timeZoneId! : "UTC";
            return $"{ConvertUtcToWallClock(utc.Value, label).ToString(format)} ({label})";
        }

        private static bool IsKnownTimeZone(string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId)) return false;
            if (timeZoneId == "UTC") return true;
            try { TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); return true; }
            catch (TimeZoneNotFoundException) { return false; }
            catch (InvalidTimeZoneException) { return false; }
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

        /// <summary>How far in the past a Start may be and still count as "now". The picker has
        /// minute precision (it cannot express seconds), so a candidate start picked for the
        /// current minute is already up to 59 seconds old by the time it is posted.</summary>
        public const int StartTimeGraceMinutes = 1;

        /// <summary>True when a (UTC) start time is earlier than now, allowing
        /// <see cref="StartTimeGraceMinutes"/> of slack. An unset start is never "in the past".</summary>
        public static bool IsStartInPast(DateTime? startUtc, DateTime utcNow) =>
            startUtc.HasValue && startUtc.Value < utcNow.AddMinutes(-StartTimeGraceMinutes);

        /// <summary>
        /// Rule for a Start submitted when RESCHEDULING an existing assessment. A past start is
        /// refused, except when it is the start the assessment already has (compared to the
        /// minute): an assessment that is already running legitimately has a past start, and its
        /// End/Status must still be editable without touching that value.
        /// </summary>
        public static bool IsStartChangeAllowed(DateTime? newStartUtc, DateTime? existingStartUtc, DateTime utcNow)
        {
            if (!IsStartInPast(newStartUtc, utcNow)) return true;
            return existingStartUtc.HasValue
                && TruncateToMinute(newStartUtc!.Value) == TruncateToMinute(existingStartUtc.Value);
        }

        private static DateTime TruncateToMinute(DateTime value) =>
            new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerMinute));

        /// <summary>Unambiguous one-line description of a stored assessment window: each bound in
        /// the business time zone (labelled) with the same instant in UTC.</summary>
        public static string FormatScheduleWindow(DateTime? startUtc, DateTime? endUtc)
        {
            static string One(DateTime? utc) => utc.HasValue
                ? $"{FormatBusinessTime(utc, "dd MMM yyyy HH:mm")} ({utc.Value:dd MMM yyyy HH:mm} UTC)"
                : "not set";
            return $"{One(startUtc)} to {One(endUtc)}";
        }
    }
}
