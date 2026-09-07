using System;
using System.Collections.Generic;
using System.Linq;
using ExamPortal.Models;

namespace ExamPortal.Services
{
    /// <summary>
    /// Computes where a candidate stands in the multi-round interview sequence
    /// (Technical → Managerial → HR → [Final] → Documents) from their existing
    /// InterviewRecord history — no new database fields or configuration table.
    ///
    /// Round-sequencing rules implemented here (see AdminController.Interviews.cs /
    /// AdminController.Documents.cs for where these are enforced, and
    /// Views/Admin/CandidateDetail.cshtml for where they drive the recruiter UI):
    ///   - Default required sequence: Technical, Managerial, HR.
    ///   - "Final" is optional. It only becomes part of the required sequence for a
    ///     candidate once the recruiter has actually scheduled a Final-round interview
    ///     for them — this reuses the existing "Final" option already in the Schedule
    ///     Interview round dropdown as the de-facto per-candidate configuration, rather
    ///     than inventing a new settings/config system.
    ///   - A round only unlocks once every prior required round has a Completed record
    ///     with Outcome == "Passed".
    ///   - Documents only unlock once every required round (including Final, if it's
    ///     part of the sequence for this candidate) has passed.
    ///   - A Failed round blocks all further scheduling/documents — the recruiter falls
    ///     back to the existing, always-available "Reject" action elsewhere on the page;
    ///     this helper does not itself change any status.
    ///   - A round currently Scheduled, or sitting in Missed/Cancelled awaiting
    ///     reschedule, also blocks moving on to the next round (existing
    ///     Complete/Reschedule UI handles bringing it to a terminal Passed/Failed state).
    /// </summary>
    public static class InterviewProgressionHelper
    {
        /// <summary>Default required rounds when no Final-round record exists yet for the candidate.</summary>
        public static readonly string[] BaseRequiredRounds = { "Technical", "Managerial", "HR" };

        public readonly struct RoundStatus
        {
            public string Round { get; }
            /// <summary>One of: "Not Started", "Scheduled", "Missed", "Cancelled", "Passed", "Failed", or a raw Outcome (e.g. "On Hold").</summary>
            public string DisplayStatus { get; }
            public RoundStatus(string round, string displayStatus) { Round = round; DisplayStatus = displayStatus; }
        }

        public readonly struct InterviewProgress
        {
            public string[] RequiredRounds { get; init; }
            public List<RoundStatus> RoundStatuses { get; init; }
            /// <summary>The next round the recruiter should schedule, or null if none is currently due.</summary>
            public string? NextRoundToSchedule { get; init; }
            /// <summary>A round that's currently Scheduled and awaiting completion, or null.</summary>
            public string? RoundInProgress { get; init; }
            /// <summary>A round sitting in Missed/Cancelled, awaiting reschedule, or null.</summary>
            public string? RoundAwaitingReschedule { get; init; }
            /// <summary>The first required round that came back Failed, or null if none has.</summary>
            public string? FailedRound { get; init; }
            /// <summary>True once every required round (per RequiredRounds) has Passed.</summary>
            public bool DocumentsUnlocked { get; init; }
            /// <summary>True when the base three rounds have all passed, no Final round has
            /// been scheduled yet, and nothing else is pending — i.e. the recruiter can
            /// choose to either add an optional Final round or go straight to Documents.</summary>
            public bool CanOptionallyScheduleFinal { get; init; }
        }

        public static InterviewProgress GetProgress(IEnumerable<InterviewRecord> interviews)
        {
            var list = interviews as IReadOnlyCollection<InterviewRecord> ?? interviews.ToList();
            var hasFinal = list.Any(i => i.Round == "Final");
            var required = hasFinal ? new[] { "Technical", "Managerial", "HR", "Final" } : BaseRequiredRounds;

            string? nextToSchedule = null;
            string? roundInProgress = null;
            string? roundAwaitingReschedule = null;
            string? failedRound = null;
            var statuses = new List<RoundStatus>();
            var passedCount = 0;

            foreach (var round in required)
            {
                // Most recent record for this round — in normal operation there is at most
                // one (scheduling a round only happens once it's actually due, and
                // reschedule/cancel/complete all mutate the same record in place), but
                // OrderByDescending(Id) keeps this correct even against older/irregular data.
                var record = list.Where(i => i.Round == round).OrderByDescending(i => i.Id).FirstOrDefault();

                if (record == null)
                {
                    statuses.Add(new RoundStatus(round, "Not Started"));
                    if (nextToSchedule == null && roundInProgress == null && roundAwaitingReschedule == null && failedRound == null)
                        nextToSchedule = round;
                    continue;
                }

                switch (record.Status)
                {
                    case "Scheduled":
                        statuses.Add(new RoundStatus(round, "Scheduled"));
                        if (roundInProgress == null && failedRound == null) roundInProgress = round;
                        break;

                    case "Missed":
                    case "Cancelled":
                        statuses.Add(new RoundStatus(round, record.Status));
                        if (roundAwaitingReschedule == null && failedRound == null) roundAwaitingReschedule = round;
                        break;

                    case "Completed" when record.Outcome == "Passed":
                        statuses.Add(new RoundStatus(round, "Passed"));
                        passedCount++;
                        break;

                    case "Completed" when record.Outcome == "Failed":
                        statuses.Add(new RoundStatus(round, "Failed"));
                        if (failedRound == null) failedRound = round;
                        break;

                    default:
                        // Completed + "On Hold" (or any other non-Passed/Failed outcome) —
                        // not a pass, not a fail; treat like an in-progress blocker so the
                        // recruiter resolves it before anything else in the sequence moves.
                        statuses.Add(new RoundStatus(round, string.IsNullOrWhiteSpace(record.Outcome) ? "Completed" : record.Outcome));
                        if (roundInProgress == null && failedRound == null) roundInProgress = round;
                        break;
                }
            }

            var documentsUnlocked = failedRound == null && passedCount == required.Length;
            var canOptionallyScheduleFinal =
                !hasFinal && failedRound == null && roundInProgress == null &&
                roundAwaitingReschedule == null && nextToSchedule == null;

            return new InterviewProgress
            {
                RequiredRounds = required,
                RoundStatuses = statuses,
                NextRoundToSchedule = failedRound == null ? nextToSchedule : null,
                RoundInProgress = failedRound == null ? roundInProgress : null,
                RoundAwaitingReschedule = failedRound == null ? roundAwaitingReschedule : null,
                FailedRound = failedRound,
                DocumentsUnlocked = documentsUnlocked,
                CanOptionallyScheduleFinal = canOptionallyScheduleFinal,
            };
        }

        /// <summary>True if scheduling `round` right now is a valid next step for this
        /// candidate — either it's the next required round in sequence, or it's an
        /// optional Final round being added after the base three have passed.</summary>
        public static bool IsValidRoundToSchedule(IEnumerable<InterviewRecord> interviews, string round)
        {
            var progress = GetProgress(interviews);
            if (round == progress.NextRoundToSchedule) return true;
            if (round == "Final" && progress.CanOptionallyScheduleFinal) return true;
            return false;
        }
    }
}
