using ExamPortal.Models;

namespace ExamPortal.Services
{
    /// <summary>
    /// Defines what the sidebar shows for each role, matching the curated,
    /// role-specific structure from the navigation redesign brief (one
    /// primary Dashboard entry, then a short, non-duplicated list of the
    /// destinations that role actually needs).
    ///
    /// Each item resolves to one of:
    ///  - a real, dedicated route (Controller/Action) — including several
    ///    new ones added as part of this pass (Home/Jobs, Account/Profile,
    ///    Hr/Reports, and the Admin dashboard's new Users/HR Staff/Audit
    ///    Logs tabs) — see each controller for how the underlying data is
    ///    sourced;
    ///  - an in-page tab on a role's own dashboard, via the ?tab= query
    ///    string app.js's initTabs already reads on load; or
    ///  - a Fragment anchor to a specific section of a page (used for HR's
    ///    links into the shared Admin Pipeline board, and the candidate
    ///    dashboard's status sections).
    ///
    /// Admin's working areas (Candidates, Pipeline, Assessments, Interviews,
    /// Offers) and the Recruiter's Offer Status and Communications tabs are
    /// in the sidebar as well as their in-page tab bars: a destination that
    /// is only reachable from a tab bar is easy to miss, and each role's
    /// workflow needs them (Admin runs the whole pipeline; a Recruiter must
    /// be able to follow an offer and see outgoing communications).
    /// Create Assessment stays a button on the Assessments tab.
    /// </summary>
    public static class SidebarNavMap
    {
        public static List<SidebarNavSection> For(string? role)
        {
            if (role == PortalRoles.Admin)
            {
                return new()
                {
                    new SidebarNavSection
                    {
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Dashboard", Icon = "layout-dashboard", Controller = "Admin", Action = "Index", Tab = "dashboard" },
                        }
                    },
                    new SidebarNavSection
                    {
                        Title = "People",
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Users", Icon = "shield-check", Controller = "Admin", Action = "Index", Tab = "access" },
                            new SidebarNavItem { Label = "Recruiters", Icon = "user-cog", Controller = "Admin", Action = "Index", Tab = "recruiters" },
                            new SidebarNavItem { Label = "HR Staff", Icon = "users-round", Controller = "Admin", Action = "Index", Tab = "hrstaff" },
                        }
                    },
                    new SidebarNavSection
                    {
                        Title = "Recruitment",
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Jobs", Icon = "briefcase", Controller = "Admin", Action = "Index", Tab = "jobs" },
                            new SidebarNavItem { Label = "Candidates", Icon = "users", Controller = "Admin", Action = "Index", Tab = "candidates" },
                            new SidebarNavItem { Label = "Pipeline", Icon = "workflow", Controller = "Admin", Action = "Pipeline" },
                            new SidebarNavItem { Label = "Assessments", Icon = "clipboard-list", Controller = "Admin", Action = "Index", Tab = "assessments" },
                            new SidebarNavItem { Label = "Interviews", Icon = "message-square", Controller = "Admin", Action = "Index", Tab = "interviews" },
                            new SidebarNavItem { Label = "Offers", Icon = "file-check", Controller = "Admin", Action = "Index", Tab = "offers" },
                        }
                    },
                    new SidebarNavSection
                    {
                        Title = "System",
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Reports", Icon = "bar-chart-3", Controller = "Admin", Action = "Index", Tab = "analytics" },
                            new SidebarNavItem { Label = "Audit Logs", Icon = "shield", Controller = "Admin", Action = "Index", Tab = "auditlogs" },
                            new SidebarNavItem { Label = "Settings", Icon = "settings", Controller = "Admin", Action = "Index", Tab = "operations" },
                        }
                    },
                };
            }

            if (role == PortalRoles.Recruiter)
            {
                return new()
                {
                    new SidebarNavSection
                    {
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Dashboard", Icon = "layout-dashboard", Controller = "Recruiter", Action = "Index", Tab = "dashboard" },
                        }
                    },
                    new SidebarNavSection
                    {
                        Title = "Recruitment",
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Jobs", Icon = "briefcase", Controller = "Recruiter", Action = "Index", Tab = "jobs" },
                            new SidebarNavItem { Label = "Candidates", Icon = "users", Controller = "Recruiter", Action = "Index", Tab = "candidates" },
                            new SidebarNavItem { Label = "Assessments", Icon = "clipboard-list", Controller = "Recruiter", Action = "Index", Tab = "assessments" },
                            new SidebarNavItem { Label = "Interviews", Icon = "message-square", Controller = "Recruiter", Action = "Index", Tab = "interviews" },
                            // View-only offer tracking (offers are released by HR/Admin).
                            new SidebarNavItem { Label = "Offer Status", Icon = "file-check", Controller = "Recruiter", Action = "Index", Tab = "offers" },
                        }
                    },
                    new SidebarNavSection
                    {
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Reports", Icon = "bar-chart-3", Controller = "Recruiter", Action = "Index", Tab = "reports" },
                            new SidebarNavItem { Label = "Communications", Icon = "mail", Controller = "Recruiter", Action = "Index", Tab = "communications" },
                            new SidebarNavItem { Label = "Profile", Icon = "circle-user-round", Controller = "Account", Action = "Profile" },
                        }
                    },
                };
            }

            if (role == PortalRoles.Hr)
            {
                return new()
                {
                    new SidebarNavSection
                    {
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Dashboard", Icon = "layout-dashboard", Controller = "Hr", Action = "Index" },
                        }
                    },
                    new SidebarNavSection
                    {
                        Title = "Hiring",
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Candidates", Icon = "users", Controller = "Admin", Action = "Students" },
                            new SidebarNavItem { Label = "Interviews", Icon = "message-square", Controller = "Admin", Action = "Pipeline", Fragment = "pipeline-interview" },
                            new SidebarNavItem { Label = "Offers", Icon = "file-check", Controller = "Admin", Action = "Pipeline", Fragment = "pipeline-offer" },
                            new SidebarNavItem { Label = "Onboarding", Icon = "briefcase", Controller = "Admin", Action = "Pipeline", Fragment = "pipeline-onboarding" },
                        }
                    },
                    new SidebarNavSection
                    {
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Reports", Icon = "bar-chart-3", Controller = "Hr", Action = "Reports" },
                            new SidebarNavItem { Label = "Profile", Icon = "circle-user-round", Controller = "Account", Action = "Profile" },
                        }
                    },
                };
            }

            // Candidate (or anonymous-but-authenticated fallback).
            return new()
            {
                new SidebarNavSection
                {
                    Title = "Menu",
                    Items = new()
                    {
                        new SidebarNavItem { Label = "Dashboard", Icon = "home", Controller = "Home", Action = "Index" },
                        new SidebarNavItem { Label = "Jobs", Icon = "briefcase", Controller = "Home", Action = "Jobs" },
                    }
                },
                new SidebarNavSection
                {
                    Title = "My Journey",
                    Items = new()
                    {
                        new SidebarNavItem { Label = "Applications", Icon = "file-text", Controller = "Home", Action = "Index", Fragment = "application-status" },
                        new SidebarNavItem { Label = "Assessments", Icon = "bar-chart-3", Controller = "Home", Action = "Results" },
                        new SidebarNavItem { Label = "Interviews", Icon = "message-square", Controller = "Home", Action = "Index", Fragment = "interview-status" },
                        new SidebarNavItem { Label = "Offers", Icon = "file-check", Controller = "Home", Action = "Index", Fragment = "offer-status" },
                        new SidebarNavItem { Label = "Documents", Icon = "folder-open", Controller = "Home", Action = "Index", Fragment = "documents-status" },
                        new SidebarNavItem { Label = "Notifications", Icon = "bell", Controller = "Home", Action = "Index", Fragment = "notifications" },
                    }
                },
                new SidebarNavSection
                {
                    Items = new()
                    {
                        new SidebarNavItem { Label = "Profile", Icon = "circle-user-round", Controller = "Account", Action = "CompleteProfile" },
                    }
                }
            };
        }
    }
}
