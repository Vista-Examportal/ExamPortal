using ExamPortal.Data;
using ExamPortal.Models;

namespace ExamPortal.Services
{
    public class AssessmentScoringService
    {
        private readonly AppDbContext _db;
        public AssessmentScoringService(AppDbContext db) => _db = db;

        public void ScoreAttempt(ExamAttempt attempt, Exam assessment)
        {
            _db.Scores.RemoveRange(_db.Scores.Where(s => s.AttemptId == attempt.Id));

            var questionsById = assessment.Questions.ToDictionary(q => q.Id);
            foreach (var answer in attempt.Answers)
            {
                if (answer.Question == null && questionsById.TryGetValue(answer.QuestionId, out var question))
                    answer.Question = question;
            }

            var grouped = attempt.Answers.GroupBy(a => a.Question?.AssessmentSection?.Name ?? a.Question?.Category ?? "General");
            foreach (var section in grouped)
            {
                var maxScore = section.Sum(a => a.Question?.Marks ?? 0);
                var awarded = section.Sum(a => a.ScoreAwarded);
                _db.Scores.Add(new Score
                {
                    AttemptId = attempt.Id,
                    SectionName = section.Key,
                    MaxScore = maxScore,
                    AwardedScore = awarded,
                    WeightedScore = awarded
                });
            }

            var selectedQuestionIds = (attempt.QuestionOrder ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => int.TryParse(v, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToHashSet();
            var totalMarks = selectedQuestionIds.Any()
                ? assessment.Questions.Where(q => selectedQuestionIds.Contains(q.Id)).Sum(q => q.Marks)
                : assessment.TotalMarks;

            attempt.Score = (int)Math.Round(attempt.Answers.Sum(a => a.ScoreAwarded));
            attempt.TotalMarks = totalMarks;
            attempt.Passed = attempt.Score >= Math.Max(assessment.PassingMarks, assessment.CutoffScore);
            attempt.PercentageScore = attempt.TotalMarks > 0 ? Math.Round(attempt.Score * 100m / attempt.TotalMarks, 2) : 0;
            attempt.EligibilityScore = (int)Math.Round(attempt.PercentageScore);
            attempt.EligibilityStatus = attempt.Passed ? "Eligible" : "Not Eligible";

            // Coding/Subjective questions (see RequiresManualGrading) can't be auto-graded —
            // there's no code-execution judge or essay grader here (RunCode in ExamController
            // is an explicit, self-labeled execution prototype). Their ScoreAwarded is 0 at
            // this point, so whenever any such question was answered, Score/Passed/
            // EligibilityStatus above are only provisional (computed from just the
            // auto-graded portion). PendingManualReview says so plainly — see
            // AdminController.GradeAttempt/FinalizeManualGrading below, which is what moves
            // an attempt from here to a final "Evaluated". This replaces what used to
            // silently award full marks for literally any non-blank coding/subjective
            // answer, regardless of content — a real scoring-integrity gap, not just an
            // incomplete feature.
            var hasPendingManualGrading = attempt.Answers.Any(a => a.Question != null && RequiresManualGrading(a.Question));
            attempt.Status = hasPendingManualGrading ? "PendingManualReview" : "Evaluated";

            UpdateRanks(assessment.Id);
        }

        /// <summary>True for question types with no automated way to check correctness
        /// here — Coding (RunCode is an explicit, self-labeled execution prototype, not a
        /// real judge) and Subjective/Short-answer/Case-study free text. These must be
        /// scored by a human via GradeAttempt/FinalizeManualGrading rather than
        /// auto-graded as "correct" just for being non-blank.</summary>
        public static bool RequiresManualGrading(Question question) =>
            question.QuestionType.Contains("Coding", StringComparison.OrdinalIgnoreCase)
            || question.QuestionType.Contains("Subjective", StringComparison.OrdinalIgnoreCase)
            || question.QuestionType.Contains("Short", StringComparison.OrdinalIgnoreCase)
            || question.QuestionType.Contains("Case", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Commits admin/recruiter-entered scores (0..Marks, clamped) for every manually-
        /// graded answer on this attempt, mirrors them onto the matching CodingSubmission
        /// row where relevant, then reuses ScoreAttempt to recompute sections/totals/rank
        /// against the now-filled-in scores. Finally overrides ScoreAttempt's own
        /// PendingManualReview detection — which would otherwise see the same
        /// Coding/Subjective questions still present and flag the attempt as pending all
        /// over again — with "Evaluated", since finishing this method IS what "no longer
        /// pending" means here: the admin just supplied a score for everything they were
        /// shown. Safe to call more than once (e.g. to correct a score) — always
        /// recomputes from scratch rather than accumulating.
        /// </summary>
        public void FinalizeManualGrading(ExamAttempt attempt, Exam assessment, Dictionary<int, decimal> scoresByAnswerId)
        {
            var questionsById = assessment.Questions.ToDictionary(q => q.Id);
            foreach (var answer in attempt.Answers)
            {
                if (answer.Question == null && questionsById.TryGetValue(answer.QuestionId, out var question))
                    answer.Question = question;

                if (answer.Question == null || !RequiresManualGrading(answer.Question)) continue;
                if (!scoresByAnswerId.TryGetValue(answer.Id, out var score)) continue;

                // 0 (fully wrong/blank) up to full marks — there's no "selected a specific
                // wrong option" here the way MCQ negative marking has, so no penalty floor
                // below 0 either.
                answer.ScoreAwarded = Math.Clamp(score, 0m, answer.Question.Marks);
                answer.IsCorrect = answer.ScoreAwarded >= answer.Question.Marks;

                if (answer.Question.QuestionType.Contains("Coding", StringComparison.OrdinalIgnoreCase))
                {
                    var submission = _db.CodingSubmissions
                        .Where(c => c.AttemptId == attempt.Id && c.QuestionId == answer.QuestionId)
                        .OrderByDescending(c => c.Id)
                        .FirstOrDefault();
                    if (submission != null) submission.ScoreAwarded = answer.ScoreAwarded;
                }
            }

            ScoreAttempt(attempt, assessment);
            attempt.Status = "Evaluated";
        }

        private void UpdateRanks(int assessmentId)
        {
            var attempts = _db.ExamAttempts
                .Where(a => a.ExamId == assessmentId && a.SubmittedAt != null)
                .OrderByDescending(a => a.PercentageScore)
                .ThenBy(a => a.SubmittedAt)
                .ToList();

            var rank = 1;
            foreach (var completedAttempt in attempts)
            {
                completedAttempt.Rank = rank++;
            }
        }
    }
}
