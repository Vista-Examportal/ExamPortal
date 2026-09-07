using System.Collections.Generic;
using ExamPortal.Models;
using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class DocumentVerificationHelperTests
    {
        private static CandidateDocument MakeDoc(string type, string status) =>
            new() { DocumentType = type, VerificationStatus = status };

        // Experience Letter is intentionally NOT in this list — it's optional (only
        // applies to candidates who actually have prior work experience), so it must
        // never be required for DocumentsVerified. See DocumentTypes.Required.
        private static readonly string[] AllRequiredTypes =
        {
            DocumentTypes.Aadhar, DocumentTypes.Pan, DocumentTypes.MarkSheet10,
            DocumentTypes.MarkSheet12, DocumentTypes.Degree,
        };

        [Fact]
        public void AllFiveRequiredVerified_ReturnsTrue()
        {
            var docs = AllRequiredTypes.Select(t => MakeDoc(t, "Verified")).ToList();

            Assert.True(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(docs));
        }

        [Fact]
        public void FourVerifiedOnePending_ReturnsFalse()
        {
            // TEST 7 from the ticket, adapted to the 5-item required set: 4 verified + 1
            // pending → still DocumentsRequested.
            var docs = new List<CandidateDocument>
            {
                MakeDoc(DocumentTypes.Aadhar, "Verified"),
                MakeDoc(DocumentTypes.Pan, "Verified"),
                MakeDoc(DocumentTypes.MarkSheet10, "Verified"),
                MakeDoc(DocumentTypes.MarkSheet12, "Verified"),
                MakeDoc(DocumentTypes.Degree, "Pending"),
            };

            Assert.False(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(docs));
        }

        [Fact]
        public void RequiredDocumentNeverUploaded_ReturnsFalse()
        {
            // Only 4 of the 5 required types have a row at all — the missing one must
            // block, not be silently skipped the way a naive `docs.All(...)` filtered
            // down to an empty set would (vacuously true).
            var docs = AllRequiredTypes.Take(4).Select(t => MakeDoc(t, "Verified")).ToList();

            Assert.False(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(docs));
        }

        [Fact]
        public void UnrelatedOptionalPendingDocument_DoesNotBlock()
        {
            // TEST 11 from the ticket: all required verified + an old unrelated Pending
            // "Other" document → still DocumentsVerified.
            var docs = AllRequiredTypes.Select(t => MakeDoc(t, "Verified")).ToList();
            docs.Add(MakeDoc(DocumentTypes.Other, "Pending"));

            Assert.True(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(docs));
        }

        [Theory]
        [InlineData("Pending")]
        [InlineData("Rejected")]
        public void ExperienceLetterNotVerified_DoesNotBlockDocumentsVerified(string experienceLetterStatus)
        {
            // The whole point of this fix: Experience Letter is optional-if-applicable,
            // so a candidate with no prior work experience — or one whose Experience
            // Letter is still Pending/Rejected — must still be able to reach
            // DocumentsVerified once the other 5 required documents are Verified.
            var docs = AllRequiredTypes.Select(t => MakeDoc(t, "Verified")).ToList();
            docs.Add(MakeDoc(DocumentTypes.ExperienceLetter, experienceLetterStatus));

            Assert.True(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(docs));
        }

        [Fact]
        public void ExperienceLetterNeverUploadedAtAll_DoesNotBlockDocumentsVerified()
        {
            // The most common real case: a fresher with no prior job never uploads an
            // Experience Letter at all.
            var docs = AllRequiredTypes.Select(t => MakeDoc(t, "Verified")).ToList();

            Assert.True(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(docs));
        }

        [Fact]
        public void NoDocumentsAtAll_ReturnsFalse()
        {
            Assert.False(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(new List<CandidateDocument>()));
        }

        [Fact]
        public void OneRequiredDocumentRejected_ReturnsFalse()
        {
            var docs = AllRequiredTypes.Select(t => MakeDoc(t, "Verified")).ToList();
            docs[0].VerificationStatus = "Rejected";

            Assert.False(DocumentVerificationHelper.AreAllRequiredDocumentsVerified(docs));
        }

        // ── CanCandidateUploadDocument ───────────────────────────────────────────
        // Regression coverage for the fix to a real security/integrity bug: a
        // candidate could previously replace an already-Verified document, and
        // repeatedly re-upload while a document was still Pending review.

        [Fact]
        public void NoExistingDocument_CanUpload()
        {
            Assert.True(DocumentVerificationHelper.CanCandidateUploadDocument(null));
            Assert.True(DocumentVerificationHelper.CanCandidateUploadDocument(""));
        }

        [Fact]
        public void RejectedDocument_CanUpload()
        {
            // TEST 4/5 from the ticket: rejected → candidate can re-upload → Pending.
            Assert.True(DocumentVerificationHelper.CanCandidateUploadDocument("Rejected"));
        }

        [Fact]
        public void VerifiedDocument_CannotUpload()
        {
            // TEST 2/3 from the ticket: verified → candidate cannot replace it.
            Assert.False(DocumentVerificationHelper.CanCandidateUploadDocument("Verified"));
        }

        [Fact]
        public void PendingDocument_CannotUpload()
        {
            // TEST 6 from the ticket: no uncontrolled duplicate/repeated uploads while
            // a document is still under review.
            Assert.False(DocumentVerificationHelper.CanCandidateUploadDocument("Pending"));
        }
    }
}
