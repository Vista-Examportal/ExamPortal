namespace ExamPortal.Services
{
    /// <summary>
    /// The company signature block appended to outgoing application emails: logo, company name,
    /// address, and website. One definition, used by the shared layout
    /// (NotificationService.BuildEmailBody) and by the assessment invitation template
    /// (AssessmentInvitationService), which has its own layout.
    ///
    /// The logo is referenced by Content-ID (cid:) and attached inline by
    /// NotificationService.SendGmail, so it needs no public URL or configuration and does not
    /// depend on the recipient's client loading remote images. If the image is blocked or the
    /// file is unavailable, the alt text and the text lines below still carry the same information.
    /// Deliberately contains no individual's name or phone numbers.
    /// </summary>
    public static class EmailFooter
    {
        /// <summary>Content-ID the inline logo is attached under.</summary>
        public const string LogoContentId = "vwtech-logo";

        /// <summary>What appears in the HTML where the logo is referenced.</summary>
        public const string LogoCidReference = "cid:" + LogoContentId;

        /// <summary>The existing site logo (wwwroot/logo-full.png: dark text on a transparent
        /// background, i.e. the variant used on light pages — emails are white).</summary>
        public const string LogoFileName = "logo-full.png";

        public const string LogoAltText = "VISTAWAYS TECH \u2014 Innovation. Integration. Impact.";
        public const string CompanyName = "VISTAWAYSTECH LLP";
        public const string AddressLine1 = "621, 6th Floor, Manjeera Majestic Commercials,";
        public const string AddressLine2 = "Opp. JNTU, KPHB, Hyderabad-500072";
        public const string WebsiteText = "www.vistawaystech.com";
        public const string WebsiteUrl = "https://www.vistawaystech.com";

        // The logo is a transparent PNG with dark lettering, so it sits on an explicit white
        // backing: mail apps in dark mode darken the page but not images, and without this the
        // "VISTAWAYS TECH" wordmark would all but vanish.
        // The logo file is 2047x561; shown at 180px wide so it matches the email's scale.
        private const int LogoWidth = 180;
        private const int LogoHeight = 49;

        /// <summary>The signature block as email-safe HTML (table layout, inline styles). The
        /// caller supplies any surrounding padding, since the two email layouts differ.</summary>
        public static string BuildHtml() =>
            $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""border-collapse:collapse;"">
  <tr><td bgcolor=""#ffffff"" style=""background-color:#ffffff;padding:4px 10px 8px 0;"">
    <img src=""{LogoCidReference}"" alt=""{LogoAltText}"" width=""{LogoWidth}"" height=""{LogoHeight}"" style=""display:block;border:0;width:{LogoWidth}px;height:auto;background-color:#ffffff;font-family:Arial,Helvetica,sans-serif;font-size:13px;font-weight:bold;color:#1a1a2e;"" />
  </td></tr>
  <tr><td style=""font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:1.5;color:#555;"">
    {AddressLine1}<br />
    {AddressLine2}<br />
    <a href=""{WebsiteUrl}"" style=""color:#4a4ad0;"">{WebsiteText}</a>
  </td></tr>
</table>";

        /// <summary>Replaces the logo <c>&lt;img&gt;</c> with the company name as text. Used when the
        /// logo file isn't available at send time, so recipients don't see a broken-image icon and
        /// the footer still names the company (with the logo shown, the name is part of the logo,
        /// so it isn't repeated as text).</summary>
        public static string RemoveLogo(string html) =>
            System.Text.RegularExpressions.Regex.Replace(
                html,
                @"<img\b[^>]*" + System.Text.RegularExpressions.Regex.Escape(LogoCidReference) + @"[^>]*>",
                "<strong style=\"font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#1a1a2e;\">" + CompanyName + "</strong>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
