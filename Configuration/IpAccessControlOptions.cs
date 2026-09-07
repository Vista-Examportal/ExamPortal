namespace ExamPortal.Configuration
{
    /// <summary>
    /// Configuration for Middleware/IpAllowListMiddleware.cs — an application-level IP
    /// allow-list. This restricts *who can reach the app at all* (and therefore, by
    /// extension, who can ever reach the database behind it) to a known set of source
    /// IPs/CIDR ranges — e.g. an office network, VPN, or admin's static IP.
    ///
    /// This is NOT a substitute for restricting the database server itself (e.g. Azure
    /// SQL server-level firewall rules, a VNet/private-endpoint setup, or on-prem SQL
    /// Server network ACLs) — that's infrastructure configuration outside this codebase
    /// and should still be locked down independently wherever the DB is hosted. This
    /// option is a defense-in-depth layer at the application's front door.
    ///
    /// Disabled by default (Enabled = false) so enabling this feature is always an
    /// explicit, deliberate opt-in — turning it on with an empty or wrong AllowedIPs
    /// list would otherwise lock out every legitimate user, including admins.
    /// </summary>
    public class IpAccessControlOptions
    {
        public const string SectionName = "IpAccessControl";

        /// <summary>Master switch. Must be explicitly set to true — see class remarks above.</summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Each entry may be a single IP ("203.0.113.10"), an IPv4/IPv6 CIDR range
        /// ("203.0.113.0/24"), or the loopback shorthand "localhost" (matches
        /// 127.0.0.1 and ::1 — convenient for local development/testing).
        /// </summary>
        public List<string> AllowedIPs { get; set; } = new();

        /// <summary>
        /// Route prefixes this restriction applies to, e.g. "/Admin", "/Hr",
        /// "/Recruiter". Leave empty to apply the allow-list to the entire
        /// application (every route, including candidate-facing pages).
        /// </summary>
        public List<string> ProtectedPathPrefixes { get; set; } = new();
    }
}
