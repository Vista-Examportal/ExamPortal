using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;

namespace ExamPortal.Controllers
{
// AdminController — assessment authoring: create/toggle exams and view results.
// Split out of the single AdminController.cs for readability; same partial class.
    public partial class AdminController
    {

        // ── Existing features ─────────────────────────────────────────────────

        // Assessment management is Admin + Recruiter per the Permissions Matrix shown
        // on Admin/Index (Roles tab) — HR's remit is interviews/documents/offers/onboarding,
        // not authoring or toggling assessments.
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult CreateExam() =>
            View(new CreateExamViewModel { Questions = Enumerable.Range(0, 5).Select(_ => new QuestionInput()).ToList() });


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult CreateExam(CreateExamViewModel model)
        {
            var validQuestions = model.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Text)).ToList();
            if (validQuestions.Count == 0)
            {
                ModelState.AddModelError("", "Please add at least one question.");
                return View(model);
            }
            if (validQuestions.Any(q => q.Marks <= 0))
            {
                // QuestionInput.Marks has no [Range] attribute (unlike PassingMarks, which
                // does), so this was reachable via a direct POST bypassing the picker's min="1".
                // Left unchecked, a 0/negative-mark question drags TotalMarks down — possibly
                // below PassingMarks (checked below), making the whole assessment silently
                // unwinnable with no error telling anyone why.
                ModelState.AddModelError("", "Every question must be worth at least 1 mark.");
                return View(model);
            }
            if (model.NegativeMarks < 0 || validQuestions.Any(q => q.NegativeMarks < 0))
            {
                // Scoring uses -GetQuestionNegativeMarks(q, exam) for a wrong answer (see
                // ExamController.GetQuestionNegativeMarks) — a *negative* NegativeMarks value
                // double-negates into a positive score, meaning a wrong answer would reward the
                // candidate instead of penalizing them. This isn't a cosmetic/unreachable-state
                // bug like the others above — it would actively corrupt real scores.
                ModelState.AddModelError("", "Negative marking can't itself be a negative number.");
                return View(model);
            }

            // Defensive length clamping — mirrors the [StringLength] limits on
            // CreateExamViewModel/QuestionInput in case binding alone doesn't enforce them.
            model.Title             = Clamp(model.Title, 200);
            model.Description       = Clamp(model.Description, 2000);
            model.Instructions      = Clamp(model.Instructions, 4000);
            model.Subject           = Clamp(model.Subject, 100);
            model.TemplateName      = Clamp(model.TemplateName, 150);
            model.Status            = Clamp(model.Status, 50);
            model.RequiredEducation = Clamp(model.RequiredEducation, 300);
            model.RequiredSkills    = Clamp(model.RequiredSkills, 500);
            model.EligibleLocations = Clamp(model.EligibleLocations, 500);
            model.ExperienceLevel   = Clamp(model.ExperienceLevel, 100);
            model.TimeZone          = Clamp(model.TimeZone, 100);

            // Convert the plain Start/End picker values (no offset of their own) into true UTC
            // instants using the selected time zone — see ConvertWallClockToUtc. Without this,
            // IsAssessmentOpen() and the "upcoming exams" dashboard filters (both of which
            // compare against DateTime.UtcNow) would silently misjudge the window whenever the
            // exam wasn't scheduled in server-local/UTC time.
            var startUtc = model.StartDateTime.HasValue ? SchedulingHelper.ConvertWallClockToUtc(model.StartDateTime.Value, model.TimeZone) : (DateTime?)null;
            var endUtc   = model.EndDateTime.HasValue   ? SchedulingHelper.ConvertWallClockToUtc(model.EndDateTime.Value, model.TimeZone)   : (DateTime?)null;
            if (startUtc.HasValue && endUtc.HasValue && endUtc.Value <= startUtc.Value)
            {
                // Without this check, an inverted window doesn't error anywhere obvious — it
                // just makes IsAssessmentOpen() permanently return false (End is never >= now
                // once End < Start), so the assessment silently becomes unreachable with no
                // message explaining why. Catching it here is far cheaper than someone
                // reverse-engineering that later from a "not currently open" report.
                ModelState.AddModelError("", "End date/time must be after the start date/time.");
                return View(model);
            }
            foreach (var q in validQuestions)
            {
                q.Text               = Clamp(q.Text, 2000);
                q.Category           = Clamp(q.Category, 100);
                q.SectionName        = Clamp(q.SectionName, 100);
                q.QuestionType       = Clamp(q.QuestionType, 100);
                q.Difficulty         = Clamp(q.Difficulty, 50);
                q.Tags               = Clamp(q.Tags, 500);
                q.OptionA            = Clamp(q.OptionA, 1000);
                q.OptionB            = Clamp(q.OptionB, 1000);
                q.OptionC            = Clamp(q.OptionC, 1000);
                q.OptionD            = Clamp(q.OptionD, 1000);
                q.StarterCode        = Clamp(q.StarterCode, 4000);
                q.SupportedLanguages = Clamp(q.SupportedLanguages, 200);
                q.SampleTestCases    = Clamp(q.SampleTestCases, 4000);
                q.HiddenTestCases    = Clamp(q.HiddenTestCases, 4000);
            }

