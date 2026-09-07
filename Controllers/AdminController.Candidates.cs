using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
// AdminController — candidate review & pipeline actions: profile review, assessment
// invitations, shortlisting, and rejection. Split out of the single AdminController.cs
// for readability; this is still the same partial class (same fields, same DB context)
// as the other AdminController.*.cs files — nothing here changes behavior or access.
    public partial class AdminController
    {

        // ── Recruitment Pipeline ──────────────────────────────────────────────

        public IActionResult Pipeline()
        {
            var students = _db.Users.AsNoTracking().Include(u => u.EducationRecords).Where(u => u.Role == PortalRoles.Candidate).ToList();
            var vm = new AdminPipelineViewModel
            {
                Registered          = students.Where(u => u.RecruitmentStage == "Registered").ToList(),
                UnderReview         = students.Where(u => u.RecruitmentStage == "ProfileUnderReview").ToList(),
                InvitationSent      = students.Where(u => u.RecruitmentStage == "InvitationSent").ToList(),
                AssessmentCompleted = students.Where(u => u.RecruitmentStage is "AssessmentCompleted" or "AssessmentStarted").ToList(),
                Shortlisted         = students.Where(u => u.RecruitmentStage == "Shortlisted").ToList(),
                InterviewInProgress = students.Where(u => u.RecruitmentStage == "InterviewInProgress").ToList(),
                DocumentsRequested  = students.Where(u => u.RecruitmentStage == "DocumentsRequested").ToList(),
                DocumentsVerified   = students.Where(u => u.RecruitmentStage == "DocumentsVerified").ToList(),
                OfferIssued         = students.Where(u => u.RecruitmentStage == "OfferIssued").ToList(),
                OfferAccepted       = students.Where(u => u.RecruitmentStage is "OfferAccepted" or "Onboarding").ToList(),
                Onboarding          = students.Where(u => u.RecruitmentStage == "Onboarding").ToList(),
                Exams = _db.Exams.AsNoTracking().Where(e => e.IsActive).ToList()
            };
            return View(vm);
        }


        // ── Candidate Detail ──────────────────────────────────────────────────

        public IActionResult CandidateDetail(int id)
        {
            var candidate = _db.Users.AsNoTracking().Include(u => u.CandidateProfile).Include(u => u.EducationRecords).Include(u => u.Address).FirstOrDefault(u => u.Id == id && u.Role == PortalRoles.Candidate);
            if (candidate == null) return NotFound();

            var vm = new AdminCandidateDetailViewModel
            {
                Candidate    = candidate,
                Invitations  = _db.AssessmentInvitations.AsNoTracking().Include(i => i.Exam).Where(i => i.UserId == id).OrderByDescending(i => i.SentAt).ToList(),
                Attempts     = _db.ExamAttempts.AsNoTracking().Include(a => a.Exam).Where(a => a.UserId == id).OrderByDescending(a => a.StartedAt).ToList(),
                Interviews   = _db.InterviewRecords.AsNoTracking().Where(ir => ir.UserId == id).OrderByDescending(ir => ir.CreatedAt).ToList(),
                Documents    = _db.CandidateDocuments.AsNoTracking().Where(d => d.UserId == id).ToList(),
                // FIX: order by IssuedAt descending so the most recent offer is always shown,
                // whether its status is Issued or Accepted.
                Offer        = _db.OfferLetters.AsNoTracking().Include(o => o.History).Where(o => o.UserId == id).OrderByDescending(o => o.IssuedAt).FirstOrDefault(),
                OfferHistory = _db.OfferHistories.AsNoTracking().Include(h => h.OfferLetter)
                    .Where(h => h.OfferLetter != null && h.OfferLetter.UserId == id)
                    .OrderByDescending(h => h.CreatedAt)
                    .ToList(),
                AvailableExams = _db.Exams.AsNoTracking().Where(e => e.IsActive).ToList()
            };
            return View(vm);
        }


        // ── Step 4 – Admin reviews the profile ───────────────────────────────

        [HttpPost]
        public IActionResult ReviewProfile(int userId)
        {
            var user = _db.Users.Find(userId);
            if (user == null || user.Role != PortalRoles.Candidate) return NotFound();

            // Same guard as CandidateWorkflowService's own profile-update methods
            // (CompleteProfile/CompleteResumeAndSkills/IssueCandidateId) — this button is
            // only ever shown in the UI while stage == "Registered" (see
            // CandidateDetail.cshtml), but a direct POST bypassing that could otherwise
            // reset an already-progressed candidate (interview scheduled, offer issued,
            // etc.) all the way back to "Profile Under Review".
            if (!CandidateWorkflowService.IsStillOnboarding(user))
            {
                TempData["Error"] = $"{user.FullName} has already progressed past the profile-review stage — status left unchanged.";
                return RedirectToAction("CandidateDetail", new { id = userId });
            }

            user.RecruitmentStage = "ProfileUnderReview";

            // CLEANUP: removed the "Profile Review" email — it's a pure status ping with
            // no action required from the candidate, and the next real event (Assessment
            // Invitation) already tells them the process has moved forward. Stage change
            // is still recorded below.
            _db.SaveChanges();

            TempData["Success"] = $"Profile of {user.FullName} marked as Under Review.";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        // ── Step 5 – Admin sends secure assessment invitation ─────────────────

        // Unlike RecruiterController.AssignAssessment (which requires the recruiter to set
        // assessmentDate/durationMinutes/passingMarks/expiryHours explicitly for a bulk send),
        // this single-candidate quick-send intentionally passes none of them, so Issue() falls
        // back to: no scheduled open time (accessible immediately), the exam's own
        // DurationMinutes/PassingMarks, and AssessmentInvitationService.DefaultExpiryHours
        // (7 days) — the same constant AssignAssessment falls back to if its own expiryHours
        // is ever omitted. If a recruiter/admin needs to schedule or customize an invite for a
        // single candidate, use AssignAssessment with that one candidate selected instead.
        [HttpPost]
        public IActionResult SendInvitation(int userId, int examId)
        {
            var user = _db.Users.Find(userId);
            var exam = _db.Exams.Find(examId);
            if (user == null || exam == null) return NotFound();

            // The form on CandidateDetail only renders once the candidate is
            // ProfileUnderReview, but that's a UI-level gate only — enforce it here too,
            // so a direct/replayed POST can't invite a candidate at an unexpected stage
            // (e.g. one already offered or onboarded). Shared with RecruiterController's
            // AssignAssessment via RecruitmentStages.CanIssueInvitation so both entry
            // points that issue a *new* invitation agree on which stages allow it.
            if (!RecruitmentStages.CanIssueInvitation(user.RecruitmentStage))
            {
                TempData["Error"] = $"{user.FullName} is at stage '{user.RecruitmentStage}' — an assessment invitation can't be sent from here at this stage.";
                return RedirectToAction("CandidateDetail", new { id = userId });
            }

            var token = AssessmentInvitationService.GenerateSecureToken();
            var link = Url.Action("AssessmentAuth", "Account", new { token }, Request.Scheme);
            _invitations.Issue(user, exam, token, link ?? "", statusesToRevoke: new[] { "Pending" });

            _db.SaveChanges();
            TempData["Success"] = $"Secure assessment invitation sent to {user.FullName} (expires 7 days).";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        // ── Resend assessment invitation (admin can resend at any stage) ─────

        [HttpPost]
        public IActionResult ResendInvitation(int userId, int examId)
        {
            var user = _db.Users.Find(userId);
            var exam = _db.Exams.Find(examId);
            if (user == null || exam == null) return NotFound();

            // Expire all existing invitations for this exam (Pending, Used, Accepted) and send
            // a fresh one — including the email, which the previous implementation skipped
            // (it relied on the candidate finding the new link on their own dashboard).
            var token = AssessmentInvitationService.GenerateSecureToken();
            var link = Url.Action("AssessmentAuth", "Account", new { token }, Request.Scheme);
            _invitations.Issue(user, exam, token, link ?? "", statusesToRevoke: new[] { "Pending", "Used", "Accepted" });

            _db.SaveChanges();
            TempData["Success"] = $"Assessment invitation resent to {user.FullName} — link valid for 7 days.";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        // ── Step 10 – Shortlist candidate ────────────────────────────────────
        // The candidate's stage is updated here, but the shortlist notification
        // email is NOT sent automatically. The admin must click "Send Shortlist Email"
        // (SendShortlistEmail action below) to dispatch the Gmail notification manually.

        [HttpPost]
        public IActionResult Shortlist(int userId)
        {
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            user.RecruitmentStage = "Shortlisted";
            _db.SaveChanges();

            TempData["Success"] = $"{user.FullName} shortlisted successfully. Use 'Send Shortlist Email' to notify the candidate.";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        // ── Step 10b – Admin manually sends shortlist notification via Gmail ──

        [HttpPost]
        public IActionResult SendShortlistEmail(int userId)
        {
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            if (user.RecruitmentStage != "Shortlisted")
            {
                TempData["Error"] = "Candidate is not in 'Shortlisted' stage.";
                return RedirectToAction("CandidateDetail", new { id = userId });
            }

            _notifications.Queue(user, null, "Shortlisting",
                "Application Update – You've Been Shortlisted",
                $"Dear {user.FullName} (ID: {user.CandidateId}),\n\n" +
                $"Congratulations! We are pleased to inform you that you have been shortlisted to move forward in our recruitment process.\n\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"  WHAT HAPPENS NEXT\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"Our recruitment team will be in touch shortly with details about the next round, " +
                $"including interview schedule information.\n\n" +
                $"Please keep an eye on your inbox and ensure your contact details are up to date in the portal.\n\n" +
                $"We look forward to speaking with you.\n\n" +
                $"Best regards,\n" +
                $"VISTAWAYS TECH Recruitment Team");
            _db.SaveChanges();

            TempData["Success"] = $"Shortlist email sent to {user.FullName} ({user.Email}).";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        // ── Reject / withdraw ─────────────────────────────────────────────────

        [HttpPost]
        public IActionResult RejectCandidate(int userId, string reason)
        {
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            reason = Clamp(reason, 500);
            user.RecruitmentStage = "Rejected";
            _notifications.Queue(user, null, "Application Status",
                "Update on your application — VISTAWAYS TECH",
                $"Dear {user.FullName} (ID: {user.CandidateId}),\n\n" +
                $"Thank you for your interest in VISTAWAYS TECH and for the time you invested in our recruitment process.\n\n" +
                $"After careful consideration, we regret to inform you that we are unable to proceed with your application at this time.\n\n" +
                (string.IsNullOrWhiteSpace(reason) ? "" :
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"  REASON\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"{reason}\n\n") +
                $"We encourage you to continue developing your skills and wish you all the best in your future endeavours.\n\n" +
                $"Thank you again for applying to VISTAWAYS TECH.\n\n" +
                $"Best regards,\n" +
                $"VISTAWAYS TECH Recruitment Team");
            _db.SaveChanges();

            TempData["Success"] = $"{user.FullName} marked as Rejected.";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        public IActionResult Students()
        {
            var students = _db.Users.AsNoTracking().Include(u => u.EducationRecords).Include(u => u.Address).Where(u => u.Role == PortalRoles.Candidate).ToList();
            // FIX: filter and group on the DB side — avoid pulling the entire
            // ExamAttempts table into memory when only per-user counts are needed.
            var studentIds = students.Select(s => s.Id).ToList();
            var attemptCounts = _db.ExamAttempts.AsNoTracking()
                .Where(a => studentIds.Contains(a.UserId))
                .GroupBy(a => a.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionary(g => g.UserId, g => g.Count);
            ViewBag.AttemptCounts = attemptCounts;
            return View(students);
        }

    }
}
