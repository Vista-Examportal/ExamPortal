using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
// AdminController — interview scheduling & lifecycle: schedule, reschedule, cancel,
// complete, mark missed, and assign panel members. Split out of the single
// AdminController.cs for readability; same partial class as the other files.
    public partial class AdminController
    {

        // ── Step 11 – Schedule interview ─────────────────────────────────────

        [HttpPost]
        public IActionResult ScheduleInterview(
            int userId,
            string round,
            DateTime scheduledAt,
            string notes,
            string timeZone      = "UTC",
            int durationMinutes  = 60,
            string format        = "Online",
            string meetingLink   = "",
            string meetingId     = "",
            string interviewerNames    = "",
            string panelMembers        = "",
            string candidateActionItems = "")
        {
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            round = Clamp(round, 50);
            var normalizedRound = string.IsNullOrEmpty(round) ? "HR" : round;

            // Enforce the required interview-round sequence (Technical → Managerial → HR,
            // with Final as an optional add-on once those three have passed) — see
            // InterviewProgressionHelper. Reused for both the very first round scheduled
            // from the Shortlisted stage and any subsequent round.
            var existingInterviews = _db.InterviewRecords.Where(i => i.UserId == userId).ToList();
            if (!InterviewProgressionHelper.IsValidRoundToSchedule(existingInterviews, normalizedRound))
            {
                TempData["Error"] = $"Cannot schedule a {normalizedRound} interview yet — the required round sequence hasn't reached this stage.";
                return RedirectToAction("CandidateDetail", new { id = userId });
            }

            notes                = Clamp(notes, 1000);
            timeZone             = Clamp(timeZone, 100);
            format               = Clamp(format, 50);
            meetingLink          = Clamp(meetingLink, 500);
            meetingId            = Clamp(meetingId, 200);
            interviewerNames     = Clamp(interviewerNames, 500);
            panelMembers         = Clamp(panelMembers, 1000);
            candidateActionItems = Clamp(candidateActionItems, 1000);
            // The picker only offers positive values, but this action takes loose params
            // (not a bound ViewModel), so nothing stops a direct POST sending 0 or negative —
            // which would show as a nonsensical "ends before it starts" duration everywhere
            // this gets displayed (candidate dashboard, calendar-style views).
            durationMinutes      = Math.Max(1, durationMinutes);
            if (string.IsNullOrWhiteSpace(panelMembers))
                panelMembers = interviewerNames;

            // Store the true UTC instant (see ConvertWallClockToUtc) so later comparisons
            // against DateTime.UtcNow — the "upcoming interviews" dashboard filter — and
            // .ToLocalTime() displays are correct regardless of which time zone was selected.
            var scheduledAtUtc = SchedulingHelper.ConvertWallClockToUtc(scheduledAt, timeZone);

            _db.InterviewRecords.Add(new InterviewRecord
            {
                UserId               = userId,
                Round                = string.IsNullOrEmpty(round) ? "HR" : round,
                Status               = "Scheduled",
                Notes                = notes,
                ScheduledAt          = scheduledAtUtc,
                TimeZone             = timeZone,
                DurationMinutes      = durationMinutes,
                Format               = format,
                MeetingLink          = meetingLink,
                MeetingId            = meetingId,
                InterviewerNames     = interviewerNames,
                PanelMembers         = panelMembers,
                CandidateActionItems = candidateActionItems
            });

            user.RecruitmentStage = "InterviewInProgress";

            // ── Build rich interview invitation email ─────────────────────────
            var (dateDisplay, interviewersLine, panelLine) = SchedulingHelper.FormatInterviewSchedule(scheduledAt, interviewerNames, panelMembers);
            var actionItemsSection = string.IsNullOrWhiteSpace(candidateActionItems)
                ? ""
                : $"\n📋 ACTION ITEMS (please complete before the interview):\n{candidateActionItems}\n";

            var meetingSection = format == "In-Person"
                ? $"📍 Location: {(string.IsNullOrWhiteSpace(meetingLink) ? "To be confirmed" : meetingLink)}"
                : $"🔗 Meeting Link: {(string.IsNullOrWhiteSpace(meetingLink) ? "To be shared separately" : meetingLink)}" +
                  (string.IsNullOrWhiteSpace(meetingId) ? "" : $"\n🆔 Meeting ID:   {meetingId}");

            var emailBody =
                $"Dear {user.FullName} (ID: {user.CandidateId}),\n\n" +
                $"We are pleased to invite you for the {round} round interview at VISTAWAYS TECH, " +
                $"as the next step in our recruitment process.\n\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"  INTERVIEW DETAILS\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"📅 Date & Time : {dateDisplay} ({timeZone})\n" +
                $"⏱  Duration    : {durationMinutes} minutes\n" +
                $"🖥  Format      : {format}\n" +
                $"{meetingSection}\n" +
                $"👥 Interviewer(s): {interviewersLine}\n\n" +
                $"👥 Panel Members : {panelLine}\n\n" +
                actionItemsSection +
                (string.IsNullOrWhiteSpace(notes) ? "" : $"📝 Additional Notes:\n{notes}\n\n") +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                $"Please reply to this email to confirm your availability or to request a reschedule " +
                $"at least 24 hours in advance.\n\n" +
                $"We look forward to speaking with you.\n\n" +
                $"Best regards,\n" +
                $"VISTAWAYS TECH Recruitment Team";

            _notifications.Queue(user, null, "Interview Schedule",
                $"Interview Invitation — {round} Round — VISTAWAYS TECH",
                emailBody);

            _db.SaveChanges();

            TempData["Success"] = $"{round} interview scheduled for {user.FullName}.";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        [HttpPost]
        public IActionResult CompleteInterview(
            int interviewId,
            string outcome,
            string notes,
            string feedback = "",
            int? rating = null,
            string remarks = "",
            string candidateStatus = "")
        {
            var interview = _db.InterviewRecords.Find(interviewId);
            if (interview == null) return NotFound();

            outcome = Clamp(outcome, 50);
            notes   = Clamp(notes, 1000);
            feedback = Clamp(feedback, 2000);
            remarks = Clamp(remarks, 1000);
            var normalizedStatus = NormalizeCandidateStatus(candidateStatus);

            interview.Status      = "Completed";
            interview.Outcome     = outcome;
            interview.Notes       = string.IsNullOrEmpty(notes) ? interview.Notes : notes;
            interview.Feedback    = feedback;
            interview.Rating      = rating.HasValue ? Math.Clamp(rating.Value, 1, 5) : null;
            interview.Remarks     = remarks;
            interview.CandidateStatusAfterInterview = normalizedStatus;
            interview.UpdatedBy   = User.Identity?.Name ?? "Recruiter";
            interview.CompletedAt = DateTime.UtcNow;

            var user = _db.Users.Find(interview.UserId);
            if (user != null)
            {
                // Guard the one legacy dropdown option that could shortcut the round
                // sequence: "Request Documents" is only honored here if every required
                // round (Technical/Managerial/HR, plus Final if in use for this candidate)
                // has actually passed — see InterviewProgressionHelper. Any other
                // selection (Interview In Progress / Shortlisted / Rejected) doesn't skip
                // a round, so it's applied as before. `interview` already has this
                // request's new Status/Outcome applied above, so it's combined with the
                // candidate's other interview records rather than re-queried (which could
                // return a stale Outcome for this very round).
                if (normalizedStatus == RecruitmentStages.DocumentsRequested)
                {
                    var otherInterviews = _db.InterviewRecords
                        .Where(i => i.UserId == interview.UserId && i.Id != interview.Id).ToList();
                    var progress = InterviewProgressionHelper.GetProgress(otherInterviews.Append(interview));
                    if (!progress.DocumentsUnlocked)
                        normalizedStatus = ""; // ignore — round sequence isn't complete yet
                }

                if (!string.IsNullOrWhiteSpace(normalizedStatus))
                    user.RecruitmentStage = normalizedStatus;

                // CLEANUP: removed the "Interview Completed" candidate email per request.
                // The candidate's Application Status (normalizedStatus, set just above)
                // still updates immediately and remains visible on their dashboard — this
                // only removes the separate email/in-app notification confirming
                // attendance. No internal evaluation fields (Outcome/Feedback/Rating/
                // Remarks) were ever included in that email to begin with.
            }

            _db.SaveChanges();
            TempData["Success"] = $"Interview marked {outcome}.";
            return RedirectToAction("CandidateDetail", new { id = interview.UserId });
        }


        // ── Mark interview as Missed ──────────────────────────────────────────

        [HttpPost]
        public IActionResult MarkInterviewMissed(int interviewId, string notes)
        {
            var interview = _db.InterviewRecords.Find(interviewId);
            if (interview == null) return NotFound();

            notes = Clamp(notes, 1000);
            interview.Status = "Missed";
            interview.Notes  = string.IsNullOrEmpty(notes) ? interview.Notes : notes;
            interview.UpdatedBy = User.Identity?.Name ?? "Recruiter";

            // CLEANUP: removed the "Interview Missed" email. NOTE: this one carried a
            // 48-hour reschedule-request deadline with a real consequence (application
            // closure) — with the email gone, candidates have no direct notification of
            // that deadline unless they check their portal dashboard.

            _db.SaveChanges();
            TempData["Success"] = "Interview marked as Missed. (No email is sent — let the candidate know directly if the 48-hour reschedule deadline applies.)";
            return RedirectToAction("CandidateDetail", new { id = interview.UserId });
        }


        [HttpPost]
        public IActionResult CancelInterview(int interviewId, string notes = "", string candidateStatus = "")
        {
            var interview = _db.InterviewRecords.Find(interviewId);
            if (interview == null) return NotFound();

            notes = Clamp(notes, 1000);
            var normalizedStatus = NormalizeCandidateStatus(candidateStatus);
            interview.Status = "Cancelled";
            interview.Notes = string.IsNullOrWhiteSpace(notes) ? interview.Notes : notes;
            interview.CandidateStatusAfterInterview = normalizedStatus;
            interview.UpdatedBy = User.Identity?.Name ?? "Recruiter";
            interview.CancelledAt = DateTime.UtcNow;

            var user = _db.Users.Find(interview.UserId);
            if (user != null)
            {
                if (!string.IsNullOrWhiteSpace(normalizedStatus))
                    user.RecruitmentStage = normalizedStatus;

                // Notify the candidate that their interview was cancelled. Deliberately
                // neutral: the `notes` param captured above (labeled "Cancellation Reason"
                // in the admin UI) is an internal/free-text field an admin may use for
                // anything up to confidential internal context, so — per the "no
                // confidential/internal cancellation reasons in candidate emails" rule —
                // it is intentionally NOT passed into the email body below. The candidate
                // only learns that the interview was cancelled and that the recruitment
                // team will follow up. See SchedulingHelper.BuildCancellationEmailBody.
                _notifications.Queue(user, null, "Interview Cancelled",
                    $"Interview Cancelled — {interview.Round} Round — VISTAWAYS TECH",
                    SchedulingHelper.BuildCancellationEmailBody(user.FullName, user.CandidateId, interview.Round, interview.ScheduledAt));
            }

            _db.SaveChanges();
            TempData["Success"] = $"Interview cancelled. {user?.FullName} has been notified by email.";
            return RedirectToAction("CandidateDetail", new { id = interview.UserId });
        }


        [HttpPost]
        public IActionResult AssignInterviewPanel(int interviewId, string panelMembers, string interviewerNames = "", string remarks = "")
        {
            var interview = _db.InterviewRecords.Find(interviewId);
            if (interview == null) return NotFound();

            panelMembers = Clamp(panelMembers, 1000);
            interviewerNames = Clamp(interviewerNames, 500);
            remarks = Clamp(remarks, 1000);

            interview.PanelMembers = panelMembers;
            if (!string.IsNullOrWhiteSpace(interviewerNames))
                interview.InterviewerNames = interviewerNames;
            if (!string.IsNullOrWhiteSpace(remarks))
                interview.Remarks = remarks;
            interview.UpdatedBy = User.Identity?.Name ?? "Recruiter";

            // CLEANUP: removed the "Interview Panel Updated" email — who sits on the panel
            // is internal staffing detail, not something the candidate needs to act on.
            _db.SaveChanges();
            TempData["Success"] = "Interview panel updated.";
            return RedirectToAction("CandidateDetail", new { id = interview.UserId });
        }


        [HttpPost]
        public IActionResult RescheduleInterview(
            int interviewId,
            DateTime scheduledAt,
            string timeZone         = "UTC",
            int durationMinutes     = 60,
            string format           = "Online",
            string meetingLink      = "",
            string meetingId        = "",
            string interviewerNames = "",
            string panelMembers     = "",
            string notes            = "")
        {
            var interview = _db.InterviewRecords.Find(interviewId);
            if (interview == null) return NotFound();

            timeZone         = Clamp(timeZone, 100);
            format           = Clamp(format, 50);
            meetingLink      = Clamp(meetingLink, 500);
            meetingId        = Clamp(meetingId, 200);
            interviewerNames = Clamp(interviewerNames, 500);
            panelMembers     = Clamp(panelMembers, 1000);
            notes            = Clamp(notes, 1000);
            durationMinutes  = Math.Max(1, durationMinutes);
            if (string.IsNullOrWhiteSpace(panelMembers))
                panelMembers = string.IsNullOrWhiteSpace(interview.PanelMembers) ? interviewerNames : interview.PanelMembers;

            interview.Status           = "Scheduled";
            // Store the true UTC instant (see ConvertWallClockToUtc) — same reasoning as
            // ScheduleInterview above.
            interview.ScheduledAt      = SchedulingHelper.ConvertWallClockToUtc(scheduledAt, timeZone);
            interview.TimeZone         = timeZone;
            interview.DurationMinutes  = durationMinutes;
            interview.Format           = format;
            interview.MeetingLink      = meetingLink;
            interview.MeetingId        = meetingId;
            interview.InterviewerNames = interviewerNames;
            interview.PanelMembers     = panelMembers;
            // FIX: treat empty string as "no change" — only overwrite when caller supplies a value.
            if (!string.IsNullOrWhiteSpace(notes))
                interview.Notes = notes;
            interview.CompletedAt      = null;
            interview.CancelledAt      = null;
            interview.UpdatedBy        = User.Identity?.Name ?? "Recruiter";

            var user = _db.Users.Find(interview.UserId);
            if (user != null)
            {
                user.RecruitmentStage = "InterviewInProgress";

                // Notify the candidate with the NEW interview details — never the old
                // ones (every value passed below is the incoming parameter for this
                // request, not a value read back off `interview`). See
                // SchedulingHelper.BuildRescheduleEmailBody — it reuses the same
                // FormatInterviewSchedule/meeting-section building blocks as
                // ScheduleInterview above, so the candidate sees the same shape of
                // information for a reschedule as they did for the original invite.
                _notifications.Queue(user, null, "Interview Rescheduled",
                    $"Interview Rescheduled — {interview.Round} Round — VISTAWAYS TECH",
                    SchedulingHelper.BuildRescheduleEmailBody(
                        user.FullName, user.CandidateId, interview.Round,
                        scheduledAt, timeZone, durationMinutes, format,
                        meetingLink, meetingId, interviewerNames, panelMembers, notes));
            }

            _db.SaveChanges();
            TempData["Success"] = $"{interview.Round} interview rescheduled. {user?.FullName} has been notified by email.";
            return RedirectToAction("CandidateDetail", new { id = interview.UserId });
        }

    }
}
