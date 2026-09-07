namespace ExamPortal.Services
{
    /// <summary>
    /// Server-side-only check for disposable/temporary email domains at registration
    /// (see AccountController.Registration.cs). This list is never sent to the
    /// browser — Register.cshtml/app.js have no knowledge of it, so it can't be
    /// trivially discovered and worked around from client-side JavaScript.
    ///
    /// The domain list is a maintainable, config-driven blocklist —
    /// "DisposableEmail:BlockedDomains" in appsettings.json (or environment-specific
    /// overrides / user-secrets) — merged with a solid built-in default so the check
    /// works correctly even with no configuration present. Extend the blocklist via
    /// configuration for routine additions rather than editing this file.
    ///
    /// This deliberately does NOT call an external disposable-email-detection API:
    /// adding a network dependency to the registration endpoint's critical path
    /// trades a small blocklist-maintenance cost for a new source of latency/outages
    /// on every signup, which isn't a good trade for a project this size. If a
    /// paid/external service (e.g. Kickbox, ZeroBounce, Mailgun's validation API) is
    /// added later: keep its API key in configuration/environment variables (`dotnet
    /// user-secrets` in Development, real environment variables in production) —
    /// never in source control or client-side JavaScript — and fail open to this
    /// local list if the external call errors or times out (i.e. still block
    /// anything already in _blockedDomains; only skip the *extra* external check).
    /// </summary>
    public class DisposableEmailService
    {
        // A reasonably broad set of well-known disposable/temp-mail providers.
        // Extend via appsettings.json's DisposableEmail:BlockedDomains (merged with,
        // not replacing, this list) rather than editing this array for routine additions.
        private static readonly string[] DefaultBlockedDomains =
        {
            "mailinator.com", "tempmail.com", "10minutemail.com", "yopmail.com",
            "guerrillamail.com", "guerrillamail.info", "guerrillamail.biz", "guerrillamail.de",
            "sharklasers.com", "trashmail.com", "trash-mail.com", "temp-mail.org",
            "throwawaymail.com", "getnada.com", "maildrop.cc", "dispostable.com",
            "fakeinbox.com", "mailnesia.com", "mintemail.com", "mytemp.email",
            "spamgourmet.com", "tempinbox.com", "tempmailaddress.com", "moakt.com",
            "emailondeck.com", "mohmal.com", "33mail.com", "mailcatch.com",
            "mail-temporaire.fr", "mailsac.com", "burnermail.io", "harakirimail.com",
            "spam4.me", "tempr.email", "discard.email", "10minemail.com",
            "20minutemail.com", "anonbox.net", "byom.de", "crazymailing.com",
            "deadaddress.com", "e4ward.com", "emailfake.com", "fakemailgenerator.com",
            "getairmail.com", "guerrillamailblock.com", "incognitomail.org", "jetable.org",
            "mail-temp.com", "meltmail.com", "mt2015.com", "no-spam.ws",
            "objectmail.com", "onewaymail.com", "pokemail.net", "rcpt.at",
            "spambog.com", "tempemail.net", "tempmail.net", "tempymail.com",
            "yopmail.fr", "yopmail.net", "0-mail.com", "1secmail.com",
        };

        private readonly HashSet<string> _blockedDomains;

        public DisposableEmailService(IConfiguration configuration)
        {
            _blockedDomains = new HashSet<string>(DefaultBlockedDomains, StringComparer.OrdinalIgnoreCase);

            var configured = configuration.GetSection("DisposableEmail:BlockedDomains").Get<string[]>();
            if (configured != null)
            {
                foreach (var domain in configured)
                    if (!string.IsNullOrWhiteSpace(domain))
                        _blockedDomains.Add(domain.Trim());
            }
        }

        /// <summary>True if the email's domain (or a parent of it — e.g.
        /// "sub.mailinator.com" for a "mailinator.com" block entry) is a known
        /// disposable/temporary provider. Case-insensitive.</summary>
        public bool IsDisposable(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;

            var at = email.LastIndexOf('@');
            if (at < 0 || at == email.Length - 1) return false;

            var domain = email[(at + 1)..].Trim().TrimEnd('.');
            if (domain.Length == 0) return false;

            var labels = domain.Split('.');
            // Check the full domain, then each shorter parent suffix in turn, so
            // "mail.mailinator.com" is caught by a "mailinator.com" block entry.
            for (var start = 0; start < labels.Length - 1; start++)
            {
                var candidate = string.Join('.', labels[start..]);
                if (_blockedDomains.Contains(candidate)) return true;
            }
            return false;
        }
    }
}
