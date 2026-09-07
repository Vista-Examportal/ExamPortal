namespace ExamPortal.Services
{
    /// <summary>
    /// Pure, dependency-free logic for the candidate document-verification workflow —
    /// see AdminController.VerifyDocument, the one caller. Upload-side rules (can this
    /// specific document be replaced right now) live directly in
    /// HomeController.UploadDocument instead, since they're a simpler single-document
    /// check that doesn't benefit from being extracted the same way.
    /// </summary>
    public static class DocumentVerificationHelper
    {
        /// <summary>
        /// True once every <see cref="ExamPortal.Models.DocumentTypes.Required"/> type has
        /// a Verified <see cref="ExamPortal.Models.CandidateDocument"/> among `documents`.
        /// An unrelated/optional type (e.g. "Other") sitting at any status never blocks
        /// this — it's simply not part of the required set — and a required type with no
        /// document at all (never uploaded) correctly blocks it, unlike a blind
        /// `documents.All(d => d.VerificationStatus == "Verified")` over every row, which
        /// is vacuously true once the required set is filtered down to nothing and can
        /// also be blocked forever by an unrelated Pending document.
        /// </summary>
        public static bool AreAllRequiredDocumentsVerified(IEnumerable<ExamPortal.Models.CandidateDocument> documents)
        {
            var docs = documents as IReadOnlyCollection<ExamPortal.Models.CandidateDocument> ?? documents.ToList();
            return ExamPortal.Models.DocumentTypes.Required.All(requiredType =>
                docs.Any(d => d.DocumentType == requiredType && d.VerificationStatus == "Verified"));
        }

        /// <summary>
        /// True if a candidate may upload/replace a document currently at
        /// `existingVerificationStatus` (null/empty means no document exists yet for this
        /// type — a brand-new upload). Only that "nothing yet" case and "Rejected" allow
        /// it: "Pending" is under review and "Verified" is locked, and neither may be
        /// replaced by the candidate. See HomeController.UploadDocument, the one caller —
        /// this only covers the single-document decision; whether uploads are open for
        /// this candidate/document type/stage at all is checked separately there.
        /// </summary>
        public static bool CanCandidateUploadDocument(string? existingVerificationStatus) =>
            string.IsNullOrEmpty(existingVerificationStatus) || existingVerificationStatus == "Rejected";
    }
}
