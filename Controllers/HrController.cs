using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamPortal.Controllers
{
    [Authorize(Roles = PortalRoles.Hr)]
    public class HrController : Controller
    {
        private readonly EnterprisePortalService _portal;

        public HrController(EnterprisePortalService portal)
        {
            _portal = portal;
        }

        public IActionResult Index()
        {
            return View(_portal.BuildDashboard(PortalRoles.Hr));
        }

        // Reuses the same cached dashboard snapshot as Index() — this is the
        // "hiring metrics" (Interviews / Docs Requested / Offers Issued /
        // Onboarding) and priority-candidate queue already computed there,
        // just given its own dedicated view instead of only appearing as a
        // strip of cards above the module links.
        public IActionResult Reports()
        {
            return View(_portal.BuildDashboard(PortalRoles.Hr));
        }
    }
}
