namespace ExamPortal.Services
{
    /// <summary>
    /// Small shared string-sanitization helpers used before writing free-text input to the
    /// database (defending against arbitrary-length input reaching nvarchar columns/emails).
    /// Extracted from near-identical private <c>Clamp</c> methods previously copy-pasted in
    /// both <c>AdminController</c> and <c>ExamController</c>.
    /// </summary>
    public static class TextUtils
    {
        /// <summary>
        /// Truncates a string to at most <paramref name="maxLength"/> characters. Treats null
        /// as "". Set <paramref name="trim"/> to true to also trim leading/trailing whitespace
        /// first — AdminController's admin-entered fields (names, emails, notes) do this;
        /// ExamController's exam-answer fields (free text, source code) deliberately don't,
        /// since leading/trailing whitespace can be meaningful in a candidate's code submission.
        /// </summary>
        public static string Clamp(string? value, int maxLength, bool trim = false)
        {
            var v = trim ? (value ?? "").Trim() : (value ?? "");
            return v.Length > maxLength ? v[..maxLength] : v;
        }
    }
}
