using ExamPortal.Models;
using ExamPortal.Services;
using ExamPortal.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPortal.Controllers
{
    [Authorize(Roles = PortalRoles.Recruiter)]
    public class RecruiterController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AssessmentInvitationService _invitations;
        private readonly ActivityFeedService _activityFeed;

        public RecruiterController(AppDbContext db, AssessmentInvitationService invitations, ActivityFeedService activityFeed)
        {
            _db = db;
            _invitations = invitations;
            _activityFeed = activityFeed;
        }

        public IActionResult Index(string q = "", string stage = "", string skill = "", string location = "", int page = 1)
        {
            ViewBag.Assessments = _db.Exams
                .AsNoTracking()
                .Where(e => e.IsActive && !e.IsTemplate)
                .Include(e => e.Questions)
                .Include(e => e.Attempts)
                .OrderBy(e => e.Title)
                .ToList();
            ViewBag.AssessmentTemplates = _db.Exams
                .AsNoTracking()
                .Where(e => e.IsTemplate)
                .OrderBy(e => e.TemplateName)
                .ThenBy(e => e.Title)
                .ToList();

            var candidateQuery = _db.Users
                .AsNoTracking()
                .Include(u => u.ProfileCompletion)
                .Include(u => u.ProfessionalProfile)
                .Include(u => u.Address)
                .Include(u => u.JobPreference)
                .Where(u => u.Role == PortalRoles.Candidate);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                candidateQuery = candidateQuery.Where(u =>
                    u.FullName.ToLower().Contains(term) ||
                    u.Email.ToLower().Contains(term) ||
                    u.CandidateId.ToLower().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(stage))
            {
                // Comma-separated list of stages supported (in addition to the
                // existing single-stage usage from the Stage dropdown) so the
                // pipeline board below can filter by a whole bucket of granular
                // stages (e.g. "Screening" = ProfileUnderReview) in one link.
                var stageList = stage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                candidateQuery = candidateQuery.Where(u => stageList.Contains(u.RecruitmentStage));
            }

            if (!string.IsNullOrWhiteSpace(skill))
            {
                var skillTerm = skill.Trim().ToLower();
                candidateQuery = candidateQuery.Where(u =>
                    (u.ProfessionalProfile != null && u.ProfessionalProfile.Skills.ToLower().Contains(skillTerm)) ||
                    u.Skills.ToLower().Contains(skillTerm));
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                var locationTerm = location.Trim().ToLower();
                candidateQuery = candidateQuery.Where(u =>
                    (u.Address != null && u.Address.City.ToLower().Contains(locationTerm)) ||
                    (u.JobPreference != null && u.JobPreference.PreferredJobLocation.ToLower().Contains(locationTerm)) ||
                    u.CurrentLocation.ToLower().Contains(locationTerm));
            }

            ViewBag.Candidates = _db.Users
                .AsNoTracking()
                .Include(u => u.ProfileCompletion)
                .Where(u => u.Role == PortalRoles.Candidate &&
                            (u.RecruitmentStage == RecruitmentStages.ProfileUnderReview ||
                             u.RecruitmentStage == RecruitmentStages.InvitationSent ||
                             u.RecruitmentStage == RecruitmentStages.AssessmentStarted))
                .OrderBy(u => u.FullName)
                .ToList();
            ViewBag.TotalCandidates = _db.Users.Count(u => u.Role == PortalRoles.Candidate);
            ViewBag.PipelineCounts = _db.Users.AsNoTracking()
                .Where(u => u.Role == PortalRoles.Candidate)
                .GroupBy(u => u.RecruitmentStage)
                .Select(g => new { Stage = g.Key, Count = g.Count() })
                .ToDictionary(g => g.Stage, g => g.Count);
            var pagedCandidates = candidateQuery
                .OrderBy(u => u.FullName)
                .ToPagedResult(page, 20);
            ViewBag.CandidateSearchResults = pagedCandidates;
            ViewBag.CandidateDocuments = _db.CandidateDocuments.AsNoTracking()
                .Where(d => d.DocumentType == "Resume" || d.DocumentType == "Photo" || d.DocumentType == "Certification")
                .OrderByDescending(d => d.UploadedAt)
                .ToList()
                .GroupBy(d => d.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());
            ViewBag.Query = q;
            ViewBag.Stage = stage;
            ViewBag.Skill = skill;
            ViewBag.Location = location;
            ViewBag.PendingInvitations = _db.AssessmentInvitations
                .AsNoTracking()
                .Include(i => i.User)
                .Include(i => i.Exam)
                .Where(i => i.Status == "Pending" || i.Status == "Accepted")
                .OrderByDescending(i => i.SentAt)
                .Take(12)
                .ToList();
            ViewBag.SuspiciousActivities = _db.ProctoringLogs
                .AsNoTracking()
                .Include(l => l.Attempt!).ThenInclude(a => a.User)
                .Include(l => l.Attempt!).ThenInclude(a => a.Exam)
                .Where(l => l.Severity >= 3)
                .OrderByDescending(l => l.OccurredAt)
                .Take(20)
                .ToList();
            ViewBag.UpcomingInterviews = _db.InterviewRecords
                .AsNoTracking()
                .Include(i => i.User)
                .Where(i => i.Status == "Scheduled")
                .OrderBy(i => i.ScheduledAt)
                .Take(12)
                .ToList();
            ViewBag.InterviewHistory = _db.InterviewRecords
                .AsNoTracking()
                .Include(i => i.User)
                .OrderByDescending(i => i.ScheduledAt ?? i.CreatedAt)
                .Take(40)
                .ToList();
            ViewBag.ActiveOffers = _db.OfferLetters
                .AsNoTracking()
                .Include(o => o.User)
                .Where(o => o.Status == "Issued" || o.Status == "Accepted")
                .OrderByDescending(o => o.IssuedAt)
                .Take(12)
                .ToList();
            ViewBag.Reports = _db.AssessmentReports
                .AsNoTracking()
                .Include(r => r.Exam)
                .OrderByDescending(r => r.GeneratedAt)
                .Take(8)
                .ToList();
            ViewBag.Notifications = _db.Notifications
                .AsNoTracking()
                .Include(n => n.User)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .ToList();
            // Note: Recruiter/Index.cshtml renders entirely from the ViewBag data queried
            // directly above/below — it never reads a PortalDashboardViewModel. Calling
            // _portal.BuildDashboard(...) here used to run a second, overlapping set of
            // candidate/interview/offer/notification queries (cached, but still redundant)
            // whose result the view discarded. Removed; EnterprisePortalService is still
            // used as-is by HR's own dashboard.
            //
            // ActivityFeed/RecruiterAlerts replace the dashboard tab's old view-local
            // "recentActivity" merge (notifications+invitations+interviews+offers) — see
            // ActivityFeedService for why that was pulled out of the view.
            ViewBag.ActivityFeed = _activityFeed.BuildRecruiterFeed();
            ViewBag.RecruiterAlerts = _activityFeed.BuildRecruiterAlerts();
            return View();
        }

        [HttpPost]
        public IActionResult AssignAssessment(int examId, int[] candidateIds, DateTimeOffset assessmentDate, int durationMinutes, int passingMarks, int expiryHours = AssessmentInvitationService.DefaultExpiryHours)
        {
            var exam = _db.Exams.Find(examId);
            if (exam == null) return NotFound();
            if (candidateIds == null || candidateIds.Length == 0)
            {
                TempData["Error"] = "Select at least one candidate.";
                return RedirectToAction("Index");
            }

            // assessmentDate is populated client-side (see Index.cshtml script) from the
            // recruiter's local datetime-local picker. If JavaScript didn't run — blocked,
            // disabled, or the request was replayed without a browser — the hidden field
            // posts empty and binds to DateTimeOffset.MinValue instead of throwing. Catch
            // that explicitly rather than silently creating an invitation dated year 0001.
            if (assessmentDate == default)
            {
                TempData["Error"] = "Couldn't read the assessment date/time — please try again. If this keeps happening, make sure JavaScript is enabled.";
                return RedirectToAction("Index");
            }

            // The form posts an unambiguous UTC ISO string (converted client-side from the
            // recruiter's local datetime-local picker — see Index.cshtml script), so this is
            // always the intended instant regardless of the recruiter's or server's timezone.
            // DateTime.SpecifyKind marks it explicitly as UTC since EF/SQL Server round-trips
            // datetime2 values as Kind=Unspecified.
            var assessmentDateUtc = DateTime.SpecifyKind(assessmentDate.UtcDateTime, DateTimeKind.Utc);

            var selectedCandidates = _db.Users
                .Where(u => candidateIds.Contains(u.Id) && u.Role == PortalRoles.Candidate)
                .ToList();

            // The checkbox list on Index.cshtml only lists candidates already at an
            // invitable stage, but that's a UI-level filter only — a direct/replayed POST
            // (or a candidate whose stage changed between page load and submit) could still
            // include an ineligible id. Re-check server-side using the same
            // RecruitmentStages.CanIssueInvitation gate as AdminController.SendInvitation,
            // so both entry points that issue a *new* invitation agree on which stages
            // allow it, and skip anyone who no longer qualifies instead of inviting them.
            var candidates = selectedCandidates
                .Where(u => RecruitmentStages.CanIssueInvitation(u.RecruitmentStage))
                .ToList();
            var skipped = selectedCandidates.Count - candidates.Count;

            // One query for every candidate's revocable invitations instead of one query
            // per candidate inside the loop below (was an N+1 — see Issue()'s
            // previousInvitations parameter).
            var candidateIdSet = candidates.Select(c => c.Id).ToHashSet();
            var revocableStatuses = new[] { "Pending", "Accepted" };
            var previousByCandidate = _db.AssessmentInvitations
                .Where(i => candidateIdSet.Contains(i.UserId) && i.ExamId == examId && revocableStatuses.Contains(i.Status))
                .ToList()
                .GroupBy(i => i.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var candidate in candidates)
            {
                var token = AssessmentInvitationService.GenerateSecureToken();
                var link = Url.Action("AssessmentAuth", "Account", new { token }, Request.Scheme);
                _invitations.Issue(candidate, exam, token, link ?? "",
                    statusesToRevoke: revocableStatuses,
                    assessmentDateUtc: assessmentDateUtc,
                    durationMinutes: durationMinutes,
                    passingMarks: passingMarks,
                    expiryHours: expiryHours,
                    previousInvitations: previousByCandidate.GetValueOrDefault(candidate.Id, new List<AssessmentInvitation>()));
            }

            _db.SaveChanges();

            if (candidates.Count == 0)
            {
                TempData["Error"] = "None of the selected candidates are at a stage that allows a new assessment invitation.";
            }
            else
            {
                TempData["Success"] = skipped > 0
                    ? $"Assessment invitation sent to {candidates.Count} candidate(s). Skipped {skipped} no longer eligible for a new invitation."
                    : $"Assessment invitation sent to {candidates.Count} candidate(s).";
            }
            return RedirectToAction("Index");
        }
    }
}
