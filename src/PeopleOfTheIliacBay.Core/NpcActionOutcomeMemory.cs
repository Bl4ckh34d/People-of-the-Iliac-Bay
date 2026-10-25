using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>Record one observed terminal action in its actor's existing personal journal.</summary>
        public bool TryRememberNpcActionOutcome(string actionId, long expectedRevision,
            string date, string place, string completedObservation)
        {
            var job = ReadNpcAction(actionId);
            if (job == null || job.Revision != expectedRevision ||
                !NpcActionExecutor.IsTerminal(job) || string.IsNullOrWhiteSpace(job.ResidentId) ||
                // Completed interactions and treatments have more precise native memory sources.
                job.Phase == NpcActionPhase.Completed &&
                    (job.Kind == NpcActionKind.Interact || job.Kind == NpcActionKind.UseHealingItem ||
                     job.Kind == NpcActionKind.RestToHeal ||
                     job.Kind == NpcActionKind.CastSpell && job.MagicHealing != null) ||
                // A failed task already publishes its own failure receipt.
                job.Phase == NpcActionPhase.Failed && job.Admission != null &&
                    job.Admission.Kind == NpcOperationAdmissionKind.TaskStep)
                return false;

            string outcome;
            if (job.Phase == NpcActionPhase.Completed)
            {
                if (string.IsNullOrWhiteSpace(job.CompletedEffectId) ||
                    string.IsNullOrWhiteSpace(completedObservation) ||
                    (job.Kind == NpcActionKind.Enter || job.Kind == NpcActionKind.Exit) &&
                        job.BuildingTransitionReceipt == null)
                    return false;
                outcome = "completed the accepted " + job.Kind + " action: " + completedObservation;
            }
            else if (job.Phase == NpcActionPhase.Failed)
            {
                if (string.IsNullOrWhiteSpace(job.FailureReason)) return false;
                outcome = "could not finish the accepted " + job.Kind + " action: " + job.FailureReason;
            }
            else if (job.Phase == NpcActionPhase.Cancelled)
            {
                if (string.IsNullOrWhiteSpace(job.FailureReason)) return false;
                outcome = "stopped the accepted " + job.Kind + " action: " + job.FailureReason;
            }
            else return false;

            var journal = MemoryFor(job.ResidentId, false);
            if (HasActionSource(journal, job.Id)) return false;
            var actor = ResolveResident(job.ResidentId);
            if (actor == null) return false;
            string actorName = CleanMemory(actor.FullName, 180);
            if (string.IsNullOrWhiteSpace(actorName)) actorName = "Person";
            var entry = new NpcMemoryEntry {
                At = job.UpdatedAt,
                Date = CleanMemory(date, 120),
                Place = CleanMemory(place, 300),
                PlayerText = "",
                NpcText = "",
                Observation = CleanMemory(actorName + " (person " + job.ResidentId + ") " + outcome, 600),
                Source = "observed",
                SourceActionId = job.Id
            };
            try { ValidateMemoryEntry(entry); }
            catch (ArgumentException) { return false; }
            Remember(job.ResidentId, entry);
            return true;
        }
    }
}
