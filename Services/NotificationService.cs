using ExamPortal.Configuration;
using ExamPortal.Data;
using ExamPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace ExamPortal.Services
{
    public class NotificationService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EmailOptions _emailOptions;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            AppDbContext db,
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            IOptions<EmailOptions> emailOptions,
            ILogger<NotificationService> logger)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _emailOptions = emailOptions.Value;
            _logger = logger;
        }

        public void Queue(User candidate, Exam? assessment, string type, string subject, string body)
        {
            QueueChannel(candidate, assessment, type, "Email", candidate.Email, subject, body);
            QueueChannel(candidate, assessment, type, "InApp", candidate.Username, subject, body);
        }

        /// <summary>
        /// Same as <see cref="Queue"/>, but lets the caller send different content to the
        /// Email channel vs. the InApp channel — used by AssessmentInvitationService, whose
        /// Email body is a full corporate HTML document (rendered as-is by SendGmail; see
        /// the "Assessment Invitation" check in DispatchOneAsync below) while the InApp
        /// body stays a short plain-text summary suitable for the dashboard's notification
        /// preview list (Views/Home/Index.cshtml truncates NotificationMessage.Body to
        /// ~140 chars — raw HTML there would show as escaped markup, not a real preview).
        /// </summary>
        public void Queue(User candidate, Exam? assessment, string type, string subject, string emailBody, string inAppBody)
        {
            QueueChannel(candidate, assessment, type, "Email", candidate.Email, subject, emailBody);
            QueueChannel(candidate, assessment, type, "InApp", candidate.Username, subject, inAppBody);
        }

        public void QueueWithAttachment(User candidate, Exam? assessment, string type,
            string subject, string body, byte[] attachmentBytes, string attachmentFileName)
        {
            QueueChannel(candidate, assessment, type, "Email", candidate.Email, subject, body, attachmentBytes, attachmentFileName);
            QueueChannel(candidate, assessment, type, "InApp", candidate.Username, subject, body);
        }

        public void QueueChannel(User candidate, Exam? assessment, string type, string channel,
            string recipient, string subject, string body, byte[]? attachmentBytes = null, string attachmentFileName = "")
        {
            _db.Notifications.Add(new NotificationMessage
            {
                UserId = candidate.Id,
                ExamId = assessment?.Id,
                Type = type,
                Channel = channel,
                Recipient = recipient,
                Subject = subject,
                Body = body,
                Status = channel == "InApp" ? "Sent" : "Queued",
                SentAt = channel == "InApp" ? DateTime.UtcNow : null,
                AttachmentBytes = attachmentBytes,
                AttachmentFileName = attachmentFileName,
                CreatedAt = DateTime.UtcNow
            });
        }

        public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var messages = await _db.Notifications
                .Where(n =>
                    (n.Status == "Queued" || n.Status == "Failed") &&
                    n.RetryCount < n.MaxRetries &&
                    (n.NextRetryAt == null || n.NextRetryAt <= now))
                .OrderBy(n => n.CreatedAt)
                .Take(25)
                .ToListAsync(cancellationToken);

            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DispatchOneAsync(message, cancellationToken);
            }

            if (messages.Count > 0)
                await _db.SaveChangesAsync(cancellationToken);

            return messages.Count;
        }

        public async Task RetryFailedAsync(int? notificationId = null)
        {
            var query = _db.Notifications.Where(n => n.Status == "Failed");
            if (notificationId.HasValue) query = query.Where(n => n.Id == notificationId.Value);

            var failed = await query.ToListAsync();
            foreach (var notification in failed)
            {
                notification.Status = "Queued";
                notification.NextRetryAt = DateTime.UtcNow;
                notification.ErrorMessage = "";
            }
            await _db.SaveChangesAsync();
        }

        public async Task MarkReadAsync(int notificationId, int userId)
        {
            var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
            if (notification == null) return;

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task MarkAllReadAsync(int userId)
        {
            var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
            foreach (var notification in unread)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }

        private async Task DispatchOneAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            message.Status = "Processing";
            message.LastAttemptAt = DateTime.UtcNow;

            try
            {
                switch (message.Channel)
                {
                    case "Email":
                        // Every other notification Type queues a plain-text body that
                        // SendGmail wraps via BuildEmailBody (encodes it and renders inside
                        // a <pre> block) — that behavior is unchanged. "Assessment
                        // Invitation" is the one exception: AssessmentInvitationService
                        // already builds a complete, self-contained HTML email (see
                        // Services/AssessmentInvitationService.cs), so it's sent as-is.
                        var isPrebuiltHtml = message.Type == "Assessment Invitation";
                        SendGmail(message.Recipient, message.Subject, message.Body, message.AttachmentBytes, message.AttachmentFileName, rawHtmlBody: isPrebuiltHtml);
                        break;
                    case "SMS":
                        await SendProviderWebhookAsync("Sms", message, cancellationToken);
                        break;
                    case "WhatsApp":
                        await SendProviderWebhookAsync("WhatsApp", message, cancellationToken);
                        break;
                    case "Push":
                        await SendProviderWebhookAsync("Push", message, cancellationToken);
                        break;
                    case "InApp":
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported notification channel: {message.Channel}");
                }

                message.Status = "Sent";
                message.SentAt = DateTime.UtcNow;
                message.ErrorMessage = "";
                message.ProviderMessageId = string.IsNullOrWhiteSpace(message.ProviderMessageId)
                    ? $"{message.Channel}-{Guid.NewGuid():N}"
                    : message.ProviderMessageId;
                _logger.LogInformation("Notification sent via {Channel} to {Recipient}: {Subject}", message.Channel, message.Recipient, message.Subject);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Status = "Failed";
                message.ErrorMessage = ex.Message;
                message.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, message.RetryCount)));
                _logger.LogError(ex, "Notification dispatch failed via {Channel} to {Recipient}", message.Channel, message.Recipient);
            }
        }

        private async Task SendProviderWebhookAsync(string providerSection, NotificationMessage message, CancellationToken cancellationToken)
        {
            var endpoint = _config[$"{providerSection}:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException($"{providerSection} provider is not configured. Set {providerSection}:Endpoint.");

            var client = _httpClientFactory.CreateClient("NotificationProvider");
            var payload = new
            {
                to = message.Recipient,
                subject = message.Subject,
                body = message.Body,
                type = message.Type,
                notificationId = message.Id
            };
            var response = await client.PostAsJsonAsync(endpoint, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            message.ProviderMessageId = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        /// <summary>
        /// Sends a Contact Us submission straight to the recruitment inbox. Deliberately
        /// bypasses Queue/QueueChannel above — those require a UserId (see
        /// NotificationMessage.UserId), because they're for notifying an existing
        /// candidate about their application. A public Contact Us message comes from an
        /// anonymous visitor with no User row, so there's nothing to attach a queued
        /// notification to; this sends synchronously instead and returns success/failure
        /// so the controller can show the visitor a real result rather than a queued
        /// message that might silently fail later with nobody able to see it.
        /// </summary>
        public bool TrySendContactFormMessage(string visitorName, string visitorEmail, string? visitorPhone, string message, out string error)
        {
            var recipient = string.IsNullOrWhiteSpace(_emailOptions.ContactInboxEmail)
                ? _emailOptions.SenderEmail
                : _emailOptions.ContactInboxEmail;

            var subject = $"Contact Us: {visitorName}";
            var bodyLines = new List<string>
            {
                $"Name: {visitorName}",
                $"Email: {visitorEmail}",
            };
            if (!string.IsNullOrWhiteSpace(visitorPhone))
                bodyLines.Add($"Phone: {visitorPhone}");
            bodyLines.Add("");
            bodyLines.Add(message);
            var body = string.Join(Environment.NewLine, bodyLines);

            try
            {
                SendGmail(recipient, subject, body, replyTo: visitorEmail);
                _logger.LogInformation("Contact Us message sent to {Recipient} from {VisitorEmail}", recipient, visitorEmail);
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Contact Us message failed to send from {VisitorEmail}", visitorEmail);
                error = ex.Message;
                return false;
            }
        }

        private void SendGmail(string toEmail, string subject, string body,
            byte[]? attachmentBytes = null, string? attachmentFileName = null, string? replyTo = null, bool rawHtmlBody = false)
        {
            var senderEmail = _emailOptions.SenderEmail;
            var appPassword = _emailOptions.Password;

            if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(appPassword)
                || senderEmail == "your-gmail@gmail.com" || appPassword == "your-gmail-app-password")
            {
                throw new InvalidOperationException(
                    "Email credentials are not configured. " +
                    "Set Email__SenderEmail and Email__Password in appsettings.Development.json " +
                    "or as environment variables. " +
                    "For Gmail: enable 2-Step Verification, then create a 16-character App Password at " +
                    "https://myaccount.google.com/apppasswords and paste it into Email__Password.");
            }

            using var client = new SmtpClient(_emailOptions.SmtpHost, _emailOptions.SmtpPort)
            {
                EnableSsl = _emailOptions.EnableSsl,
                Credentials = new NetworkCredential(senderEmail, appPassword),
                Timeout = 15_000
            };

            // rawHtmlBody: body is already a complete, self-contained HTML document (built
            // by the caller, e.g. AssessmentInvitationService) — send it unchanged. Every
            // other notification type still goes through BuildEmailBody, which HTML-encodes
            // the plain-text body and renders it inside the shared "From/To/Date/Subject"
            // chrome — that path and its output are completely unchanged.
            var htmlBody = rawHtmlBody ? body : BuildEmailBody(toEmail, senderEmail, subject, body, DateTime.UtcNow);
            using var mail = new MailMessage
            {
                From = new MailAddress(senderEmail, _emailOptions.SenderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);
            if (!string.IsNullOrWhiteSpace(replyTo))
                mail.ReplyToList.Add(new MailAddress(replyTo));

            if (attachmentBytes != null && !string.IsNullOrWhiteSpace(attachmentFileName))
            {
                using var stream = new MemoryStream(attachmentBytes);
                mail.Attachments.Add(new Attachment(stream, attachmentFileName, "application/pdf"));
                client.Send(mail);
            }
            else
            {
                client.Send(mail);
            }
        }

        public static string BuildEmailBody(
            string toEmail,
            string senderEmail,
            string subject,
            string bodyHtml,
            DateTime sentAt)
        {
            var dateStr = sentAt.ToString("ddd, dd MMM yyyy HH:mm:ss") + " UTC";

            // SECURITY: all values below originate from user-controlled or DB-stored input
            // (candidate names, free-text remarks, etc. flow into subject/body upstream).
            // HTML-encode before interpolating into the email markup to prevent HTML/script
            // injection into outgoing emails and the stored NotificationMessage.Body.
            var safeToEmail    = System.Net.WebUtility.HtmlEncode(toEmail);
            var safeSenderEmail = System.Net.WebUtility.HtmlEncode(senderEmail);
            var safeSubject    = System.Net.WebUtility.HtmlEncode(subject);
            var safeBody       = System.Net.WebUtility.HtmlEncode(bodyHtml);

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""UTF-8"" /><meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" /><title>{safeSubject}</title></head>
<body style=""font-family:Arial,sans-serif;background:#f4f4f4;margin:0;padding:0;"">
  <div style=""max-width:640px;margin:32px auto;background:#ffffff;border:1px solid #ddd;border-radius:6px;overflow:hidden;"">
    <div style=""background:#1a1a2e;padding:20px 28px;""><h1 style=""color:#ffffff;font-size:20px;margin:0;letter-spacing:1px;"">VISTAWAYS TECH</h1></div>
    <div style=""background:#f0f0f0;padding:12px 28px;border-bottom:1px solid #ddd;font-size:12px;color:#555;"">
      <table style=""border-collapse:collapse;width:100%;"">
        <tr><td style=""width:80px;font-weight:bold;color:#333;"">From:</td><td>VISTAWAYS TECH Recruitment &lt;{safeSenderEmail}&gt;</td></tr>
        <tr><td style=""width:80px;font-weight:bold;color:#333;"">To:</td><td>{safeToEmail}</td></tr>
        <tr><td style=""width:80px;font-weight:bold;color:#333;"">Date:</td><td>{dateStr}</td></tr>
        <tr><td style=""width:80px;font-weight:bold;color:#333;"">Subject:</td><td><strong>{safeSubject}</strong></td></tr>
      </table>
    </div>
    <div style=""padding:28px;color:#333;font-size:14px;line-height:1.7;""><pre style=""white-space:pre-wrap;font-family:inherit;margin:0;"">{safeBody}</pre></div>
    <div style=""background:#f7f7f7;border-top:1px solid #e0e0e0;padding:16px 28px;font-size:11px;color:#888;"">
      <p><strong>VISTAWAYS TECH Recruitment Team</strong><br />Email: {safeSenderEmail}</p>
      <p>This email was sent to <strong>{safeToEmail}</strong> as part of your application process. Do not share your Candidate ID or password with anyone.</p>
      <p>&copy; {sentAt.Year} VISTAWAYS TECH. All rights reserved.</p>
    </div>
  </div>
</body>
</html>";
        }
    }

    public class NotificationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NotificationBackgroundService> _logger;

        public NotificationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<NotificationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                    await notifications.DispatchPendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Notification background dispatch loop failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
