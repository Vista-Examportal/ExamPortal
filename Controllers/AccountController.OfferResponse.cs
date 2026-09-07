using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace ExamPortal.Controllers
{
// AccountController — candidate offer-letter accept/decline (the public, token-based
// AcceptOffer page). Split out of the single AccountController.cs for readability;
// same partial class as the other AccountController.*.cs files.
    public partial class AccountController
    {

        [HttpGet]
        public IActionResult AcceptOffer(string token)
        {
            var offer = _db.OfferLetters.FirstOrDefault(o => o.AcceptanceToken == token && o.Status == "Issued");
            if (offer == null)
            {
                TempData["Error"] = "This offer link is invalid or the offer has already been processed.";
                return RedirectToAction("Login");
            }

            var candidate = _db.Users.Find(offer.UserId);
            if (candidate == null) return NotFound();

            ViewBag.Offer = offer;
            ViewBag.Candidate = candidate;
            return View();
        }

        [HttpPost]
        public IActionResult AcceptOffer(string token, string action, string reason = "")
        {
            var offer = _db.OfferLetters.Include(o => o.History).FirstOrDefault(o => o.AcceptanceToken == token && o.Status == "Issued");
            if (offer == null) return BadRequest("Invalid token.");

            var candidate = _db.Users.Find(offer.UserId);
            if (candidate == null) return NotFound();

            if (action == "accept")
            {
                offer.Status = "Accepted";
                offer.AcceptedAt = DateTime.UtcNow;
                candidate.RecruitmentStage = RecruitmentStages.OfferAccepted;
                offer.History.Add(new OfferHistory
                {
                    Action = "CandidateAccepted",
                    Actor = candidate.CandidateId,
                    Remarks = "Candidate accepted the offer through secure offer link."
                });

                // CLEANUP: removed the "Offer Accepted" email + duplicate PDF generation —
                // this just confirms the candidate's own just-completed action (already
                // confirmed on the AcceptOffer page), and they already received the PDF
                // offer letter in the "Offer Letter Approved" email.
            }
            else
            {
                offer.Status = "Declined";
                offer.RejectedAt = DateTime.UtcNow;
                offer.RejectionReason = string.IsNullOrWhiteSpace(reason) ? "Candidate declined the offer." : reason.Trim();
                candidate.RecruitmentStage = RecruitmentStages.OfferDeclined;
                offer.History.Add(new OfferHistory
                {
                    Action = "CandidateDeclined",
                    Actor = candidate.CandidateId,
                    Remarks = offer.RejectionReason
                });
                // CLEANUP: removed the "Offer Declined" email — confirms the candidate's
                // own just-completed action, already reflected on the AcceptOffer page.
            }

            _db.SaveChanges();
            TempData["Success"] = action == "accept"
                ? "Congratulations! You have accepted the offer. Our HR team will contact you with onboarding details."
                : "You have declined the offer. Thank you for your time.";
            return RedirectToAction("Login");
        }

    }
}
