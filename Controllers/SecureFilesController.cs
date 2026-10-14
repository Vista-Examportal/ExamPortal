using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExamPortal.Controllers
{
    [Authorize]
    [Route("secure-files")]
    public class SecureFilesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly FileStorageService _files;

        public SecureFilesController(AppDbContext db, FileStorageService files)
        {
            _db = db;
            _files = files;
        }

        [HttpGet("{folder}/{fileName}")]
        public async Task<IActionResult> Get(string folder, string fileName)
        {
            var relativePath = $"/secure-files/{Path.GetFileName(folder)}/{Path.GetFileName(fileName)}";
            if (!await CanAccessAsync(relativePath)) return Forbid();

            var absolutePath = _files.ResolvePrivatePath(folder, fileName);
            if (!System.IO.File.Exists(absolutePath)) return NotFound();

            var contentType = _files.GetContentTypeFromFileName(fileName);
            return PhysicalFile(absolutePath, contentType, enableRangeProcessing: true);
        }

        private async Task<bool> CanAccessAsync(string path)
        {
            // Admin and HR review every candidate file (including identity/education proofs).
            if (User.IsInRole(PortalRoles.Admin) || User.IsInRole(PortalRoles.Hr))
                return true;

            // Recruiters screen on resume / photo / certifications only (DocumentTypes.RecruiterVisible,
            // the same types their dashboard lists). Anything else — Aadhar, PAN, marksheets, degree,
            // experience letters — is HR/Admin document verification, so a direct URL must not open it.
            if (User.IsInRole(PortalRoles.Recruiter))
            {
                return await _db.Users.AsNoTracking().AnyAsync(u => u.ResumePath == path || u.ProfilePhotoPath == path)
                    || await _db.CandidateDocuments.AsNoTracking().AnyAsync(d =>
                        d.FilePath == path && DocumentTypes.RecruiterVisible.Contains(d.DocumentType));
            }

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId)) return false;

            return await _db.Users.AsNoTracking().AnyAsync(u =>
                    u.Id == userId && (u.ResumePath == path || u.ProfilePhotoPath == path))
                || await _db.CandidateDocuments.AsNoTracking().AnyAsync(d =>
                    d.UserId == userId && d.FilePath == path);
        }
    }
}
