using System.Security.Cryptography;

namespace ExamPortal.Services
{
    public class StoredFileResult
    {
        public string PublicPath { get; set; } = "";
        public string AbsolutePath { get; set; } = "";
        public string FileHash { get; set; } = "";
        public string ContentType { get; set; } = "application/octet-stream";
    }

    public class FileStorageService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

        public FileStorageService(IWebHostEnvironment environment, IConfiguration configuration)
        {
            _environment = environment;
            _configuration = configuration;
        }

        public async Task<StoredFileResult> SavePrivateAsync(IFormFile file, string folder, string extension, CancellationToken cancellationToken = default)
        {
            extension = extension.ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var absoluteDirectory = GetPrivateFolder(folder);
            Directory.CreateDirectory(absoluteDirectory);

            var absolutePath = Path.Combine(absoluteDirectory, fileName);
            await using (var stream = File.Create(absolutePath))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            return new StoredFileResult
            {
                AbsolutePath = absolutePath,
                PublicPath = $"/secure-files/{folder}/{fileName}",
                FileHash = await ComputeSha256Async(absolutePath, cancellationToken),
                ContentType = GetContentType(extension)
            };
        }

        public string ResolvePrivatePath(string folder, string fileName)
        {
            var safeFolder = Path.GetFileName(folder);
            var safeFile = Path.GetFileName(fileName);
            return Path.Combine(GetPrivateFolder(safeFolder), safeFile);
        }

        public string GetContentTypeFromFileName(string fileName) => GetContentType(Path.GetExtension(fileName));

        /// <summary>Creates the private upload subfolders (resumes, profile photos,
        /// documents, certifications) if they don't already exist. Called once at
        /// startup from Program.cs — new candidate files are served through
        /// SecureFilesController rather than from public wwwroot static files, so these
        /// directories live under the same PrivateRoot that GetPrivateFolder resolves.</summary>
        public void EnsureDirectoriesExist()
        {
            var uploadDirs = new[] { "resumes", "profile-photos", "documents", "certifications" };
            foreach (var dir in uploadDirs)
                Directory.CreateDirectory(GetPrivateFolder(dir));
        }

        private string GetPrivateFolder(string folder)
        {
            var configuredRoot = _configuration["FileStorage:PrivateRoot"];
            var root = string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.Combine(_environment.ContentRootPath, "App_Data", "uploads")
                : configuredRoot;

            return Path.Combine(root, Path.GetFileName(folder));
        }

        private static string GetContentType(string extension) =>
            ContentTypes.TryGetValue(extension, out var contentType) ? contentType : "application/octet-stream";

        private static async Task<string> ComputeSha256Async(string absolutePath, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(absolutePath);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }
    }
}
