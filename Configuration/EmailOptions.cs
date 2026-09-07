namespace ExamPortal.Configuration
{
    /// <summary>
    /// Outbound-email (SMTP) credentials and settings, used by
    /// Services/NotificationService.cs to send candidate/staff notifications.
    /// Bound from the "Email" configuration section.
    ///
    /// Values are never hardcoded. In override order they come from:
    /// appsettings.json -> appsettings.{Environment}.json -> .NET User Secrets
    /// -> environment variables (Email__SenderEmail / Email__Password / etc.)
    /// -> Azure Key Vault, when configured. The default appsettings.json ships
    /// with placeholder values only ("your-gmail@gmail.com" / "your-gmail-app-password");
    /// NotificationService explicitly refuses to send while those placeholders are
    /// still in place.
    /// </summary>
    public class EmailOptions
    {
        public const string SectionName = "Email";

        public string SenderEmail { get; set; } = "";
        public string SenderName { get; set; } = "VISTAWAYS TECH";
        public string Password { get; set; } = "";
        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;

        /// <summary>Where Contact Us submissions (Controllers/SiteController.cs) get
        /// delivered. Falls back to SenderEmail in NotificationService if left blank.</summary>
        public string ContactInboxEmail { get; set; } = "info@vistawaystech.com";
    }
}
