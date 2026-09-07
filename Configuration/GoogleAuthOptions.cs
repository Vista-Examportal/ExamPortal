namespace ExamPortal.Configuration
{
    /// <summary>
    /// Google OAuth credentials for candidate "Continue with Google" sign-in
    /// (see Controllers/AccountController.ExternalAuth.cs). Bound from the
    /// "Authentication:Google" configuration section.
    ///
    /// Values are never hardcoded. In override order they come from:
    /// appsettings.json -> appsettings.{Environment}.json -> .NET User Secrets
    /// (local dev: `dotnet user-secrets set "Authentication:Google:ClientId" "..."`)
    /// -> environment variables (Authentication__Google__ClientId /
    /// Authentication__Google__ClientSecret) -> Azure Key Vault, when configured.
    /// See appsettings.json for the placeholder section and README for setup notes.
    ///
    /// If left unset, Google sign-in is simply inactive (GoogleSignInStatus.IsConfigured
    /// is false) rather than the app failing to start — see Program.cs.
    /// </summary>
    public class GoogleAuthOptions
    {
        public const string SectionName = "Authentication:Google";

        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";

        /// <summary>True only when both values have actually been supplied.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }
}
