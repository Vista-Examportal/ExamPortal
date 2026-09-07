using System;
using System.Linq;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExamPortal.Tests.Services
{
    // Design note on these tests: ScoreAttempt() does not call _db.SaveChanges() itself
    // (that's left to the caller/controller), and its ranking step (UpdateRanks) queries
    // _db.ExamAttempts mid-call. To avoid depending on exactly when EF Core's InMemory
    // provider reconciles an in-flight, not-yet-saved mutation on a tracked entity with a
    // query running inside the very same method call — a genuinely provider-specific
    // timing detail this sandbox has no way to actually run and confirm — every test here
    // is written so its assertions don't depend on that timing at all:
    //   - Score/TotalMarks/Passed/PercentageScore/EligibilityScore/Status assertions read
    //     properties mutated directly on the in-memory `attempt` object — no DB involved.
    //   - Section-score assertions use db.Scores.Local, which always reflects the current
    //     change tracker state synchronously, regardless of provider or SaveChanges timing.
    //   - The "old scores removed" test only pre-seeds data via a SaveChanges() that
    //     happens *before* ScoreAttempt runs, so the removal query inside ScoreAttempt is
    //     unambiguously querying already-committed data.
    //   - The ranking test's "other" attempts (B, C, D) are added and saved *before*
    //     ScoreAttempt runs on a separate attempt that itself is *not* added to the
    //     context — so the ranking query only ever sees fully-committed, stable data.
    public class AssessmentScoringServiceTests
    {
        private static AppDbContext BuildContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static Question MakeQuestion(int id, int marks, string category = "Technical MCQ", AssessmentSection? section = null)
        {
            var q = new Question { Id = id, Marks = marks, Category = category, Text = $"Question {id}" };
            if (section != null)
            {
                q.AssessmentSection = section;
                q.AssessmentSectionId = section.Id;
            }
            return q;
        }

        [Fact]
        public void ScoreAttempt_SumsAwardedScoreAcrossAllAnswers()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 10);
            var q2 = MakeQuestion(2, marks: 20);
            var exam = new Exam { Id = 1, TotalMarks = 30, PassingMarks = 15, CutoffScore = 0 };
            exam.Questions.Add(q1);
            exam.Questions.Add(q2);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 10 });
            attempt.Answers.Add(new UserAnswer { QuestionId = 2, Question = q2, ScoreAwarded = 15 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal(25, attempt.Score);
        }

        [Fact]
        public void ScoreAttempt_RoundsFractionalAwardedScoreToNearestWholeNumber()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 10);
            var exam = new Exam { Id = 1, TotalMarks = 10, PassingMarks = 5 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 7.6m });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal(8, attempt.Score); // MidpointRounding default (ToEven) doesn't apply at .6
        }

        [Fact]
        public void ScoreAttempt_ScoreAtOrAbovePassingMarks_MarksAttemptAsPassed()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 100);
            var exam = new Exam { Id = 1, TotalMarks = 100, PassingMarks = 50, CutoffScore = 0 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 50 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.True(attempt.Passed);
            Assert.Equal("Eligible", attempt.EligibilityStatus);
        }

        [Fact]
        public void ScoreAttempt_ScoreBelowPassingMarks_MarksAttemptAsNotPassed()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 100);
            var exam = new Exam { Id = 1, TotalMarks = 100, PassingMarks = 50, CutoffScore = 0 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 49 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.False(attempt.Passed);
            Assert.Equal("Not Eligible", attempt.EligibilityStatus);
        }

        [Fact]
        public void ScoreAttempt_UsesHigherOfPassingMarksAndCutoffScore()
        {
            // Passing bar is Math.Max(PassingMarks, CutoffScore) — a higher CutoffScore
            // (e.g. set separately for a stricter role) should win even if PassingMarks
            // is lower.
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 100);
            var exam = new Exam { Id = 1, TotalMarks = 100, PassingMarks = 40, CutoffScore = 60 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 50 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            // 50 clears PassingMarks (40) but not CutoffScore (60) — should still fail.
            Assert.False(attempt.Passed);
        }

        [Fact]
        public void ScoreAttempt_ComputesPercentageScoreAndEligibilityScore()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 100);
            var exam = new Exam { Id = 1, TotalMarks = 100, PassingMarks = 50 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 75 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal(75.00m, attempt.PercentageScore);
            Assert.Equal(75, attempt.EligibilityScore);
        }

        [Fact]
        public void ScoreAttempt_ZeroTotalMarks_PercentageScoreIsZeroNotDivideByZero()
        {
            using var db = BuildContext();
            var exam = new Exam { Id = 1, TotalMarks = 0, PassingMarks = 0 };
            var attempt = new ExamAttempt { ExamId = 1 };

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal(0, attempt.PercentageScore);
        }

        [Fact]
        public void ScoreAttempt_SetsStatusToEvaluated()
        {
            using var db = BuildContext();
            var exam = new Exam { Id = 1, TotalMarks = 0, PassingMarks = 0 };
            var attempt = new ExamAttempt { ExamId = 1, Status = "Submitted" };

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal("Evaluated", attempt.Status);
        }

        [Fact]
        public void ScoreAttempt_NoQuestionOrder_UsesAssessmentTotalMarks()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 10);
            var q2 = MakeQuestion(2, marks: 20);
            var exam = new Exam { Id = 1, TotalMarks = 999, PassingMarks = 0 };
            exam.Questions.Add(q1);
            exam.Questions.Add(q2);

            var attempt = new ExamAttempt { ExamId = 1, QuestionOrder = "" };

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal(999, attempt.TotalMarks);
        }

        [Fact]
        public void ScoreAttempt_WithQuestionOrder_TotalMarksIsSumOfOnlySelectedQuestions()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 10);
            var q2 = MakeQuestion(2, marks: 20);
            var q3 = MakeQuestion(3, marks: 30);
            // Assessment.TotalMarks (999) should be ignored entirely once QuestionOrder
            // names a specific subset — only the marks of the selected questions count,
            // matching how a randomized-subset assessment is actually scored.
            var exam = new Exam { Id = 1, TotalMarks = 999, PassingMarks = 0 };
            exam.Questions.Add(q1);
            exam.Questions.Add(q2);
            exam.Questions.Add(q3);

            var attempt = new ExamAttempt { ExamId = 1, QuestionOrder = "1,2" };

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal(30, attempt.TotalMarks);
        }

        [Fact]
        public void ScoreAttempt_BackfillsMissingAnswerQuestionFromAssessmentQuestions()
        {
            // Mirrors real usage: an EF-loaded UserAnswer's Question navigation can come
            // back null depending on the query, while QuestionId is always populated —
            // ScoreAttempt is supposed to backfill it from the assessment's own Questions
            // list rather than silently treating the answer as worth 0 marks.
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 40);
            var exam = new Exam { Id = 1, TotalMarks = 40, PassingMarks = 0 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = null, ScoreAwarded = 40 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            var score = db.Scores.Local.Single();
            Assert.Equal(40, score.MaxScore);
        }

        [Fact]
        public void ScoreAttempt_GroupsSectionScoresByAssessmentSectionName()
        {
            using var db = BuildContext();
            var section = new AssessmentSection { Id = 1, Name = "Aptitude" };
            var q1 = MakeQuestion(1, marks: 10, section: section);
            var exam = new Exam { Id = 1, TotalMarks = 10, PassingMarks = 0 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 8 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            var score = db.Scores.Local.Single();
            Assert.Equal("Aptitude", score.SectionName);
            Assert.Equal(10, score.MaxScore);
            Assert.Equal(8, score.AwardedScore);
        }

        [Fact]
        public void ScoreAttempt_NoSection_FallsBackToQuestionCategory()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 10, category: "Coding");
            var exam = new Exam { Id = 1, TotalMarks = 10, PassingMarks = 0 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 5 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal("Coding", db.Scores.Local.Single().SectionName);
        }

        [Fact]
        public void ScoreAttempt_UnknownQuestion_FallsBackToGeneralSection()
        {
            // An answer referencing a question that isn't in the assessment's Questions
            // list at all (e.g. a deleted question) can't be backfilled and has no
            // Category to group by — should land in "General" rather than throwing.
            using var db = BuildContext();
            var exam = new Exam { Id = 1, TotalMarks = 0, PassingMarks = 0 };

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 9999, Question = null, ScoreAwarded = 0 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal("General", db.Scores.Local.Single().SectionName);
        }

        [Fact]
        public void ScoreAttempt_RemovesPreviouslySavedScoresForThisAttemptBeforeAddingNew()
        {
            // Re-evaluating an attempt (e.g. after a manual regrade) shouldn't leave old
            // section-score rows lying around next to the freshly computed ones.
            using var db = BuildContext();
            var attempt = new ExamAttempt { Id = 5, ExamId = 1 };
            db.Scores.Add(new Score { AttemptId = 5, SectionName = "Stale Section", MaxScore = 100, AwardedScore = 10 });
            db.SaveChanges();

            var q1 = MakeQuestion(1, marks: 10);
            var exam = new Exam { Id = 1, TotalMarks = 10, PassingMarks = 0 };
            exam.Questions.Add(q1);
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 10 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);
            db.SaveChanges();

            var remainingScores = db.Scores.Where(s => s.AttemptId == 5).ToList();
            Assert.DoesNotContain(remainingScores, s => s.SectionName == "Stale Section");
            Assert.Contains(remainingScores, s => s.SectionName == "Technical MCQ");
        }

        // ── Manual grading (Coding/Subjective/Short/Case) ───────────────────────
        // Regression coverage for the fix to a real scoring-integrity bug: these
        // question types used to get auto-awarded full marks for any non-blank
        // answer, regardless of content. They're now scored 0 by ScoreAttempt and
        // must go through FinalizeManualGrading (admin/recruiter-entered scores)
        // before the attempt is considered fully Evaluated.

        [Theory]
        [InlineData("Coding Questions")]
        [InlineData("Subjective Questions")]
        [InlineData("Short Answer")]
        [InlineData("Case Study")]
        public void RequiresManualGrading_TrueForUnGradableTypes(string questionType)
        {
            var question = new Question { QuestionType = questionType };
            Assert.True(AssessmentScoringService.RequiresManualGrading(question));
        }

        [Theory]
        [InlineData("Single Choice MCQ")]
        [InlineData("Multiple Choice MCQ")]
        [InlineData("True/False")]
        [InlineData("Fill in the Blank")]
        public void RequiresManualGrading_FalseForObjectiveTypes(string questionType)
        {
            var question = new Question { QuestionType = questionType };
            Assert.False(AssessmentScoringService.RequiresManualGrading(question));
        }

        [Fact]
        public void ScoreAttempt_AttemptHasCodingQuestion_StatusIsPendingManualReviewNotEvaluated()
        {
            using var db = BuildContext();
            var q1 = new Question { Id = 1, Marks = 20, QuestionType = "Coding Questions", Text = "Write a function" };
            var exam = new Exam { Id = 1, TotalMarks = 20, PassingMarks = 10 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            // Mirrors ExamController.Submit's new behavior: a manually-graded question is
            // scored 0 at submit time, regardless of whether an answer/code was provided.
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 0, TextAnswer = "def solve(): pass" });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal("PendingManualReview", attempt.Status);
        }

        [Fact]
        public void ScoreAttempt_MixOfGradedAndPendingQuestions_ScoreOnlyReflectsGradedPortion()
        {
            using var db = BuildContext();
            var mcq = MakeQuestion(1, marks: 10);
            var coding = new Question { Id = 2, Marks = 20, QuestionType = "Coding Questions", Text = "Write a function" };
            var exam = new Exam { Id = 1, TotalMarks = 30, PassingMarks = 5 };
            exam.Questions.Add(mcq);
            exam.Questions.Add(coding);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = mcq, ScoreAwarded = 10 });
            attempt.Answers.Add(new UserAnswer { QuestionId = 2, Question = coding, ScoreAwarded = 0 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal(10, attempt.Score); // only the MCQ's marks — coding contributes 0 until graded
            Assert.Equal("PendingManualReview", attempt.Status);
        }

        [Fact]
        public void ScoreAttempt_NoManualGradableQuestions_StatusIsEvaluated()
        {
            using var db = BuildContext();
            var q1 = MakeQuestion(1, marks: 10); // defaults to "Single Choice MCQ"
            var exam = new Exam { Id = 1, TotalMarks = 10, PassingMarks = 5 };
            exam.Questions.Add(q1);

            var attempt = new ExamAttempt { ExamId = 1 };
            attempt.Answers.Add(new UserAnswer { QuestionId = 1, Question = q1, ScoreAwarded = 10 });

            new AssessmentScoringService(db).ScoreAttempt(attempt, exam);

            Assert.Equal("Evaluated", attempt.Status);
        }

        [Fact]
        public void FinalizeManualGrading_AppliesAdminScoresAndSetsStatusToEvaluated()
        {
            using var db = BuildContext();
            var mcq = MakeQuestion(1, marks: 10);
            var coding = new Question { Id = 2, Marks = 20, QuestionType = "Coding Questions", Text = "Write a function" };
            var exam = new Exam { Id = 1, TotalMarks = 30, PassingMarks = 20 };
            exam.Questions.Add(mcq);
            exam.Questions.Add(coding);

            var attempt = new ExamAttempt { Id = 1, ExamId = 1 };
            var mcqAnswer = new UserAnswer { Id = 1, QuestionId = 1, Question = mcq, ScoreAwarded = 10 };
            var codingAnswer = new UserAnswer { Id = 2, QuestionId = 2, Question = coding, ScoreAwarded = 0 };
            attempt.Answers.Add(mcqAnswer);
            attempt.Answers.Add(codingAnswer);

            var service = new AssessmentScoringService(db);
            service.ScoreAttempt(attempt, exam); // initial submit-time pass
            Assert.Equal("PendingManualReview", attempt.Status);
            Assert.Equal(10, attempt.Score);

            // Admin awards 15/20 for the coding answer.
            service.FinalizeManualGrading(attempt, exam, new Dictionary<int, decimal> { [2] = 15m });

            Assert.Equal("Evaluated", attempt.Status);
            Assert.Equal(25, attempt.Score); // 10 (MCQ) + 15 (coding, now graded)
            Assert.True(attempt.Passed); // 25 >= PassingMarks (20)
            Assert.Equal(15, codingAnswer.ScoreAwarded);
        }

        [Fact]
        public void FinalizeManualGrading_ClampsScoreToQuestionMarksRange()
        {
            using var db = BuildContext();
            var coding = new Question { Id = 1, Marks = 10, QuestionType = "Coding Questions", Text = "Write a function" };
            var exam = new Exam { Id = 1, TotalMarks = 10, PassingMarks = 5 };
            exam.Questions.Add(coding);

            var attempt = new ExamAttempt { Id = 1, ExamId = 1 };
            var answer = new UserAnswer { Id = 1, QuestionId = 1, Question = coding, ScoreAwarded = 0 };
            attempt.Answers.Add(answer);

            var service = new AssessmentScoringService(db);

            // An out-of-range score (negative, or above the question's own max) should
            // never be stored as-is — clamp to [0, Marks].
            service.FinalizeManualGrading(attempt, exam, new Dictionary<int, decimal> { [1] = 999m });
            Assert.Equal(10, answer.ScoreAwarded);

            service.FinalizeManualGrading(attempt, exam, new Dictionary<int, decimal> { [1] = -5m });
            Assert.Equal(0, answer.ScoreAwarded);
        }

        [Fact]
        public void FinalizeManualGrading_LeavesUnmentionedAnswersUnchanged()
        {
            // Only answers actually present in scoresByAnswerId get updated — a partial
            // grading pass (e.g. one question graded now, another later) shouldn't wipe
            // out a score that was already entered for a different question.
            using var db = BuildContext();
            var coding1 = new Question { Id = 1, Marks = 10, QuestionType = "Coding Questions", Text = "Q1" };
            var coding2 = new Question { Id = 2, Marks = 10, QuestionType = "Coding Questions", Text = "Q2" };
            var exam = new Exam { Id = 1, TotalMarks = 20, PassingMarks = 5 };
            exam.Questions.Add(coding1);
            exam.Questions.Add(coding2);

            var attempt = new ExamAttempt { Id = 1, ExamId = 1 };
            var answer1 = new UserAnswer { Id = 1, QuestionId = 1, Question = coding1, ScoreAwarded = 7 };
            var answer2 = new UserAnswer { Id = 2, QuestionId = 2, Question = coding2, ScoreAwarded = 0 };
            attempt.Answers.Add(answer1);
            attempt.Answers.Add(answer2);

            var service = new AssessmentScoringService(db);
            service.FinalizeManualGrading(attempt, exam, new Dictionary<int, decimal> { [2] = 8m });

            Assert.Equal(7, answer1.ScoreAwarded); // untouched
            Assert.Equal(8, answer2.ScoreAwarded);
        }

        [Fact]
        public void ScoreAttempt_RanksSubmittedAttemptsByPercentageScoreDescending()
        {
            using var db = BuildContext();
            const int examId = 100;

            var attemptB = new ExamAttempt { ExamId = examId, PercentageScore = 90m, SubmittedAt = new DateTime(2026, 1, 1) };
            var attemptC = new ExamAttempt { ExamId = examId, PercentageScore = 70m, SubmittedAt = new DateTime(2026, 1, 2) };
            db.ExamAttempts.Add(attemptB);
            db.ExamAttempts.Add(attemptC);
            db.SaveChanges();

            // A separate attempt, deliberately never added to the context, purely to
            // trigger the ranking pass — see the class-level design note above.
            var triggerAttempt = new ExamAttempt { ExamId = examId };
            var exam = new Exam { Id = examId, TotalMarks = 0, PassingMarks = 0 };

            new AssessmentScoringService(db).ScoreAttempt(triggerAttempt, exam);

            Assert.Equal(1, attemptB.Rank);
            Assert.Equal(2, attemptC.Rank);
        }

        [Fact]
        public void ScoreAttempt_RankingTieBreaksByEarlierSubmittedAtFirst()
        {
            using var db = BuildContext();
            const int examId = 200;

            var earlier = new ExamAttempt { ExamId = examId, PercentageScore = 80m, SubmittedAt = new DateTime(2026, 1, 1, 9, 0, 0) };
            var later = new ExamAttempt { ExamId = examId, PercentageScore = 80m, SubmittedAt = new DateTime(2026, 1, 1, 10, 0, 0) };
            db.ExamAttempts.Add(earlier);
            db.ExamAttempts.Add(later);
            db.SaveChanges();

            var triggerAttempt = new ExamAttempt { ExamId = examId };
            var exam = new Exam { Id = examId, TotalMarks = 0, PassingMarks = 0 };

            new AssessmentScoringService(db).ScoreAttempt(triggerAttempt, exam);

            Assert.Equal(1, earlier.Rank);
            Assert.Equal(2, later.Rank);
        }

        [Fact]
        public void ScoreAttempt_RankingIgnoresAttemptsFromOtherExams()
        {
            using var db = BuildContext();
            var sameExam = new ExamAttempt { ExamId = 300, PercentageScore = 50m, SubmittedAt = DateTime.UtcNow };
            var otherExam = new ExamAttempt { ExamId = 301, PercentageScore = 99m, SubmittedAt = DateTime.UtcNow };
            db.ExamAttempts.Add(sameExam);
            db.ExamAttempts.Add(otherExam);
            db.SaveChanges();

            var triggerAttempt = new ExamAttempt { ExamId = 300 };
            var exam = new Exam { Id = 300, TotalMarks = 0, PassingMarks = 0 };

            new AssessmentScoringService(db).ScoreAttempt(triggerAttempt, exam);

            Assert.Equal(1, sameExam.Rank);
            Assert.Equal(0, otherExam.Rank); // untouched — different exam, never ranked
        }

        [Fact]
        public void ScoreAttempt_RankingIgnoresNotYetSubmittedAttempts()
        {
            using var db = BuildContext();
            const int examId = 400;
            var submitted = new ExamAttempt { ExamId = examId, PercentageScore = 40m, SubmittedAt = DateTime.UtcNow };
            var inProgress = new ExamAttempt { ExamId = examId, PercentageScore = 0m, SubmittedAt = null };
            db.ExamAttempts.Add(submitted);
            db.ExamAttempts.Add(inProgress);
            db.SaveChanges();

            var triggerAttempt = new ExamAttempt { ExamId = examId };
            var exam = new Exam { Id = examId, TotalMarks = 0, PassingMarks = 0 };

            new AssessmentScoringService(db).ScoreAttempt(triggerAttempt, exam);

            Assert.Equal(1, submitted.Rank);
            Assert.Equal(0, inProgress.Rank); // untouched — SubmittedAt is null
        }
    }
}
