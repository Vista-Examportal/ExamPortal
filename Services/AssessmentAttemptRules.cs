namespace ExamPortal.Services
{
    /// <summary>What <c>ExamController.Start</c> should do when the candidate already has an
    /// unfinished attempt (or none) for the assessment.</summary>
    public enum StartAttemptDecision
    {
        /// <summary>No unfinished attempt: create a new one.</summary>
        CreateNew,
        /// <summary>Unfinished attempt and the assessment allows resuming: reopen it.</summary>
        ResumeExisting,
        /// <summary>Unfinished attempt but resuming is not allowed: close it out with the answers
        /// already saved. A second, fresh attempt must not be opened next to it — that would leave two
        /// active attempts and give the candidate a new timer and question set.</summary>
        SubmitAbandoned
    }

    public static class AssessmentAttemptRules
    {
        public static StartAttemptDecision DecideStart(bool hasUnfinishedAttempt, bool allowResume) =>
            !hasUnfinishedAttempt ? StartAttemptDecision.CreateNew
            : allowResume ? StartAttemptDecision.ResumeExisting
            : StartAttemptDecision.SubmitAbandoned;
    }
}
