using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamPortal.Controllers
{
    // Public entry point. Anonymous visitors land here instead of going straight
    // to the login form; authenticated users are sent on to their role dashboard.
    public class SiteController : Controller
    {
        private readonly NotificationService _notifications;

        public SiteController(NotificationService notifications)
        {
            _notifications = notifications;
        }

        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole(PortalRoles.Admin)) return RedirectToAction("Index", "Admin");
                if (User.IsInRole(PortalRoles.Recruiter)) return RedirectToAction("Index", "Recruiter");
                if (User.IsInRole(PortalRoles.Hr)) return RedirectToAction("Index", "Hr");
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        /// <summary>
        /// Target of app.UseExceptionHandler in Program.cs. Must be reachable by every
        /// role and by anonymous visitors — an exception can happen to anyone — which is
        /// why this lives on the unauthenticated SiteController rather than the
        /// Candidate-only HomeController. Shows only a request id for support reference;
        /// never surfaces the underlying exception/stack trace to the browser.
        /// </summary>
        public IActionResult Error()
        {
            ViewBag.RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            Response.StatusCode = 500;
            return View();
        }

        /// <summary>
        /// Target of app.UseStatusCodePagesWithReExecute in Program.cs — catches any
        /// response that comes back with a non-success status code and no body yet
        /// (most commonly 404 for a mistyped/dead link, or 403 from [Authorize] on a
        /// page this visitor can't access) and shows a branded page instead of the
        /// framework's bare, unstyled default. Same reachable-by-anyone reasoning as
        /// Error() above — a 404 can happen to any visitor, logged in or not.
        ///
        /// Named StatusCodePage rather than StatusCode on purpose: ControllerBase
        /// already defines a StatusCode(int) helper (returns a StatusCodeResult) that
        /// every controller inherits — an action here with that exact name would hide
        /// it (CS0114) and silently change what any future `return StatusCode(...);`
        /// elsewhere in this controller actually calls. The route URL below still says
        /// "StatusCode" — that's just a string Program.cs points at, not a C# identifier,
        /// so it can't collide with anything.
        /// </summary>
        [Route("Site/StatusCode/{code:int}")]
        public IActionResult StatusCodePage(int code)
        {
            Response.StatusCode = code;
            var (title, message) = code switch
            {
                404 => ("Page not found", "That page doesn't exist, or it's moved. Double-check the link, or head back to somewhere familiar."),
                403 => ("Access denied", "You don't have permission to view that page with your current account."),
                _ => ("Something went wrong", "That wasn't supposed to happen. Please try again in a moment, or head back to somewhere familiar."),
            };
            ViewBag.Code = code;
            ViewBag.Title = title;
            ViewBag.Message = message;
            // Explicit view name: the action is StatusCodePage now, but the view file
            // is still Views/Site/StatusCode.cshtml — no need to rename that file too,
            // View() would otherwise look for "StatusCodePage.cshtml" by convention.
            return View("StatusCode");
        }

        // ── Footer / legal pages ──────────────────────────────────────────────
        // All plain content pages, reachable by anyone (no [Authorize]) since they're
        // linked from the public footer on every page, authenticated or not. Each view
        // under Views/Site/ that covers Privacy/Terms/Cookies is a real, tailored draft —
        // accurate to what this app actually does (see the note on PrivacyPolicy below) —
        // not lorem-ipsum boilerplate. It still hasn't been reviewed by a lawyer, which
        // each page says explicitly in its own footnote.
        public IActionResult AboutUs() => View();

        [HttpGet]
        public IActionResult ContactUs() => View(new ContactMessageViewModel());

        [HttpPost]
        [EnableRateLimiting("Contact")]
        public IActionResult ContactUs(ContactMessageViewModel model)
        {
            // Honeypot: a real visitor never sees or fills this field (hidden via CSS in
            // the view). A non-empty value here means a bot filled every input it found —
            // pretend to succeed so the bot doesn't learn to adapt, but don't actually send.
            if (!string.IsNullOrWhiteSpace(model.Website))
            {
                TempData["ContactSuccess"] = "Thanks — your message has been sent. We'll get back to you soon.";
                return RedirectToAction("ContactUs");
            }

            if (!ModelState.IsValid)
                return View(model);

            var sent = _notifications.TrySendContactFormMessage(model.Name.Trim(), model.Email.Trim(), model.Phone?.Trim(), model.Message.Trim(), out var error);
            if (!sent)
            {
                ModelState.AddModelError("", "Sorry, we couldn't send your message right now. Please try again shortly, or email us directly at info@vistawaystech.com.");
                return View(model);
            }

            TempData["ContactSuccess"] = "Thanks — your message has been sent. We'll get back to you soon.";
            return RedirectToAction("ContactUs");
        }

        /// <summary>
        /// The view behind this action is a real, tailored privacy policy — it names the
        /// actual categories of data this app collects (from the models/flows in this
        /// codebase: name, email, mobile, DOB, gender, address, resume, skills, photo),
        /// describes the actual recruitment pipeline, and covers India's DPDP Act 2023
        /// (the applicable law, since Vistaways Tech is based in Hyderabad). It still needs
        /// a lawyer's review, and a named Grievance Officer designated (DPDP Act
        /// requirement) before being relied on as-is — both noted on the page itself.
        /// </summary>
        public IActionResult PrivacyPolicy() => View();

        /// <summary>Same caveat as PrivacyPolicy above — real, tailored draft, not yet
        /// lawyer-reviewed.</summary>
        public IActionResult TermsOfService() => View();

        /// <summary>Same caveat as PrivacyPolicy above — real, tailored draft, not yet
        /// lawyer-reviewed.</summary>
        public IActionResult CookiePolicy() => View();

        public IActionResult AccessibilityStatement() => View();
    }
}
