using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ExamPortal.Services
{
    /// <summary>
    /// Plain-text alternative for candidate-facing emails (sent as the text/plain part next to the
    /// HTML part). Same wording as the HTML version — nothing is added or reworded, apart from the
    /// company footer lines, which are also in the HTML.
    /// </summary>
    public static class EmailPlainText
    {
        /// <summary>Plain-text version of a message sent through the shared layout.</summary>
        public static string FromMessage(string message, string toEmail)
        {
            var text = (message ?? "").Replace("\r\n", "\n").Trim('\n');
            var sb = new StringBuilder(text)
                .Append("\n\n")
                .Append(EmailFooter.BuildPlainText())
                .Append("\n\nEmail: ").Append(EmailFooter.CompanyEmail)
                .Append("\nThis email was sent to ").Append(toEmail)
                .Append(" as part of your application process. Do not share your Candidate ID or password with anyone.");
            return ToCrlf(sb.ToString());
        }

        /// <summary>Plain-text version of a prebuilt HTML email (the assessment invitation).</summary>
        public static string FromHtml(string html)
        {
            var t = Regex.Replace(html ?? "", "<!--.*?-->", "", RegexOptions.Singleline);
            t = Regex.Replace(t, @"<(head|style|script)\b.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            // the logo image becomes the company name
            t = Regex.Replace(t, @"<img\b[^>]*" + Regex.Escape(EmailFooter.LogoCidReference) + @"[^>]*>",
                EmailFooter.CompanyName + "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<br\s*/?>|</(p|div|tr|li|h\d|table|ul)>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"</t[dh]>", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", "");
            t = WebUtility.HtmlDecode(t);

            var lines = t.Replace("\r\n", "\n").Split('\n')
                .Select(l => Regex.Replace(l, @"[ \t\u00a0]+", " ").Trim());
            var sb = new StringBuilder();
            var blank = true; // swallow leading blank lines
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    if (!blank) sb.Append('\n');
                    blank = true;
                    continue;
                }
                sb.Append(line).Append('\n');
                blank = false;
            }
            return ToCrlf(sb.ToString().TrimEnd('\n'));
        }

        private static string ToCrlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
}
