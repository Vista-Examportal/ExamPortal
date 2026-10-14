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

        // Aggregate reporting over the whole hiring pipeline (stage funnel, interview
        // outcomes, document verification, offer outcomes, joining outlook). This used to
        // re-render the dashboard's own metric cards and priority queue, which made it a
        // duplicate of Index(); see EnterprisePortalService.BuildHrReport.
        public IActionResult Reports()
        {
            return View(_portal.BuildHrReport());
        }
    }
}
