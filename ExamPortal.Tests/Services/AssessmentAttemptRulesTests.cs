using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class AssessmentAttemptRulesTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NoUnfinishedAttempt_CreatesANewOne(bool allowResume) =>
            Assert.Equal(StartAttemptDecision.CreateNew, AssessmentAttemptRules.DecideStart(false, allowResume));

        [Fact]
        public void UnfinishedAttempt_WithResumeAllowed_ReopensIt() =>
            Assert.Equal(StartAttemptDecision.ResumeExisting, AssessmentAttemptRules.DecideStart(true, true));

        [Fact]
        public void UnfinishedAttempt_WithResumeDisabled_NeverCreatesASecondActiveAttempt()
        {
            var decision = AssessmentAttemptRules.DecideStart(true, false);

            Assert.Equal(StartAttemptDecision.SubmitAbandoned, decision);
            Assert.NotEqual(StartAttemptDecision.CreateNew, decision);
        }
    }
}