            var totalMarks = validQuestions.Sum(q => q.Marks);
            if (totalMarks < model.PassingMarks)
            {
                // Same reasoning as the Marks<=0 check above: without this, an admin could
                // set PassingMarks=35 with questions that only add up to, say, 20 total —
                // no candidate could ever pass, and nothing here would have told the admin.
                ModelState.AddModelError("", $"Passing marks ({model.PassingMarks}) can't be higher than the total possible marks ({totalMarks}). Add more questions, raise their marks, or lower the passing marks.");
                return View(model);
            }

            var exam = new Exam
            {
                Title = model.Title, Description = model.Description, Instructions = model.Instructions,
                Subject = model.Subject, TemplateName = model.TemplateName, IsTemplate = model.IsTemplate,
                RandomQuestionCount = model.RandomQuestionCount, DurationMinutes = model.DurationMinutes,
                PassingMarks = model.PassingMarks, CutoffScore = model.PassingMarks,
                StartDateTime = startUtc, EndDateTime = endUtc, Status = model.Status,
                RequiredEducation = model.RequiredEducation, RequiredSkills = model.RequiredSkills,
                EligibleLocations = model.EligibleLocations, ExperienceLevel = model.ExperienceLevel,
                NegativeMarks = model.NegativeMarks, UseQuestionBank = model.UseQuestionBank,
                RandomizeQuestions = model.RandomizeQuestions,
                RandomizeOptions = model.RandomizeOptions, AllowResumeAssessment = model.AllowResumeAssessment,
                RequireFullScreen = model.RequireFullScreen, RestrictCopyPaste = model.RestrictCopyPaste,
                RequireWebcam = model.RequireWebcam, RequireScreenMonitoring = model.RequireScreenMonitoring,
                TotalMarks = totalMarks,
                TotalQuestions = model.RandomQuestionCount > 0 ? Math.Min(model.RandomQuestionCount, validQuestions.Count) : validQuestions.Count,
                IsActive = model.Status is "Active" or "Scheduled"
            };

