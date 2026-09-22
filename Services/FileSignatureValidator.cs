namespace ExamPortal.Services
{
    /// <summary>
    /// Verifies that a file's actual content matches its claimed extension, by checking
    /// well-known magic-byte signatures. Extension and the Content-Type header are both
    /// entirely client-supplied and trivially spoofed in a multipart form request — neither
    /// alone proves a file really is a PDF/JPEG/PNG/WebP, only that it claims to be one.
    ///
    /// Called after the existing extension/Content-Type/size checks, before the file is
    /// handed to FileStorageService.SavePrivateAsync (see
    /// AccountController.Registration.cs SaveResumeAsync/SaveProfilePhotoAsync, and
    /// HomeController.UploadDocument).
    ///
    /// This is a signature check, not a full structural validation (it won't catch every
    /// possible corrupt-but-correctly-headed file) — but it reliably rejects the common
    /// case this system previously had no defense against: a renamed executable, script,
    /// or arbitrary binary uploaded with a spoofed extension and Content-Type.
    /// </summary>
    public static class FileSignatureValidator
    {
        private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] RiffTag = "RIFF"u8.ToArray();
        private static readonly byte[] WebpTag = "WEBP"u8.ToArray();

        /// <summary>
        /// Reads just enough of the file's content to check its signature.
        /// IFormFile.OpenReadStream() returns a fresh, independently-positioned stream on
        /// each call, so this doesn't consume or affect any later read/copy of the same
        /// file (e.g. FileStorageService.SavePrivateAsync's own CopyToAsync).
        /// </summary>
        public static async Task<bool> MatchesExtensionAsync(IFormFile file, string extension, CancellationToken cancellationToken = default)
        {
            extension = extension.ToLowerInvariant();
            // Nothing here needs more than 12 bytes (WebP's signature is the longest: a
            // 4-byte "RIFF" tag, a 4-byte size field, then a 4-byte "WEBP" tag at offset 8).
            var header = new byte[12];
            await using var stream = file.OpenReadStream();
            var read = await ReadFullyAsync(stream, header, cancellationToken);

            return extension switch
            {
                ".pdf" => read >= PdfSignature.Length && header.AsSpan(0, PdfSignature.Length).SequenceEqual(PdfSignature),
                ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".png" => read >= PngSignature.Length && header.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature),
                ".webp" => read >= 12
                    && header.AsSpan(0, 4).SequenceEqual(RiffTag)
                    && header.AsSpan(8, 4).SequenceEqual(WebpTag),
                // An extension this validator doesn't know how to check — the caller's own
                // extension allow-list is responsible for rejecting anything unexpected
                // before this is ever reached, so there's nothing further to verify here.
                _ => true
            };
        }

        /// <summary>Stream.ReadAsync isn't guaranteed to fill the buffer in one call (short
        /// reads are legal, especially for network-backed streams) — this loops until the
        /// buffer is full or the stream ends, returning the actual number of bytes read.</summary>
        private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
                if (read == 0) break; // end of stream
                totalRead += read;
            }
            return totalRead;
        }
    }
}
