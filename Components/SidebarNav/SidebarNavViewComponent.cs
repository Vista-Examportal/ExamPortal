using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExamPortal.Components
{
    /// <summary>
    /// Renders the primary sidebar navigation. Replaces the role-branching
    /// if/else-if chain that previously lived inline in
    /// Views/Shared/_Layout.cshtml — same links, same behavior, now sourced
    /// from <see cref="SidebarNavMap"/> instead of hardcoded markup.
    ///
    /// This component only decides *what to show*; it does not grant access
    /// to anything. Every linked action still enforces its own
    /// [Authorize(Roles = ...)] independently, exactly as before.
    /// </summary>
    public class SidebarNavViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(string currentController, string currentAction)
        {
            string? role =
                User.IsInRole(PortalRoles.Admin) ? PortalRoles.Admin :
                User.IsInRole(PortalRoles.Recruiter) ? PortalRoles.Recruiter :
                User.IsInRole(PortalRoles.Hr) ? PortalRoles.Hr :
                null; // Candidate, or any other authenticated role — falls back to the candidate nav.

            var model = new SidebarNavViewModel
            {
                Sections = SidebarNavMap.For(role),
                CurrentController = currentController ?? "",
                CurrentAction = currentAction ?? "",
                CurrentTab = HttpContext.Request.Query["tab"].ToString()
            };

            return View(model);
        }
    }
}
