namespace ExamPortal.Models
{
    /// <summary>
    /// One link in the primary sidebar. Display-only — carries no
    /// authorization logic of its own. Access is (and remains) governed
    /// entirely by the [Authorize(Roles = ...)] attributes on the actual
    /// controllers/actions; this model exists purely so the sidebar can be
    /// described as data instead of as a hardcoded if/else-if chain of
    /// Razor markup per role.
    /// </summary>
    public class SidebarNavItem
    {
        public string Label { get; set; } = "";
        public string Icon { get; set; } = "circle";
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "";

        /// <summary>
        /// When true, this item is treated as "active" whenever the current
        /// request is anywhere on <see cref="Controller"/>, regardless of
        /// which action. Used for single-entry-point portals where one
        /// sidebar link fronts a whole controller instead of one specific
        /// action.
        /// </summary>
        public bool MatchControllerOnly { get; set; }

        /// <summary>
        /// When set, this item links to a specific in-page tab on
        /// Controller/Action (e.g. "interviews") via the existing ?tab=
        /// query string that app.js's tab controller already reads on load
        /// (see wwwroot/js/app.js, initTabs). This promotes a tab that used
        /// to be reachable only after already being on the dashboard into a
        /// real, persistent sidebar destination — without adding any new
        /// controller action or changing what data that tab shows.
        /// </summary>
        public string? Tab { get; set; }

        /// <summary>
        /// Optional URL fragment (e.g. "pipeline-interview"), for items that
        /// link to a specific anchored section of a page rather than a tab
        /// or a dedicated route — used by HR's dashboard links into the
        /// Admin Pipeline board's individual columns. Same mechanism as
        /// PortalModule.Fragment (see Models/PortalModels.cs).
        /// </summary>
        public string? Fragment { get; set; }
    }

    /// <summary>
    /// A labeled (or unlabeled, for the first/top section) group of
    /// <see cref="SidebarNavItem"/>s, rendered under one section heading.
    /// </summary>
    public class SidebarNavSection
    {
        public string? Title { get; set; }
        public List<SidebarNavItem> Items { get; set; } = new();
    }

    /// <summary>
    /// View model passed to the SidebarNav view component's view: the nav
    /// data for the signed-in user's role, plus enough route context to
    /// decide which item (if any) is currently active.
    /// </summary>
    public class SidebarNavViewModel
    {
        public List<SidebarNavSection> Sections { get; set; } = new();
        public string CurrentController { get; set; } = "";
        public string CurrentAction { get; set; } = "";

        /// <summary>The ?tab= query value on the current request, if any.</summary>
        public string CurrentTab { get; set; } = "";
    }
}
