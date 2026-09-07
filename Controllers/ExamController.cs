using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
    [Authorize(Roles = PortalRoles.Candidate)]
    public class ExamController : Controller
    {
        private readonly AppDbContext _db;
        private readonly EligibilityService _eligibility;
        private readonly NotificationService _notifications;
        private readonly AssessmentScoringService _scoring;
        private readonly AuditService _audit;

        public ExamController(AppDbContext db, EligibilityService eligibility, NotificationService notifications, AssessmentScoringService scoring, AuditService audit)
        {
            _db = db;
            _eligibility = eligibility;
            _notifications = notifications;
            _scoring = scoring;
            _audit = audit;
        }

        private int UserId => User.GetUserId();

        /// <summary>
        /// Recruitment stages at or beyond AssessmentCompleted (or Rejected) — once a
        /// candidate reaches one of these, a retake requires a fresh admin invitation.
        /// </summary>
        private static readonly string[] LockedStages =
        {
            "AssessmentCompleted", "Shortlisted", "InterviewInProgress",
            "DocumentsRequested", "DocumentsVerified", "OfferIssued",
            "OfferAccepted", "Onboarding", "Rejected"
        };

        /// <summary>
        /// Caps untrusted candidate-submitted free text to a reasonable max length.
        /// Applied to answer text and code submissions before they reach the database,
        /// since these endpoints are called repeatedly (auto-save, run code) with no
        /// other size limit on the underlying nvarchar(max) columns.
        /// </summary>
        /// <summary>Delegates to the shared TextUtils.Clamp (trim=false — preserves this
        /// controller's previous behavior of NOT trimming exam-answer text/code, since
        /// leading/trailing whitespace can be meaningful in a candidate's submission).
        /// Kept as a local wrapper so the call sites below didn't need to change.</summary>
        private static string Clamp(string? value, int maxLength) => TextUtils.Clamp(value, maxLength);

        /// <summary>
        /// GET: /Exam/AssessmentLanding
        /// Reached after a successful email-link login (AssessmentAuth POST).
        /// Reads the exam id from TempData and renders a self-posting form so the
        /// browser completes the required POST to Start — bridging the GET redirect
        /// that cookie sign-in forces with the [HttpPost] Start action.
        /// </summary>
        [HttpGet]
        public IActionResult AssessmentLanding()
        {
            if (!TempData.ContainsKey("AssessmentLinkExamId"))
                return RedirectToAction("Index", "Home");

            var examId = (int)TempData["AssessmentLinkExamId"]!;
            var exam = _db.Exams.FirstOrDefault(e => e.Id == examId);
            if (exam == null) return NotFound();

            // Auto-submit a form via JavaScript; falls back to a manual button.
            ViewBag.ExamId = examId;
            ViewBag.ExamTitle = exam.Title;
            return View();
        }

        public IActionResult Details(int id)
        {
            var exam = _db.Exams.Include(e => e.Questions).ThenInclude(q => q.CodingQuestion).FirstOrDefault(e => e.Id == id);
            if (exam == null) return NotFound();
            ViewBag.PreviousAttempt = _db.ExamAttempts.Where(a => a.UserId == UserId && a.ExamId == id).OrderByDescending(a => a.StartedAt).FirstOrDefault();
            // Intentionally NOT loading EligibilityResult here — it's internal
            // recruitment-evaluation data (EligibilityScore/Status/Notes). Candidates
            // must not see it; Admin/Recruiter/HR retain full access via AdminController.
            return View(exam);
        }

        [HttpPost]
        public async Task<IActionResult> Start(int id)
        {
            var exam = await _db.Exams.Include(e => e.Questions).ThenInclude(q => q.AssessmentSection).FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();
            if (!IsAssessmentOpen(exam))
            {
                TempData["Error"] = "This eligibility assessment is not currently open.";
                return RedirectToAction("Details", new { id });
            }

            var candidate = await _db.Users.Include(u => u.CandidateProfile).FirstAsync(u => u.Id == UserId);

            // ── Block retake if candidate already has a completed attempt ─────────
            // Also block if their stage is past AssessmentCompleted (Shortlisted,
            // InterviewInProgress, Rejected, etc.) — a fresh admin invitation is
            // required to unlock a retake in all these cases.
            var hasCompletedAttempt = await _db.ExamAttempts
                .AnyAsync(a => a.UserId == UserId && a.ExamId == id && a.SubmittedAt != null);

            var stageIsLocked = LockedStages.Contains(candidate.RecruitmentStage);

            if (hasCompletedAttempt || stageIsLocked)
            {
                // Allow only if admin issued a brand-new "Pending" invitation AFTER the last attempt
                var lastAttemptTime = await _db.ExamAttempts
                    .Where(a => a.UserId == UserId && a.ExamId == id && a.SubmittedAt != null)
                    .OrderByDescending(a => a.SubmittedAt)
                    .Select(a => a.SubmittedAt)
                    .FirstOrDefaultAsync();

                var freshInvitation = await _db.AssessmentInvitations
                    .AnyAsync(i => i.UserId == UserId && i.ExamId == id &&
                              i.Status == "Pending" &&
                              i.ExpiresAt >= DateTime.UtcNow &&
                              (lastAttemptTime == null || i.SentAt > lastAttemptTime));

                if (!freshInvitation)
                {
                    TempData["Error"] = "You have already completed this assessment. A new invitation from the recruitment team is required to retake it.";
                    return RedirectToAction("Index", "Home");
                }
            }

            // Require a valid active invitation
            var activeInvitation = await _db.AssessmentInvitations
                .Where(i => i.UserId == UserId && i.ExamId == id &&
                            (i.Status == "Accepted" || i.Status == "Pending"))
                .OrderByDescending(i => i.SentAt)
                .FirstOrDefaultAsync();

            if (activeInvitation == null || activeInvitation.ExpiresAt < DateTime.UtcNow)
            {
                if (activeInvitation != null) activeInvitation.Status = "Expired";
                await _db.SaveChangesAsync();
                TempData["Error"] = "You need a valid assessment invitation to start this assessment. Please check your email from VISTAWAYS TECH.";
                return RedirectToAction("Index", "Home");
            }

            // AssessmentDate is stored as a true UTC instant (see RecruiterController.AssignAssessment).
            // EF/SQL Server round-trips datetime2 values as Kind=Unspecified, so SpecifyKind marks it
            // back as UTC explicitly rather than calling ToUniversalTime() — which would incorrectly
            // re-interpret it as being in *this server's* local timezone.
            if (activeInvitation.AssessmentDate.HasValue)
            {
                var opensAtUtc = DateTime.SpecifyKind(activeInvitation.AssessmentDate.Value, DateTimeKind.Utc);
                if (DateTime.UtcNow < opensAtUtc)
                {
                    TempData["Error"] = $"This assessment opens on {opensAtUtc:dd MMM yyyy HH:mm} UTC.";
                    return RedirectToAction("Index", "Home");
                }
            }

            ApplyInvitationSettings(exam, activeInvitation);

            var eligibility = _eligibility.Evaluate(candidate, exam);
            if (eligibility.Status != "Eligible")
            {
                // CLEANUP: removed the "Eligibility Result" email — this fires while the
                // candidate is actively trying to access the assessment, and the same
                // information is already shown on-screen via TempData below.
                await _db.SaveChangesAsync();
                TempData["Error"] = "You are not eligible to start this assessment yet. Check profile verification, resume, education, skills, and location requirements.";
                return RedirectToAction("Details", new { id });
            }

            var existing = await _db.ExamAttempts.FirstOrDefaultAsync(a => a.UserId == UserId && a.ExamId == id && a.SubmittedAt == null);
            if (existing != null && exam.AllowResumeAssessment) return RedirectToAction("Take", new { attemptId = existing.Id });

            var selectedQuestions = SelectAttemptQuestions(exam);
            var attempt = new ExamAttempt
            {
                UserId = UserId,
                ExamId = id,
                TotalMarks = selectedQuestions.Sum(q => q.Marks),
                StartedAt = DateTime.UtcNow,
                ProctoringSessionId = Guid.NewGuid().ToString("N"),
                LastHeartbeatAt = DateTime.UtcNow,
                EligibilityScore = eligibility.EligibilityScore,
                EligibilityStatus = eligibility.Status,
                QuestionOrder = string.Join(",", selectedQuestions.Select(q => q.Id))
            };
            _db.ExamAttempts.Add(attempt);

            // Advance recruitment stage
            candidate.RecruitmentStage = "AssessmentStarted";

            // CLEANUP: removed the "Assessment Started" email — candidate is actively inside
            // the assessment at this point, so a confirmation email adds nothing they don't
            // already know. The audit log below still records the start event.
            _audit.Record(User.Identity?.Name ?? "candidate", "Start Assessment", nameof(Exam), exam.Id, $"Attempt {attempt.Id}");
            await _db.SaveChangesAsync();
            return RedirectToAction("Take", new { attemptId = attempt.Id });
        }

        public async Task<IActionResult> Take(int attemptId)
        {
            var attempt = await _db.ExamAttempts.Include(a => a.Answers).FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == UserId);
            if (attempt == null || attempt.SubmittedAt != null) return RedirectToAction("Index", "Home");
            var exam = await _db.Exams
                .Include(e => e.Questions).ThenInclude(q => q.CodingQuestion)
                .Include(e => e.Questions).ThenInclude(q => q.AssessmentSection)
                .FirstOrDefaultAsync(e => e.Id == attempt.ExamId);
            if (exam == null) return NotFound();
            ApplyInvitationSettings(exam, await GetAttemptInvitation(attempt));
            exam.Questions = GetAttemptQuestions(exam, attempt);

            // Check time limit
            if (IsAttemptExpired(attempt.StartedAt, exam.DurationMinutes, DateTime.UtcNow))
            {
                await AutoSubmit(attempt, exam);
                return RedirectToAction("Result", new { attemptId });
            }

            var elapsed = DateTime.UtcNow - attempt.StartedAt;
            ViewBag.RemainingSeconds = (int)((exam.DurationMinutes * 60) - elapsed.TotalSeconds);
            if (string.IsNullOrWhiteSpace(attempt.ProctoringSessionId))
            {
                attempt.ProctoringSessionId = Guid.NewGuid().ToString("N");
                attempt.LastHeartbeatAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            ViewBag.ProctoringSessionId = attempt.ProctoringSessionId;
            return View(new ExamTakeViewModel
            {
                Exam = exam,
                AttemptId = attemptId,
                Answers = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.SelectedAnswer),
                TextAnswers = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.TextAnswer)
            });
        }

        [HttpPost]
        public async Task<IActionResult> Submit(int attemptId, IFormCollection form)
        {
            var attempt = await _db.ExamAttempts.Include(a => a.Answers).FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == UserId);
            if (attempt == null || attempt.SubmittedAt != null) return RedirectToAction("Index", "Home");
            var exam = await _db.Exams
                .Include(e => e.Questions).ThenInclude(q => q.CodingQuestion)
                .Include(e => e.Questions).ThenInclude(q => q.AssessmentSection)
                .FirstOrDefaultAsync(e => e.Id == attempt.ExamId);
            if (exam == null) return NotFound();
            var attemptInvitation = await GetAttemptInvitation(attempt);
            ApplyInvitationSettings(exam, attemptInvitation);
            exam.Questions = GetAttemptQuestions(exam, attempt);

            // Server-side time-limit enforcement — this was previously only checked in
            // Take (GET), which only runs when the candidate is actually looking at the
            // page (the client-side JS timer calls autoSubmit() at zero, which posts here
            // too, but that's client-side and bypassable). A POST straight to this action
            // after time is up — whether from a paused/blocked timer, a direct request
            // replaying a stale attemptId, or simply never reloading the page again — had
            // nothing stopping it from being accepted as an on-time submission, giving
            // effectively unlimited time. Route it through the same AutoSubmit path Take
            // already uses instead: that scores from whatever was already auto-saved
            // *before* the deadline, ignoring the posted form entirely, so answers typed
            // during unauthorized extra time can't be smuggled in this way either.
            if (IsAttemptExpired(attempt.StartedAt, exam.DurationMinutes, DateTime.UtcNow))
            {
                await AutoSubmit(attempt, exam);
                return RedirectToAction("Result", new { attemptId });
            }

            foreach (var q in exam.Questions)
            {
                var selected = Clamp(form[$"q_{q.Id}"].ToString(), 10);
                var textAnswer = Clamp(form[$"text_{q.Id}"].ToString(), 20_000);
                var code = Clamp(form[$"code_{q.Id}"].ToString(), 50_000);
                var language = Clamp(form[$"language_{q.Id}"].ToString(), 50);
                var effectiveAnswer = string.IsNullOrWhiteSpace(selected) ? textAnswer : selected;
                // Coding/Subjective/Short/Case questions can't be auto-graded here (no
                // code-execution judge or essay grader — see AssessmentScoringService.
                // RequiresManualGrading's doc comment) — they're scored 0 for now and
                // wait for an admin/recruiter to grade them via GradeAttempt afterward.
                // This used to call IsAnswerCorrect for these too, which treated any
                // non-blank submission as fully correct regardless of content.
                var needsManualGrading = AssessmentScoringService.RequiresManualGrading(q);
                var correct = !needsManualGrading && IsAnswerCorrect(q, effectiveAnswer);
                var scoreAwarded = needsManualGrading ? 0m : (correct ? q.Marks : -GetQuestionNegativeMarks(q, exam));

                var answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
                if (answer == null)
                {
                    answer = new UserAnswer { AttemptId = attemptId, QuestionId = q.Id };
                    _db.UserAnswers.Add(answer);
                }

                answer.SelectedAnswer = selected;
                answer.TextAnswer = q.QuestionType.Contains("Coding", StringComparison.OrdinalIgnoreCase) ? code : textAnswer;
                answer.IsCorrect = correct;
                answer.ScoreAwarded = scoreAwarded;
                answer.MarkedForReview = form[$"review_{q.Id}"] == "on";

                if (q.QuestionType.Contains("Coding", StringComparison.OrdinalIgnoreCase))
                {
                    _db.CodingSubmissions.Add(new CodingSubmission
                    {
                        AttemptId = attemptId,
                        QuestionId = q.Id,
                        Language = string.IsNullOrWhiteSpace(language) ? "C#" : language,
                        SourceCode = code,
                        ScoreAwarded = scoreAwarded
                    });
                }
            }

            attempt.SubmittedAt = DateTime.UtcNow;
            attempt.Status = "Submitted";
            _scoring.ScoreAttempt(attempt, exam);

            // Advance recruitment stage
            var candidateUser = await _db.Users.FirstAsync(u => u.Id == UserId);
            if (candidateUser.RecruitmentStage is "AssessmentStarted" or "InvitationSent")
                candidateUser.RecruitmentStage = "AssessmentCompleted";

            // Mark ALL active invitations for this exam as Used — link expires after
            // one attempt; admin must send a fresh invitation to allow a retake.
            var activeInvitations = await _db.AssessmentInvitations
                .Where(i => i.UserId == UserId && i.ExamId == exam.Id &&
                            (i.Status == "Accepted" || i.Status == "Pending"))
                .ToListAsync();
            activeInvitations.ForEach(i => i.Status = "Used");

            _notifications.Queue(candidateUser, exam, "Assessment Completion", "Assessment Submitted – VISTAWAYS TECH",
                $"Dear {candidateUser.FullName} (ID: {candidateUser.CandidateId}),\n\n" +
                $"Your assessment has been submitted successfully.\n\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"  SUBMISSION CONFIRMATION\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"📋 Assessment : {exam.Title}\n" +
                $"🕐 Submitted  : {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC\n\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"  WHAT HAPPENS NEXT\n" +
                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"Thank you for completing the assessment. Our recruitment team will review your submission " +
                $"and contact you regarding the next steps.\n\n" +
                $"Please keep an eye on your inbox and log in to the portal periodically for updates.\n\n" +
                $"Best regards,\n" +
                $"VISTAWAYS TECH Recruitment Team");
            // NOTE: Auto-shortlist notification removed — admin must manually shortlist and send the email.
            _audit.Record(User.Identity?.Name ?? "candidate", "Submit Assessment", nameof(ExamAttempt), attempt.Id, attempt.EligibilityStatus);
            await _db.SaveChangesAsync();
            return RedirectToAction("Result", new { attemptId });
        }

        [HttpPost]
        public async Task<IActionResult> AutoSave(int attemptId, int questionId, string? answer, string? textAnswer, bool markedForReview)
        {
            var attempt = await _db.ExamAttempts.Include(a => a.Answers).FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == UserId && a.SubmittedAt == null);
            if (attempt == null) return NotFound();

            // Same time-limit enforcement as Submit (see its own comment for the full
            // reasoning) — without this, a candidate who never reloads Take or calls
            // Submit could keep autosaving new/changed answers indefinitely past the
            // deadline, and Submit's own deadline check wouldn't help: it now scores from
            // whatever's already sitting in these very UserAnswer rows, which is exactly
            // what this endpoint would otherwise keep letting them rewrite.
            var exam = await _db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == attempt.ExamId);
            if (exam == null) return NotFound();
            ApplyInvitationSettings(exam, await GetAttemptInvitation(attempt));
            if (IsAttemptExpired(attempt.StartedAt, exam.DurationMinutes, DateTime.UtcNow))
                return StatusCode(409, new { saved = false, reason = "Time expired." });

            var saved = attempt.Answers.FirstOrDefault(a => a.QuestionId == questionId);
            if (saved == null)
            {
                saved = new UserAnswer { AttemptId = attemptId, QuestionId = questionId };
                _db.UserAnswers.Add(saved);
            }

            saved.SelectedAnswer = Clamp(answer, 10);
            saved.TextAnswer = Clamp(textAnswer, 20_000);
            saved.MarkedForReview = markedForReview;
            attempt.LastAutoSavedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { saved = true, savedAt = attempt.LastAutoSavedAt });
        }

        [HttpPost]
        public async Task<IActionResult> LogViolation(int attemptId, string eventType, string details, int severity = 1)
        {
            var attempt = await _db.ExamAttempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == UserId && a.SubmittedAt == null);
            if (attempt == null) return NotFound();
            eventType = Clamp(eventType, 100);
            details = Clamp(details, 2000);
            _db.ProctoringLogs.Add(new ProctoringLog { AttemptId = attemptId, EventType = eventType, Details = details, Severity = severity });
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> ProctorHeartbeat(int attemptId, string sessionId, string browserInfo, bool online, bool fullScreen)
        {
            var attempt = await _db.ExamAttempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == UserId && a.SubmittedAt == null);
            if (attempt == null) return NotFound();

            sessionId = Clamp(sessionId, 80);
            browserInfo = Clamp(browserInfo, 500);
            var now = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(attempt.ProctoringSessionId))
            {
                attempt.ProctoringSessionId = sessionId;
            }
            else if (!string.IsNullOrWhiteSpace(sessionId) && attempt.ProctoringSessionId != sessionId)
            {
                _db.ProctoringLogs.Add(new ProctoringLog
                {
                    AttemptId = attemptId,
                    EventType = "Multiple Login",
                    Details = $"Another active assessment session was detected. Existing={attempt.ProctoringSessionId}; Incoming={sessionId}; Browser={browserInfo}",
                    Severity = 5,
                    OccurredAt = now
                });
                attempt.ProctoringSessionId = sessionId;
            }

            if (!online)
            {
                _db.ProctoringLogs.Add(new ProctoringLog
                {
                    AttemptId = attemptId,
                    EventType = "Offline Heartbeat",
                    Details = "Heartbeat reported offline network state.",
                    Severity = 3,
                    OccurredAt = now
                });
            }

            if (!fullScreen)
            {
                _db.ProctoringLogs.Add(new ProctoringLog
                {
                    AttemptId = attemptId,
                    EventType = "Fullscreen Not Active",
                    Details = $"Heartbeat reported non-fullscreen mode. Browser={browserInfo}",
                    Severity = 2,
                    OccurredAt = now
                });
            }

            attempt.LastHeartbeatAt = now;
            await _db.SaveChangesAsync();
            return Ok(new { ok = true, heartbeatAt = now });
        }

        [HttpPost]
        public async Task<IActionResult> RunCode(int attemptId, int questionId, string language, string sourceCode)
        {
            var attempt = await _db.ExamAttempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == UserId && a.SubmittedAt == null);
            if (attempt == null) return NotFound();

            language = Clamp(language, 50);
            sourceCode = Clamp(sourceCode, 50_000);

            var output = string.IsNullOrWhiteSpace(sourceCode)
                ? "No source code provided."
                : $"Queued {language} code for prototype execution. Sample tests simulated successfully.";
            _db.CodingSubmissions.Add(new CodingSubmission
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                Language = language,
                SourceCode = sourceCode,
            });
            await _db.SaveChangesAsync();
            return Ok(new { output });
        }

        private async Task AutoSubmit(ExamAttempt attempt, Exam exam)
        {
            ApplyInvitationSettings(exam, await GetAttemptInvitation(attempt));
            exam.Questions = GetAttemptQuestions(exam, attempt);
            // Score the attempt with whatever answers were auto-saved so far.
            _scoring.ScoreAttempt(attempt, exam);
            attempt.Status      = "AutoSubmitted";
            attempt.SubmittedAt = DateTime.UtcNow;

            // Advance the candidate's pipeline stage so they are not left stuck
            // in "AssessmentStarted" indefinitely after a timeout.
            var candidateUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == attempt.UserId);
            if (candidateUser != null &&
                (candidateUser.RecruitmentStage == "AssessmentStarted" ||
                 candidateUser.RecruitmentStage == "InvitationSent"))
            {
                candidateUser.RecruitmentStage = "AssessmentCompleted";
            }

            await _db.SaveChangesAsync();
        }

        public IActionResult Result(int attemptId)
        {
            var attempt = _db.ExamAttempts
                .Include(a => a.Exam)
                .FirstOrDefault(a => a.Id == attemptId && a.UserId == UserId);
            if (attempt == null) return NotFound();

            // Results are NOT shown to the candidate immediately.
            // Return minimal view-model with no score/answer details.
            return View(new ExamResultViewModel
            {
                Attempt = attempt,
                Exam    = attempt.Exam!,
                Results = new List<QuestionResultItem>()
            });
        }

        /// <summary>Single source of truth for the assessment time limit, used by Take
        /// (GET), Submit, and AutoSave — previously each had its own inline copy of this
        /// comparison; AutoSave didn't have one at all, which was a real gap (see Submit's
        /// own comment on why that mattered). now &gt; startedAt + durationMinutes.</summary>
        private static bool IsAttemptExpired(DateTime startedAt, int durationMinutes, DateTime now) =>
            (now - startedAt).TotalMinutes > durationMinutes;

        private static bool IsAssessmentOpen(Exam assessment)
        {
            var now = DateTime.UtcNow;
            var statusOpen = assessment.IsActive && (assessment.Status == "Active" || assessment.Status == "Scheduled");
            var startsOk = assessment.StartDateTime == null || assessment.StartDateTime <= now;
            var endsOk = assessment.EndDateTime == null || assessment.EndDateTime >= now;
            return statusOpen && startsOk && endsOk;
        }

        private static List<Question> SelectAttemptQuestions(Exam exam)
        {
            var questions = exam.Questions
                .Where(q => q.IsQuestionBankItem || !exam.UseQuestionBank)
                .OrderBy(q => q.AssessmentSection?.DisplayOrder ?? 0)
                .ThenBy(q => q.DisplayOrder)
                .ThenBy(q => q.Id)
                .ToList();

            if (exam.RandomizeQuestions)
                questions = questions.OrderBy(_ => Guid.NewGuid()).ToList();

            if (exam.RandomQuestionCount > 0)
                questions = questions.Take(Math.Min(exam.RandomQuestionCount, questions.Count)).ToList();

            return questions;
        }

        private static List<Question> GetAttemptQuestions(Exam exam, ExamAttempt attempt)
        {
            var questionsById = exam.Questions.ToDictionary(q => q.Id);
            var orderedIds = (attempt.QuestionOrder ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => int.TryParse(id, out var parsed) ? parsed : 0)
                .Where(id => id > 0)
                .ToList();

            if (!orderedIds.Any()) return SelectAttemptQuestions(exam);
            return orderedIds.Where(questionsById.ContainsKey).Select(id => questionsById[id]).ToList();
        }

        /// <summary>Auto-grades objective question types only — Coding/Subjective/Short/
        /// Case answers never reach here (see AssessmentScoringService.
        /// RequiresManualGrading and its one caller in Submit above), since there's no
        /// automated way to check those for actual correctness.</summary>
        private static bool IsAnswerCorrect(Question question, string answer)
        {
            var expected = (question.CorrectAnswer ?? "").Trim();
            var actual = (answer ?? "").Trim();

            if (question.QuestionType.Contains("Blank", StringComparison.OrdinalIgnoreCase))
                return expected.Equals(actual, StringComparison.OrdinalIgnoreCase);

            if (question.QuestionType.Contains("True", StringComparison.OrdinalIgnoreCase))
                return expected.Equals(actual, StringComparison.OrdinalIgnoreCase);

            if (question.QuestionType.Contains("Multiple", StringComparison.OrdinalIgnoreCase))
            {
                var expectedSet = expected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).OrderBy(v => v);
                var actualSet = actual.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).OrderBy(v => v);
                return expectedSet.SequenceEqual(actualSet, StringComparer.OrdinalIgnoreCase);
            }

            return expected.Equals(actual, StringComparison.OrdinalIgnoreCase);
        }

        private static decimal GetQuestionNegativeMarks(Question question, Exam exam)
        {
            return question.NegativeMarks > 0 ? question.NegativeMarks : exam.NegativeMarks;
        }

        private async Task<AssessmentInvitation?> GetAttemptInvitation(ExamAttempt attempt)
        {
            return await _db.AssessmentInvitations
                .Where(i => i.UserId == attempt.UserId && i.ExamId == attempt.ExamId &&
                            (i.Status == "Accepted" || i.Status == "Pending"))
                .OrderByDescending(i => i.SentAt)
                .FirstOrDefaultAsync();
        }

        private static void ApplyInvitationSettings(Exam exam, AssessmentInvitation? invitation)
        {
            if (invitation == null) return;
            if (invitation.DurationMinutes > 0) exam.DurationMinutes = invitation.DurationMinutes;
            if (invitation.PassingMarks > 0)
            {
                exam.PassingMarks = invitation.PassingMarks;
                exam.CutoffScore = invitation.PassingMarks;
            }
        }
    }
}
