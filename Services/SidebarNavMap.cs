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
    /// Deliberately NOT in any of these lists: the granular Admin-only
    /// working tabs (Candidates/Assessments/Create Assessment/Interviews/
    /// Offers/Pipeline) that still exist and are still fully reachable from
    /// inside Admin/Index's own tab bar — promoting every one of them to
    /// the sidebar as well would just be the "duplicate navigation" the
    /// redesign brief asks to remove. Nothing was deleted; they're one
    /// click away instead of two.
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
                        }
                    },
                    new SidebarNavSection
                    {
                        Items = new()
                        {
                            new SidebarNavItem { Label = "Reports", Icon = "bar-chart-3", Controller = "Recruiter", Action = "Index", Tab = "reports" },
                            new SidebarNavItem { Label = "Profile", Icon = "circle-user-round", Controller = "Account", Action = "Profile" },
                        }
                    },
                    // "Offers" and "Email & Notifications" stay reachable from the
                    // in-page tab bar itself (nothing removed) — Offers isn't in the
                    // target sidebar list for this role, and Notifications is an
                    // operational utility panel rather than a primary destination.
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
