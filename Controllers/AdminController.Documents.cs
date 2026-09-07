using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
// AdminController — candidate document collection & verification.
// Split out of the single AdminController.cs for readability; same partial class.
    public partial class AdminController
    {

        // ── Step 12 – Request document upload ────────────────────────────────

        [HttpPost]
        public IActionResult RequestDocuments(int userId)
        {
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            // Documents only unlock once every required interview round has passed — see
            // InterviewProgressionHelper. Guards against skipping straight from an early
            // round (e.g. Technical PASS) to Documents.
            var interviews = _db.InterviewRecords.Where(i => i.UserId == userId).ToList();
            var progress = InterviewProgressionHelper.GetProgress(interviews);
            if (!progress.DocumentsUnlocked)
            {
                TempData["Error"] = "Documents cannot be requested yet — one or more required interview rounds haven't passed.";
                return RedirectToAction("CandidateDetail", new { id = userId });
            }

            user.RecruitmentStage = "DocumentsRequested";
            _notifications.Queue(user, null, "Document Request",
                "Please upload your documents — VISTAWAYS TECH",
                $"Dear {user.FullName} (ID: {user.CandidateId}),\n\nPlease log in and upload the following documents:\n• Aadhar Card\n• PAN Card\n• 10th Marksheet\n• 12th Marksheet\n• Degree Certificate\n• Experience Letters (if applicable)\n\nLogin at our portal using your Candidate ID.\n\nVISTAWAYS TECH Recruitment Team");
            _db.SaveChanges();

            TempData["Success"] = $"Document upload request sent to {user.FullName}.";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }


        // ── Step 13 – Admin verifies documents ───────────────────────────────

        [HttpPost]
        public IActionResult VerifyDocument(int documentId, string status, string notes)
        {
            var doc = _db.CandidateDocuments.Find(documentId);
            if (doc == null) return NotFound();

            status = Clamp(status, 50);
            notes  = Clamp(notes, 1000);

            doc.VerificationStatus = status; // Verified | Rejected
            doc.AdminNotes         = notes;
            doc.VerifiedAt         = DateTime.UtcNow;
            doc.VerifiedBy         = User.Identity?.Name ?? "BackOffice";

            var user = _db.Users.Find(doc.UserId);

            // If all REQUIRED docs are verified, advance stage. Previously this checked
            // every CandidateDocument row for this candidate indiscriminately — an
            // unrelated/optional "Other" document sitting at Pending would silently block
            // DocumentsVerified forever, and conversely a required type that was never
            // uploaded at all wouldn't be caught either (an empty .All() over a filtered-
            // out set is vacuously true). DocumentTypes.Required is the actual source of
            // truth for what "all documents verified" means here.
            if (status == "Verified" && user != null)
            {
                var candidateDocs = _db.CandidateDocuments.Where(d => d.UserId == doc.UserId).ToList();
                if (DocumentVerificationHelper.AreAllRequiredDocumentsVerified(candidateDocs))
                {
                    user.RecruitmentStage = RecruitmentStages.DocumentsVerified;
                    // CLEANUP: removed the "Documents Verified" email — no action required
                    // from the candidate, and the next real event (Offer Letter, once HR
                    // approves) already tells them everything this one did. Stage change
                    // is still recorded.
                }
            }
            else if (status == "Rejected" && user != null)
            {
                _notifications.Queue(user, null, "Document Rejected",
                    "Action Required: Document Rejected — VISTAWAYS TECH",
                    $"Dear {user.FullName} (ID: {user.CandidateId}),\n\n" +
                    $"Unfortunately, one of your submitted documents could not be verified and has been rejected.\n\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"  REJECTION DETAILS\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"📄 Document Type : {doc.DocumentType}\n" +
                    $"❌ Reason        : {notes}\n\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"  ACTION REQUIRED\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"Please log in to the portal using your Candidate ID ({user.CandidateId}) and re-upload the correct document at your earliest convenience.\n\n" +
                    $"If you have any questions, please reply to this email.\n\n" +
                    $"Best regards,\n" +
                    $"VISTAWAYS TECH Recruitment Team");
            }

            _db.SaveChanges();
            TempData["Success"] = $"Document {doc.DocumentType} marked as {status}.";
            return RedirectToAction("CandidateDetail", new { id = doc.UserId });
        }

    }
}
