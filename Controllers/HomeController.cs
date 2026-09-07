using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
    [Authorize(Roles = PortalRoles.Candidate)]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly FileStorageService _fileStorage;
        private readonly NotificationService _notifications;
        private readonly ActivityFeedService _activityFeed;

        public HomeController(AppDbContext db, FileStorageService fileStorage, NotificationService notifications, ActivityFeedService activityFeed)
        {
            _db = db;
            _fileStorage = fileStorage;
            _notifications = notifications;
            _activityFeed = activityFeed;
        }

        private int UserId => User.GetUserId();

        public IActionResult Index()
        {
            var candidate = _db.Users
                .AsNoTracking()
                .Include(u => u.CandidateProfile)
                .Include(u => u.ProfileCompletion)
                .Include(u => u.PersonalDetail)
                .Include(u => u.Address)
                .Include(u => u.EducationRecords)
                .Include(u => u.ProfessionalProfile)
                .Include(u => u.JobPreference)
                .FirstOrDefault(u => u.Id == UserId);
            if (candidate == null) return RedirectToAction("Login", "Account");
            var vm = new CandidateDashboardViewModel
            {
                Candidate   = candidate,
                Invitations = _db.AssessmentInvitations.AsNoTracking().Include(i => i.Exam)
                                  .Where(i => i.UserId == UserId)
                                  .OrderByDescending(i => i.SentAt).ToList(),
                Attempts    = _db.ExamAttempts.AsNoTracking().Include(a => a.Exam)
                                  .Where(a => a.UserId == UserId)
                                  .OrderByDescending(a => a.StartedAt).Take(5).ToList(),
                Interviews  = _db.InterviewRecords.AsNoTracking().Where(ir => ir.UserId == UserId)
                                  .OrderByDescending(ir => ir.CreatedAt).ToList(),
                Documents   = _db.CandidateDocuments.AsNoTracking().Where(d => d.UserId == UserId).ToList(),
                Notifications = _db.Notifications.AsNoTracking().Where(n => n.UserId == UserId)
                                  .OrderByDescending(n => n.CreatedAt).Take(20).ToList(),
                Offer       = _db.OfferLetters.AsNoTracking().FirstOrDefault(o => o.UserId == UserId)
            };
            vm.ActivityFeed = _activityFeed.BuildCandidateFeed(vm.Invitations, vm.Attempts, vm.Interviews, vm.Offer);
            return View(vm);
        }

        // Open roles, derived the same way as the Admin/Recruiter "Jobs" tabs:
        // active, non-template Exams grouped by Subject (closest proxy to
        // "role" on the Exam model). Read-only, candidate-facing — no
        // application flow here, since candidates are invited to a specific
        // assessment by a recruiter rather than self-applying to a listing;
        // this exists so "Jobs" in the sidebar isn't a dead link, matching
        // the same open-roles data shown elsewhere in the portal.
        public IActionResult Jobs()
        {
            var activeExams = _db.Exams
                .AsNoTracking()
                .Where(e => e.IsActive && !e.IsTemplate)
                .ToList();
            var openRoles = activeExams
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Subject) ? "General" : e.Subject)
                .Select(g => new OpenRoleViewModel
                {
                    Role = g.Key,
                    Skills = string.Join(", ", g.Select(e => e.RequiredSkills).Where(s => !string.IsNullOrWhiteSpace(s)).SelectMany(s => s.Split(',')).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct()),
                    Locations = string.Join(", ", g.Select(e => e.EligibleLocations).Where(s => !string.IsNullOrWhiteSpace(s)).SelectMany(s => s.Split(',')).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct()),
                    Experience = string.Join(", ", g.Select(e => e.ExperienceLevel).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
                    OpenCount = g.Count()
                })
                .OrderByDescending(r => r.OpenCount)
                .ToList();
            return View(openRoles);
        }

        public IActionResult Results()
        {
            var attempts = _db.ExamAttempts.AsNoTracking().Include(a => a.Exam)
                .Where(a => a.UserId == UserId)
                .OrderByDescending(a => a.StartedAt).ToList();
            return View(attempts);
        }

        // ── Step 12 – Document upload ─────────────────────────────────────────

        [HttpPost]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadDocument(string documentType, IFormFile file)
        {
            var user = _db.Users.Find(UserId);
            if (user == null) return NotFound();

            // Uploads are only ever open during DocumentsRequested. The previous check
            // here also allowed DocumentsVerified — backwards from what it should do —
            // which meant a candidate could keep uploading/replacing documents even
            // after every required one had already been verified and the offer workflow
            // had moved on.
            if (user.RecruitmentStage == RecruitmentStages.DocumentsVerified)
            {
                TempData["Error"] = "Your documents have already been verified. Document uploads are no longer available.";
                return RedirectToAction("Index");
            }
            if (user.RecruitmentStage != RecruitmentStages.DocumentsRequested)
            {
                TempData["Error"] = "Document upload is not available at your current recruitment stage.";
                return RedirectToAction("Index");
            }

            // documentType is client-supplied — never trust it as-is. Only the specific
            // set of document types this workflow actually offers may be uploaded; this
            // also implicitly blocks path/identifier-style values someone might try to
            // slip in via a direct POST instead of the form's own hidden field.
            documentType = TextUtils.Clamp(documentType, 100);
            if (!DocumentTypes.AllOffered.Contains(documentType))
            {
                TempData["Error"] = "That document type is not currently requested.";
                return RedirectToAction("Index");
            }

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction("Index");
            }

            var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
            {
                TempData["Error"] = "Only PDF, JPG, and PNG files are accepted.";
                return RedirectToAction("Index");
            }

            if (file.Length > 5 * 1024 * 1024)
            {
                TempData["Error"] = "File must be 5 MB or smaller.";
                return RedirectToAction("Index");
            }

            // Find (rather than replace outright) the existing record for this type first,
            // so its current VerificationStatus can gate whether this upload is even
            // allowed — a Verified document is locked, and (by design) so is a Pending one
            // still awaiting review; only Rejected (or no existing record at all) may be
            // uploaded/replaced. This is the actual server-side enforcement the UI's own
            // hide/disable behavior mirrors — never the other way around.
            var existing = _db.CandidateDocuments
                .FirstOrDefault(d => d.UserId == UserId && d.DocumentType == documentType);
            if (existing != null && !DocumentVerificationHelper.CanCandidateUploadDocument(existing.VerificationStatus))
            {
                TempData["Error"] = existing.VerificationStatus == "Verified"
                    ? "This document has already been verified and cannot be replaced."
                    : "This document is currently under review. It can only be replaced if it's rejected.";
                return RedirectToAction("Index");
            }

            var stored = await _fileStorage.SavePrivateAsync(file, "documents", ext);
            var filePath = stored.PublicPath;
            var fileHash = stored.FileHash;

            if (existing != null)
            {
                // Rejected → re-uploaded → back to Pending for another review pass. The
                // previous rejection reason no longer applies to this new file, so it's
                // cleared rather than left sitting next to a "Pending" badge.
                existing.FilePath             = filePath;
                existing.OriginalFileName     = file.FileName;
                existing.VerificationStatus   = "Pending";
                existing.AdminNotes           = "";
                existing.UploadedAt           = DateTime.UtcNow;
                existing.VerifiedAt           = null;
                existing.VerifiedBy           = "";
                existing.FileHash             = fileHash;
            }
            else
            {
                _db.CandidateDocuments.Add(new CandidateDocument
                {
                    UserId           = UserId,
                    DocumentType     = documentType,
                    FilePath         = filePath,
                    OriginalFileName = file.FileName,
                    VerificationStatus = "Pending",
                    FileHash         = fileHash,
                    UploadedAt       = DateTime.UtcNow
                });
            }

            _db.SaveChanges();
            TempData["Success"] = $"{documentType} uploaded successfully and is pending verification.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> MarkNotificationRead(int notificationId)
        {
            await _notifications.MarkReadAsync(notificationId, UserId);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllNotificationsRead()
        {
            await _notifications.MarkAllReadAsync(UserId);
            return RedirectToAction("Index");
        }
    }
}
