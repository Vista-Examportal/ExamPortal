namespace ExamPortal.Configuration
{
    /// <summary>
    /// RESERVED FOR FUTURE USE — this codebase does not currently issue or validate
    /// JWTs anywhere. Authentication is entirely cookie-based (see the
    /// CookieAuthenticationDefaults / AuthSchemes.GoogleExternal setup in Program.cs).
    /// No functionality was changed to introduce this class.
    ///
    /// It's provided so that if/when a JWT-secured API surface is added, the signing
    /// key never needs to be hardcoded — same override chain as the other options
    /// classes: appsettings -> User Secrets -> environment variables
    /// (Jwt__SigningKey) -> Azure Key Vault. Do not wire this up with a hardcoded
    /// or short signing key; generate a high-entropy secret and store it only in
    /// User Secrets / environment variables / Key Vault, never in source control.
    /// </summary>
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string SigningKey { get; set; } = "";
        public string Issuer { get; set; } = "";
        public string Audience { get; set; } = "";
        public int ExpiryMinutes { get; set; } = 60;
    }
}