            var sectionMap = validQuestions
                .Select(q => string.IsNullOrWhiteSpace(q.SectionName) ? q.Category : q.SectionName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((name, index) => new AssessmentSection
                {
                    Name = name,
                    Description = $"{name} section",
                    DisplayOrder = index + 1,
                    WeightPercent = 100
                })
                .ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

            exam.Sections = sectionMap.Values.ToList();
            exam.Questions = validQuestions.Select((q, index) =>
            {
                var sectionName = string.IsNullOrWhiteSpace(q.SectionName) ? q.Category : q.SectionName;
                return new Question
                {
                    Text = q.Text, Category = q.Category, QuestionType = q.QuestionType,
                    Difficulty = q.Difficulty, Tags = q.Tags, IsQuestionBankItem = q.IsQuestionBankItem,
                    DisplayOrder = index + 1, AssessmentSection = sectionMap[sectionName],
                    OptionA = q.OptionA, OptionB = q.OptionB, OptionC = q.OptionC, OptionD = q.OptionD,
                    CorrectAnswer = q.CorrectAnswer, Marks = q.Marks, NegativeMarks = q.NegativeMarks,
                    CodingQuestion = q.QuestionType.Contains("Coding", StringComparison.OrdinalIgnoreCase)
                        ? new CodingQuestion
                        {
                            StarterCode = q.StarterCode, SupportedLanguages = q.SupportedLanguages,
                            SampleTestCases = q.SampleTestCases, HiddenTestCases = q.HiddenTestCases
                        } : null
                };
            }).ToList();
            _db.Exams.Add(exam);
            _db.SaveChanges();
            TempData["Success"] = "Assessment created successfully!";

            // Same reasoning as EditSchedule/ToggleExam below: Index() is Admin-only, so a
            // Recruiter (CreateExam is also [Authorize(Roles = Admin + "," + Recruiter)],
            // and Recruiter/Index.cshtml links here) who successfully creates an assessment
            // must not be bounced to a page they're not authorized to view.
            if (!User.IsInRole(PortalRoles.Admin) && User.IsInRole(PortalRoles.Recruiter))
                return RedirectToAction("Index", "Recruiter", new { tab = "assessments" });
            return RedirectToAction("Index");
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult ToggleExam(int id)
        {
            var exam = _db.Exams.Find(id);
            if (exam != null) { exam.IsActive = !exam.IsActive; _db.SaveChanges(); }

            // Same reasoning as EditSchedule above: Index() is Admin-only, so a Recruiter
            // toggling from their own Assessments tab must not be bounced to a page they
            // can't view.
            if (!User.IsInRole(PortalRoles.Admin) && User.IsInRole(PortalRoles.Recruiter))
                return RedirectToAction("Index", "Recruiter", new { tab = "assessments" });
            return RedirectToAction("Index");
        }


        // Reschedule/extend an existing assessment's Start/End window without recreating it —
        // recreating would orphan any attempts/results already tied to the original exam id.
        // Shows times in Asia/Kolkata by default since Exam doesn't persist which zone the
        // stored UTC values were originally entered in — see ConvertUtcToWallClock.
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult EditSchedule(int id)
        {
            var exam = _db.Exams.Find(id);
            if (exam == null) return NotFound();

            var model = new EditExamScheduleViewModel
            {
                Id = exam.Id,
                Title = exam.Title,
                Status = exam.Status,
                StartDateTime = exam.StartDateTime.HasValue
                    ? SchedulingHelper.ConvertUtcToWallClock(exam.StartDateTime.Value, "Asia/Kolkata")
                    : null,
                EndDateTime = exam.EndDateTime.HasValue
                    ? SchedulingHelper.ConvertUtcToWallClock(exam.EndDateTime.Value, "Asia/Kolkata")
                    : null,
            };
            return View(model);
        }


        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult EditSchedule(EditExamScheduleViewModel model)
        {
            var exam = _db.Exams.Find(model.Id);
            if (exam == null) return NotFound();

            if (!ModelState.IsValid)
            {
                model.Title = exam.Title; // never trust the hidden field alone over the real record
                return View(model);
            }

            model.Status = Clamp(model.Status, 50);
            model.TimeZone = Clamp(model.TimeZone, 100);

            // Same UTC conversion CreateExam uses — without it, IsAssessmentOpen() and the
            // dashboard's "upcoming" filters would silently misjudge the window whenever the
            // exam wasn't (re)scheduled in server-local/UTC time.
            var startUtc = model.StartDateTime.HasValue
                ? SchedulingHelper.ConvertWallClockToUtc(model.StartDateTime.Value, model.TimeZone)
                : (DateTime?)null;
            var endUtc = model.EndDateTime.HasValue
                ? SchedulingHelper.ConvertWallClockToUtc(model.EndDateTime.Value, model.TimeZone)
                : (DateTime?)null;
            if (!SchedulingHelper.IsValidWindow(startUtc, endUtc))
            {
                // Same reasoning as CreateExam's identical check: an inverted window doesn't
                // error anywhere else, it just silently makes the assessment permanently
                // unreachable via IsAssessmentOpen().
                model.Title = exam.Title;
                ModelState.AddModelError("", "End date/time must be after the start date/time.");
                return View(model);
            }

            exam.StartDateTime = startUtc;
            exam.EndDateTime = endUtc;
            exam.Status = model.Status;
            exam.IsActive = model.Status is "Active" or "Scheduled";
            _db.SaveChanges();

            TempData["Success"] = $"Schedule updated for \"{exam.Title}\".";
            // AdminController.Index() is [Authorize(Roles = PortalRoles.Admin)] only — a
            // Recruiter (this action is also [Authorize(Roles = Admin + "," + Recruiter)])
            // who successfully saves here must not be sent to a page they're not authorized
            // to view. Recruiters go back to their own Assessments tab instead.
            if (!User.IsInRole(PortalRoles.Admin) && User.IsInRole(PortalRoles.Recruiter))
                return RedirectToAction("Index", "Recruiter", new { tab = "assessments" });
            return RedirectToAction("Index");
        }


        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult ExamResults(int id)
        {
            var exam = _db.Exams.AsNoTracking().Include(e => e.Questions).FirstOrDefault(e => e.Id == id);
            if (exam == null) return NotFound();

            var attempts = _db.ExamAttempts.AsNoTracking().Include(a => a.User).Include(a => a.Scores)
                .Include(a => a.ProctoringLogs)
                .Where(a => a.ExamId == id && a.SubmittedAt != null)
                .OrderByDescending(a => a.SubmittedAt).ToList();

            var completed = attempts.Count;

            // FIX: upsert the report so stats stay current on every visit,
            // rather than freezing at the values from the first ever view.
            var report = _db.AssessmentReports.FirstOrDefault(r => r.ExamId == id);
            if (report == null)
            {
                report = new AssessmentReport { ExamId = id };
                _db.AssessmentReports.Add(report);
            }
            report.InvitedCandidates  = _db.AssessmentInvitations.Count(i => i.ExamId == id);
            report.CompletedAttempts  = completed;
            report.AverageScore       = completed > 0 ? (decimal)attempts.Average(a => a.Score) : 0;
            report.EligibleCandidates = attempts.Count(a => a.Passed);
            report.GeneratedAt        = DateTime.UtcNow;
            _db.SaveChanges();

            ViewBag.Exam = exam;
            return View(attempts);
        }

        // ── Manual grading for Coding/Subjective/Short/Case-study questions ─────
        // See AssessmentScoringService.RequiresManualGrading — these question types have
        // no automated way to check correctness, so instead of (previously) silently
        // awarding full marks for any non-blank answer, they sit at 0 and the attempt's
        // Status is "PendingManualReview" until an admin/recruiter grades them here.

        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult GradeAttempt(int id)
        {
            var attempt = _db.ExamAttempts
                .Include(a => a.User)
                .Include(a => a.Exam)
                .Include(a => a.Answers).ThenInclude(a => a.Question)
                .FirstOrDefault(a => a.Id == id);
            if (attempt == null) return NotFound();

            ViewBag.PendingAnswers = attempt.Answers
                .Where(a => a.Question != null && AssessmentScoringService.RequiresManualGrading(a.Question))
                .OrderBy(a => a.Question!.DisplayOrder)
                .ToList();
            return View(attempt);
        }

        [HttpPost]
        [Authorize(Roles = PortalRoles.Admin + "," + PortalRoles.Recruiter)]
        public IActionResult GradeAttempt(int id, Dictionary<int, decimal> scores)
        {
            var attempt = _db.ExamAttempts
                .Include(a => a.Answers).ThenInclude(a => a.Question)
                .FirstOrDefault(a => a.Id == id);
            if (attempt == null) return NotFound();

            var exam = _db.Exams.AsNoTracking()
                .Include(e => e.Questions).ThenInclude(q => q.AssessmentSection)
                .FirstOrDefault(e => e.Id == attempt.ExamId);
            if (exam == null) return NotFound();

            _scoring.FinalizeManualGrading(attempt, exam, scores ?? new Dictionary<int, decimal>());
            _db.SaveChanges();

            TempData["Success"] = "Grading finalized.";
            return RedirectToAction("ExamResults", new { id = attempt.ExamId });
        }

    }
}
