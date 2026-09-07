using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
// AdminController — offer issuance, HR approval/rejection, onboarding kickoff, and
// offer-letter download. Split out of the single AdminController.cs for readability;
// same partial class as the other AdminController.*.cs files.
    public partial class AdminController
    {

        private void AddOfferHistory(OfferLetter offer, string action, string remarks = "")
        {
            offer.History.Add(new OfferHistory
            {
                Action = Clamp(action, 100),
                Actor = User.Identity?.Name ?? "System",
                Remarks = Clamp(remarks, 1000)
            });
        }


        private static string CreateOfferSignature(OfferLetter offer, User user, string signedBy)
        {
            var raw = $"{offer.Id}|{user.CandidateId}|{offer.Designation}|{offer.CtcLpa}|{offer.JoiningDate:O}|{signedBy}|{DateTime.UtcNow:O}";
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        }


        // ── Download offer letter as PDF ─────────────────────────────────────

        [HttpGet]
        public IActionResult DownloadOfferLetter(int userId)
        {
            var user = _db.Users.Include(u => u.Address).FirstOrDefault(u => u.Id == userId);
            var offer = _db.OfferLetters
                .Where(o => o.UserId == userId && (o.Status == "PendingHRApproval" || o.Status == "Issued" || o.Status == "Accepted"))
                .OrderByDescending(o => o.IssuedAt)
                .FirstOrDefault();

            if (user == null || offer == null)
            {
                TempData["Error"] = "No active offer letter found for this candidate.";
                return RedirectToAction("CandidateDetail", new { id = userId });
            }

            var pdfBytes = _offerPdf.GeneratePdf(offer, user);
            var filename = $"OfferLetter_{user.CandidateId}_{offer.IssuedAt:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", filename);
        }


        // ── Step 14 – Issue offer letter ─────────────────────────────────────

        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Hr)]
        public IActionResult IssueOffer(IssueOfferViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please fill all offer details correctly.";
                return RedirectToAction("CandidateDetail", new { id = model.UserId });
            }

            var user = _db.Users.Find(model.UserId);
            if (user == null) return NotFound();

            // Same rule the "Issue Offer Letter" form is already only ever shown under
            // (see CandidateDetail.cshtml's `stage == "DocumentsVerified"` guard) — enforced
            // here too so a direct POST can't issue an offer to a candidate who hasn't
            // actually reached that point yet.
            if (user.RecruitmentStage != RecruitmentStages.DocumentsVerified)
            {
                TempData["Error"] = "An offer can only be issued once the candidate's documents have been verified.";
                return RedirectToAction("CandidateDetail", new { id = model.UserId });
            }

            // Revoke any existing offer
            var existing = _db.OfferLetters.Include(o => o.History)
                .Where(o => o.UserId == model.UserId && (o.Status == "PendingHRApproval" || o.Status == "Issued"))
                .ToList();
            existing.ForEach(o =>
            {
                o.Status = "Revoked";
                AddOfferHistory(o, "Revoked", "Revoked because a new offer was prepared.");
            });

            var acceptToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var offer = new OfferLetter
            {
                UserId           = model.UserId,
                Designation      = model.Designation,
                Department       = model.Department,
                Location         = model.Location,
                CtcLpa           = model.CtcLpa,
                BaseSalaryLpa    = model.BaseSalaryLpa,
                VariablePayLpa   = model.VariablePayLpa,
                BonusLpa         = model.BonusLpa,
                CompensationBreakup = model.CompensationBreakup,
                JoiningDate      = model.JoiningDate,
                OfferValidUntil  = model.OfferValidUntil,
                BasicMonthly             = model.BasicMonthly,
                HraMonthly               = model.HraMonthly,
                SpecialAllowanceMonthly  = model.SpecialAllowanceMonthly,
                EmployeeEpfMonthly       = model.EmployeeEpfMonthly,
                EmployeeEsiMonthly       = model.EmployeeEsiMonthly,
                TrainingChargesMonthly   = model.TrainingChargesMonthly,
                ProfessionalTaxMonthly   = model.ProfessionalTaxMonthly,
                EmployerEpfMonthly       = model.EmployerEpfMonthly,
                EmployerEsiMonthly       = model.EmployerEsiMonthly,
                PerformanceIncentiveAnnual = model.PerformanceIncentiveAnnual,
                Status           = "PendingHRApproval",
                AcceptanceToken  = acceptToken,
                HrApprovalStatus = "Pending",
                SignedBy         = User.Identity?.Name ?? "HR",
                SignedAt         = DateTime.UtcNow,
                IssuedAt         = DateTime.UtcNow
            };
            offer.DigitalSignature = CreateOfferSignature(offer, user, offer.SignedBy);
            AddOfferHistory(offer, "OfferCreated", $"Offer prepared for {model.Designation} with CTC {model.CtcLpa:N2} LPA. Pending HR approval.");
            _db.OfferLetters.Add(offer);
            user.RecruitmentStage = "DocumentsVerified";

            // No email is sent here — the accept-offer link only becomes real once
            // HR approves (see ApproveOffer), so a candidate can never be handed a
            // working accept link for an offer nobody has actually signed off on yet.
            _db.SaveChanges();
            TempData["Success"] = $"Offer letter issued to {user.FullName}.";
            return RedirectToAction("CandidateDetail", new { id = model.UserId });
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Hr)]
        public IActionResult ApproveOffer(int offerId, string remarks = "")
        {
            var offer = _db.OfferLetters.Include(o => o.History).FirstOrDefault(o => o.Id == offerId);
            if (offer == null) return NotFound();
            var user = _db.Users.Include(u => u.Address).FirstOrDefault(u => u.Id == offer.UserId);
            if (user == null) return NotFound();

            remarks = Clamp(remarks, 1000);
            offer.Status = "Issued";
            offer.HrApprovalStatus = "Approved";
            offer.HrApprovedBy = User.Identity?.Name ?? "HR";
            offer.HrApprovedAt = DateTime.UtcNow;
            offer.HrApprovalRemarks = remarks;
            offer.SignedBy = offer.HrApprovedBy;
            offer.SignedAt = DateTime.UtcNow;
            offer.DigitalSignature = CreateOfferSignature(offer, user, offer.SignedBy);
            user.RecruitmentStage = RecruitmentStages.OfferIssued;
            AddOfferHistory(offer, "HRApproved", remarks);

            var link = Url.Action("AcceptOffer", "Account", new { token = offer.AcceptanceToken }, Request.Scheme);
            var pdfBytes = _offerPdf.GeneratePdf(offer, user);
            var pdfFileName = $"OfferLetter_{user.CandidateId}_{offer.IssuedAt:yyyyMMdd}.pdf";
            _notifications.QueueWithAttachment(user, null, "Offer Letter Approved",
                $"Offer Letter from VISTAWAYS TECH — {offer.Designation}",
                $"Dear {user.FullName} (ID: {user.CandidateId}),\n\n" +
                $"Your offer has been approved by HR.\n\n" +
                $"Designation: {offer.Designation}\n" +
                $"Compensation: ₹{offer.CtcLpa:N2} LPA\n" +
                $"Joining Date: {offer.JoiningDate:dd MMM yyyy}\n\n" +
                $"Please accept or decline the offer using this secure link:\n{link}\n\n" +
                $"VISTAWAYS TECH HR Team",
                pdfBytes, pdfFileName);

            _db.SaveChanges();
            TempData["Success"] = "Offer approved, digitally signed, and sent to candidate.";
            return RedirectToAction("CandidateDetail", new { id = offer.UserId });
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Hr)]
        public IActionResult RejectOffer(int offerId, string reason)
        {
            var offer = _db.OfferLetters.Include(o => o.History).FirstOrDefault(o => o.Id == offerId);
            if (offer == null) return NotFound();
            var user = _db.Users.Find(offer.UserId);
            if (user == null) return NotFound();

            reason = Clamp(reason, 1000);
            offer.Status = "Rejected";
            offer.HrApprovalStatus = "Rejected";
            offer.HrApprovedBy = User.Identity?.Name ?? "HR";
            offer.HrApprovedAt = DateTime.UtcNow;
            offer.HrApprovalRemarks = reason;
            offer.RejectionReason = reason;
            offer.RejectedAt = DateTime.UtcNow;
            user.RecruitmentStage = RecruitmentStages.DocumentsVerified;
            AddOfferHistory(offer, "HRRejected", reason);

            _notifications.Queue(user, null, "Offer Rejected",
                "Offer update from VISTAWAYS TECH",
                $"Dear {user.FullName} (ID: {user.CandidateId}),\n\n" +
                $"The prepared offer for {offer.Designation} requires revision and has not been released.\n\n" +
                $"Our HR team will contact you if a revised offer is issued.\n\n" +
                $"VISTAWAYS TECH HR Team");

            _db.SaveChanges();
            TempData["Success"] = "Offer rejected and history updated.";
            return RedirectToAction("CandidateDetail", new { id = offer.UserId });
        }


        // ── Step 16 – Begin onboarding ────────────────────────────────────────

        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Hr)]
        public IActionResult BeginOnboarding(int userId)
        {
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            user.RecruitmentStage = "Onboarding";
            var offer = _db.OfferLetters.FirstOrDefault(o => o.UserId == userId && o.Status == "Accepted");
            if (offer != null) offer.JoiningConfirmedAt = DateTime.UtcNow;

            // CLEANUP: removed the "Onboarding" welcome email per request — the email's
            // own content said HR would follow up directly with reporting/access/induction
            // details, so this step was just an early, redundant heads-up.
            _db.SaveChanges();

            TempData["Success"] = $"Onboarding initiated for {user.FullName}.";
            return RedirectToAction("CandidateDetail", new { id = userId });
        }

    }
}
