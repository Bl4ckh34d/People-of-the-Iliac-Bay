using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcHealthCarePhase { Seeking = 1, Acting = 2, Blocked = 3, Recovered = 4 }

    [Serializable]
    public sealed class NpcHealthCareRecord
    {
        public string PersonId, Id;
        public long Revision = 1;
        public double StartedAt, UpdatedAt, RetryAt;
        public string TaskId, LastFailure;
        public NpcHealthCarePhase Phase;
        public NpcActionTarget PausedPosition;
        // Episode-scoped fact: an actual care task was atomically accepted and bound.
        public bool HasAcceptedTreatment;
    }

    public static class NpcHealthCare
    {
        public const double RetryDelaySeconds = 300d;
        private const string EpisodePrefix = "care:v1:";
        private const string TaskPrefix = "npc-health-care:";

        public static string MakeTaskId(NpcHealthCareRecord row)
        {
            if (row == null || !ValidEpisodeId(row.Id) || row.Revision < 1)
                throw new ArgumentException("Invalid NPC health-care row.", nameof(row));
            return TaskPrefix + row.Id + ":attempt:" + row.Revision.ToString(CultureInfo.InvariantCulture);
        }

        internal static string EpisodeId(string personId, long vitalsRevision)
        {
            string key = personId + "\n" + vitalsRevision.ToString(CultureInfo.InvariantCulture);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return EpisodePrefix + text;
            }
        }

        internal static NpcHealthCareRecord CopyRecord(NpcHealthCareRecord value)
        {
            if (value == null) return null;
            return new NpcHealthCareRecord
            {
                PersonId = value.PersonId, Id = value.Id, Revision = value.Revision,
                StartedAt = value.StartedAt, UpdatedAt = value.UpdatedAt, RetryAt = value.RetryAt,
                TaskId = value.TaskId, LastFailure = value.LastFailure, Phase = value.Phase,
                PausedPosition = value.PausedPosition == null ? null : NpcActionExecutor.CopyTarget(value.PausedPosition),
                HasAcceptedTreatment = value.HasAcceptedTreatment
            };
        }

        internal static bool IsSupportedCareTaskPlan(NpcTaskPlan task)
        {
            if (task == null || task.Steps == null || task.Steps.Count == 0) return false;
            bool hasTreatment = false;
            for (int i = 0; i < task.Steps.Count; i++)
            {
                NpcTaskStep step = task.Steps[i];
                if (step == null) return false;
                switch (step.Kind)
                {
                    case NpcActionKind.MoveTo:
                        break;
                    case NpcActionKind.Purchase:
                        bool consumedByLaterUse = false;
                        for (int j = i + 1; j < task.Steps.Count; j++)
                        {
                            NpcTaskStep later = task.Steps[j];
                            if (later != null && later.Kind == NpcActionKind.UseHealingItem &&
                                later.ItemSourceStep == i + 1)
                            {
                                consumedByLaterUse = true;
                                break;
                            }
                        }
                        if (!consumedByLaterUse) return false;
                        break;
                    case NpcActionKind.UseHealingItem:
                        hasTreatment = true;
                        if (step.ItemSourceStep > 0)
                        {
                            int sourceIndex = step.ItemSourceStep - 1;
                            if (sourceIndex >= i || sourceIndex < 0 ||
                                task.Steps[sourceIndex] == null || task.Steps[sourceIndex].Kind != NpcActionKind.Purchase ||
                                task.Steps[sourceIndex].Target == null || step.Target == null ||
                                task.Steps[sourceIndex].Target.ItemId != step.Target.ItemId ||
                                task.Steps[sourceIndex].Target.Quantity != step.Target.Quantity)
                                return false;
                        }
                        break;
                    case NpcActionKind.RestToHeal:
                        hasTreatment = true;
                        if (step.ItemSourceStep != 0) return false;
                        break;
                    case NpcActionKind.CastSpell:
                        hasTreatment = true;
                        if (step.ItemSourceStep != 0 || step.MagicHealing == null) return false;
                        break;
                    default:
                        return false;
                }
            }
            return hasTreatment;
        }

        internal static bool IsBoundCareTask(NpcHealthCareRecord row, NpcTaskPlan task)
        {
            if (row == null || task == null || task.Id != row.TaskId || task.ResidentId != row.PersonId ||
                task.ProposalId != task.Id || !IsSupportedCareTaskPlan(task) ||
                task.AcceptedAt < row.StartedAt || task.AcceptedAt > row.UpdatedAt)
                return false;
            string prefix = TaskPrefix + row.Id + ":attempt:";
            if (!task.Id.StartsWith(prefix, StringComparison.Ordinal)) return false;
            long attempt;
            string suffix = task.Id.Substring(prefix.Length);
            return long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out attempt) &&
                attempt >= 1 && attempt < row.Revision;
        }

        internal static void ValidateRecord(NpcHealthCareRecord row)
        {
            if (row == null || !ValidId(row.PersonId) || !ValidEpisodeId(row.Id) || row.Revision < 1 ||
                row.HasAcceptedTreatment && row.Revision < 2 ||
                !Finite(row.StartedAt) || row.StartedAt < 0 || !Finite(row.UpdatedAt) || row.UpdatedAt < row.StartedAt ||
                !Finite(row.RetryAt) || row.RetryAt < 0 || (int)row.Phase < (int)NpcHealthCarePhase.Seeking ||
                (int)row.Phase > (int)NpcHealthCarePhase.Recovered ||
                row.LastFailure != null && (string.IsNullOrWhiteSpace(row.LastFailure) || row.LastFailure.Length > 400))
                throw new ArgumentException("Invalid NPC health-care record.");

            bool expectsTask = row.Phase == NpcHealthCarePhase.Acting;
            if (expectsTask != ValidId(row.TaskId) ||
                row.Phase == NpcHealthCarePhase.Blocked && (row.TaskId != null || row.LastFailure == null || row.RetryAt < row.UpdatedAt) ||
                row.Phase != NpcHealthCarePhase.Blocked && row.RetryAt != 0)
                throw new ArgumentException("Invalid NPC health-care progress.");

            if (row.PausedPosition != null)
            {
                NpcActionTarget ignored;
                if (!TryCopyStaticPosition(row.PausedPosition, out ignored))
                    throw new ArgumentException("Invalid NPC health-care paused position.");
            }
        }

        internal static bool TryCopyStaticPosition(NpcActionTarget value, out NpcActionTarget copy)
        {
            copy = null;
            if (value == null ||
                value.Kind != NpcActionTargetKind.IndoorPoint && value.Kind != NpcActionTargetKind.WorldPoint ||
                value.PersonId != null || value.ContainerId != null || value.ItemId != null ||
                value.Quantity != 1 || value.QuotedGold != 0 ||
                value.TransferDirection != NpcActionTransferDirection.NpcToCounterparty ||
                value.Kind == NpcActionTargetKind.IndoorPoint &&
                    (value.SettlementId < 0 || value.BuildingKey < 0 || value.WorldSpaceId != null) ||
                value.Kind == NpcActionTargetKind.WorldPoint &&
                    (value.SettlementId < -1 || value.BuildingKey != -1))
                return false;
            try { NpcActionContract.ValidateTarget(value); }
            catch (ArgumentException) { return false; }
            copy = NpcActionExecutor.CopyTarget(value);
            return true;
        }

        internal static bool SamePosition(NpcActionTarget first, NpcActionTarget second)
        {
            return first != null && second != null && first.Kind == second.Kind &&
                first.SettlementId == second.SettlementId && first.BuildingKey == second.BuildingKey &&
                first.WorldSpaceId == second.WorldSpaceId && first.X == second.X && first.Y == second.Y && first.Z == second.Z;
        }

        internal static bool ValidEpisodeId(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(EpisodePrefix, StringComparison.Ordinal) || value.Length != EpisodePrefix.Length + 64)
                return false;
            for (int i = EpisodePrefix.Length; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        private static bool ValidId(string value)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= 240; }
        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed partial class WorldStateStore
    {
        public NpcHealthCareRecord ReadNpcHealthCare(string personId)
        {
            if (string.IsNullOrWhiteSpace(personId) || state.NpcHealthCares == null) return null;
            NpcHealthCareRecord row = state.NpcHealthCares.Find(value => value != null && value.PersonId == personId);
            return NpcHealthCare.CopyRecord(row);
        }

        public bool TryBeginNpcHealthCare(string personId, NpcArchetype archetype, double now)
        { return TryBeginNpcHealthCare(personId, archetype, now, null); }

        public bool TryBeginNpcHealthCare(string personId, NpcArchetype archetype, double now, NpcActionTarget pausedPosition)
        {
            if (string.IsNullOrWhiteSpace(personId) || !CareTimeValid(now) || !Enum.IsDefined(typeof(NpcArchetype), archetype)) return false;
            Resident resident = ResolveResident(personId);
            if (resident == null || !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,
                resident.AgeGroup == ResidentAgeGroup.Adult, resident.IsQuestProtected)) return false;

            NpcVitalsView vitals;
            if (!TryReadObservedVitals(personId, archetype, now, out vitals) ||
                !NpcHealthNeedLifecycle.ShouldInterruptWork(vitals)) return false;

            NpcActionTarget copiedPosition = null;
            if (pausedPosition != null && !NpcHealthCare.TryCopyStaticPosition(pausedPosition, out copiedPosition)) return false;
            if (state.NpcHealthCares == null) return false;

            int index = FindHealthCareIndex(personId);
            if (index >= 0)
            {
                NpcHealthCareRecord previous = state.NpcHealthCares[index];
                string episodeId = NpcHealthCare.EpisodeId(personId, vitals.Revision);
                if (previous.Phase != NpcHealthCarePhase.Recovered || previous.Id == episodeId) return false;
                state.NpcHealthCares[index] = new NpcHealthCareRecord { PersonId = personId, Id = episodeId,
                    Revision = 1, StartedAt = now, UpdatedAt = now, Phase = NpcHealthCarePhase.Seeking,
                    PausedPosition = copiedPosition, HasAcceptedTreatment = false };
                return true;
            }

            state.NpcHealthCares.Add(new NpcHealthCareRecord { PersonId = personId,
                Id = NpcHealthCare.EpisodeId(personId, vitals.Revision), Revision = 1,
                StartedAt = now, UpdatedAt = now, Phase = NpcHealthCarePhase.Seeking,
                PausedPosition = copiedPosition, HasAcceptedTreatment = false });
            return true;
        }

        public bool TryBindNpcHealthCareTask(string personId, long expectedRevision, string taskId, double now)
        {
            NpcTaskPlan task = ReadNpcTask(taskId);
            int index; NpcHealthCareRecord prepared;
            if (!TryPrepareNpcHealthCareTaskBinding(personId, expectedRevision, task, now, true, out index, out prepared))
                return false;
            state.NpcHealthCares[index] = prepared;
            return true;
        }

        /// <summary>
        /// Checks the complete care transition before accepting the task. Task acceptance is the only
        /// fallible publication; the prepared care row is assigned immediately afterward without callbacks.
        /// </summary>
        public bool TryAcceptAndBindNpcHealthCareTask(string personId, long expectedCareRevision,
            NpcTaskPlan proposed, double now, out string reason)
        {
            reason = "The health-care task is no longer available.";
            if (proposed == null || proposed.Admission != null) return false;
            int index; NpcHealthCareRecord prepared;
            if (!TryPrepareNpcHealthCareTaskBinding(personId, expectedCareRevision, proposed, now, false,
                out index, out prepared)) return false;
            NpcTaskPlan admitted = NpcTaskContract.Copy(proposed);
            admitted.Admission = NpcOperationAdmission.FromHealthCare(state.NpcHealthCares[index], expectedCareRevision);
            if (!TryAcceptNpcTaskClassified(admitted, true, admitted.Admission, out reason)) return false;
            prepared.HasAcceptedTreatment = true;
            state.NpcHealthCares[index] = prepared;
            return true;
        }

        private bool TryPrepareNpcHealthCareTaskBinding(string personId, long expectedRevision,
            NpcTaskPlan task, double now, bool alreadyAccepted, out int index, out NpcHealthCareRecord prepared)
        {
            index = FindHealthCareIndex(personId);
            prepared = null;
            if (index < 0 || !CareTimeValid(now) || task == null) return false;
            NpcHealthCareRecord row = NpcHealthCare.CopyRecord(state.NpcHealthCares[index]);
            bool canStart = row.Phase == NpcHealthCarePhase.Seeking ||
                row.Phase == NpcHealthCarePhase.Blocked && now >= row.RetryAt;
            if (!MutableCare(row, expectedRevision, now) || !canStart || row.TaskId != null ||
                !string.Equals(task.Id, NpcHealthCare.MakeTaskId(row), StringComparison.Ordinal) ||
                task.ResidentId != personId || task.Phase != NpcTaskPhase.Ready ||
                task.ProposalId != task.Id || task.AcceptedAt != now || task.AcceptedAt < row.StartedAt ||
                !NpcHealthCare.IsSupportedCareTaskPlan(task)) return false;
            if (task.Steps.Exists(step => step.Kind == NpcActionKind.CastSpell) &&
                (task.GoalId != NpcHealthNeedLifecycle.RecoveryGoalId || task.GoalStartRevision < 1)) return false;
            if (alreadyAccepted)
            {
                NpcTaskPlan active = ActiveNpcTask(personId);
                if (active == null || active.Id != task.Id || active.Phase != NpcTaskPhase.Ready) return false;
            }
            else
            {
                if (ActiveNpcTask(personId) != null || task.Revision != 1 || task.CurrentStepIndex != 0 ||
                    task.UpdatedAt != task.AcceptedAt || task.ActiveActionId != null || task.FailureReason != null ||
                    task.FailureReceipt != null) return false;
            }
            try { NpcTaskContract.Validate(task); }
            catch (ArgumentException) { return false; }
            row.Phase = NpcHealthCarePhase.Acting;
            row.TaskId = task.Id;
            row.RetryAt = 0;
            row.UpdatedAt = now;
            row.Revision++;
            prepared = row;
            return true;
        }

        public bool TryBlockNpcHealthCare(string personId, long expectedRevision, double now, string reason)
        {
            int index = FindHealthCareIndex(personId);
            if (index < 0 || !CareTimeValid(now) || string.IsNullOrWhiteSpace(reason) || reason.Length > 400) return false;
            NpcHealthCareRecord row = NpcHealthCare.CopyRecord(state.NpcHealthCares[index]);
            bool retryExpired = row.Phase == NpcHealthCarePhase.Blocked && now >= row.RetryAt;
            if (!MutableCare(row, expectedRevision, now) ||
                row.Phase != NpcHealthCarePhase.Seeking && row.Phase != NpcHealthCarePhase.Acting && !retryExpired ||
                ActiveNpcTask(personId) != null) return false;
            if (row.Phase == NpcHealthCarePhase.Acting)
            {
                NpcTaskPlan task = ReadNpcTask(row.TaskId);
                if (task == null || task.ResidentId != personId || !NpcTaskContract.IsTerminal(task)) return false;
            }

            double retryAt = now + NpcHealthCare.RetryDelaySeconds;
            if (!CareTimeValid(retryAt)) return false;
            row.Phase = NpcHealthCarePhase.Blocked;
            row.TaskId = null;
            row.LastFailure = reason.Trim();
            row.RetryAt = retryAt;
            row.UpdatedAt = now;
            row.Revision++;
            state.NpcHealthCares[index] = row;
            return true;
        }

        /// <summary>Replan promptly only after a completed, receipted treatment still leaves observed injury.</summary>
        public bool TryContinueNpcHealthCareAfterTreatment(string personId, long expectedRevision,
            NpcArchetype archetype, double now, NpcActionTarget observedPosition)
        {
            int index = FindHealthCareIndex(personId);
            if (index < 0 || !CareTimeValid(now)) return false;
            NpcHealthCareRecord row = NpcHealthCare.CopyRecord(state.NpcHealthCares[index]);
            NpcActionTarget position;
            if (!MutableCare(row, expectedRevision, now) || row.Phase != NpcHealthCarePhase.Acting ||
                !NpcHealthCare.TryCopyStaticPosition(observedPosition, out position) ||
                ActiveNpcTask(personId) != null) return false;
            NpcTaskPlan task = ReadNpcTask(row.TaskId);
            if (task == null || task.ResidentId != personId || task.Phase != NpcTaskPhase.Completed ||
                !NpcHealthCare.IsSupportedCareTaskPlan(task) || task.Steps.Count == 0 ||
                task.CurrentStepIndex != task.Steps.Count) return false;
            int last = task.Steps.Count - 1;
            NpcTaskStep step = task.Steps[last];
            if (step.Kind != NpcActionKind.UseHealingItem && step.Kind != NpcActionKind.RestToHeal &&
                step.Kind != NpcActionKind.CastSpell) return false;
            NpcActionJob action = ReadNpcAction(task.Id + ":step:" + last);
            if (action == null || action.ResidentId != personId || action.Phase != NpcActionPhase.Completed ||
                !action.ReturningToRoutine || !NpcHealthCare.SamePosition(action.LastPosition, position) ||
                action.CompletedEffectId != step.CompletedEffectId || action.TreatmentReceipt == null ||
                action.TreatmentReceipt.HealthAfter <= action.TreatmentReceipt.HealthBefore ||
                action.TreatmentReceipt.Kind != (step.Kind == NpcActionKind.RestToHeal ?
                    NpcTreatmentKind.Rest : step.Kind == NpcActionKind.CastSpell ?
                    NpcTreatmentKind.MagicSelfCast : NpcTreatmentKind.HealingItem)) return false;
            NpcVitalsView vitals;
            if (!TryReadObservedVitals(personId, archetype, now, out vitals) ||
                vitals.CurrentHealth >= vitals.MaximumHealth) return false;

            row.Phase = NpcHealthCarePhase.Seeking;
            row.TaskId = null;
            row.PausedPosition = position;
            row.RetryAt = 0;
            row.LastFailure = null;
            row.UpdatedAt = now;
            row.Revision++;
            state.NpcHealthCares[index] = row;
            return true;
        }

        public bool TryUpdateNpcHealthCarePosition(string personId, long expectedRevision, double now, NpcActionTarget position)
        {
            int index = FindHealthCareIndex(personId);
            if (index < 0 || !CareTimeValid(now)) return false;
            NpcHealthCareRecord row = NpcHealthCare.CopyRecord(state.NpcHealthCares[index]);
            NpcActionTarget copied;
            if (!MutableCare(row, expectedRevision, now) || row.Phase != NpcHealthCarePhase.Acting ||
                !NpcHealthCare.TryCopyStaticPosition(position, out copied) ||
                ActiveNpcTask(personId) != null || !IsTerminalCareTask(row)) return false;
            if (NpcHealthCare.SamePosition(row.PausedPosition, copied)) return false;
            row.PausedPosition = copied;
            row.UpdatedAt = now;
            row.Revision++;
            state.NpcHealthCares[index] = row;
            return true;
        }

        public bool TryCompleteNpcHealthCare(string personId, long expectedRevision, NpcArchetype archetype, double now)
        {
            int index = FindHealthCareIndex(personId);
            if (index < 0 || !CareTimeValid(now) || !Enum.IsDefined(typeof(NpcArchetype), archetype)) return false;
            NpcHealthCareRecord row = NpcHealthCare.CopyRecord(state.NpcHealthCares[index]);
            Resident resident = ResolveResident(personId);
            if (!MutableCare(row, expectedRevision, now) || row.Phase == NpcHealthCarePhase.Recovered ||
                resident == null || !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,
                    resident.AgeGroup == ResidentAgeGroup.Adult, resident.IsQuestProtected) || ActiveNpcTask(personId) != null)
                return false;
            NpcVitalsView vitals;
            if (!TryReadObservedVitals(personId, archetype, now, out vitals) || !NpcHealthNeedLifecycle.HasRecoveredForWork(vitals) ||
                row.TaskId != null && !IsTerminalCareTask(row)) return false;

            row.Phase = NpcHealthCarePhase.Recovered;
            row.TaskId = null;
            row.RetryAt = 0;
            row.UpdatedAt = now;
            row.Revision++;
            state.NpcHealthCares[index] = row;
            return true;
        }

        private bool IsTerminalCareTask(NpcHealthCareRecord row)
        {
            NpcTaskPlan task = ReadNpcTask(row.TaskId);
            return task != null && task.ResidentId == row.PersonId && NpcTaskContract.IsTerminal(task);
        }

        private int FindHealthCareIndex(string personId)
        {
            return string.IsNullOrWhiteSpace(personId) || state.NpcHealthCares == null ? -1 :
                state.NpcHealthCares.FindIndex(row => row != null && row.PersonId == personId);
        }

        private static bool MutableCare(NpcHealthCareRecord row, long expectedRevision, double now)
        {
            return row != null && row.Revision == expectedRevision && expectedRevision < long.MaxValue &&
                CareTimeValid(now) && now >= row.UpdatedAt;
        }

        private bool TryReadObservedVitals(string personId, NpcArchetype archetype, double now, out NpcVitalsView vitals)
        {
            vitals = null;
            try { vitals = ReadNpcVitals(personId, archetype); }
            catch (ArgumentException) { return false; }
            return vitals != null && vitals.IsAlive && vitals.IsPersisted && vitals.Revision > 0 &&
                vitals.MaximumHealth > 0 && vitals.CurrentHealth > 0 && vitals.CurrentHealth <= vitals.MaximumHealth &&
                CareTimeValid(vitals.CapturedAt) && now >= vitals.CapturedAt;
        }

        private static bool CareTimeValid(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }

        private static List<NpcHealthCareRecord> CopyNpcHealthCareRecords(WorldState source, WorldState validated)
        {
            var result = new List<NpcHealthCareRecord>();
            Require(source.NpcHealthCares != null && source.NpcHealthCares.Count <= 100000,
                "Invalid NPC health-care ledger.");
            var residents = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementPopulation town in validated.Settlements)
                foreach (Household home in town.Households)
                    foreach (Resident resident in home.Residents) residents.Add(resident.Id);
            var tasks = new Dictionary<string, NpcTaskPlan>(StringComparer.Ordinal);
            foreach (NpcTaskPlan task in validated.NpcTasks) tasks.Add(task.Id, task);
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcHealthCareRecord row in source.NpcHealthCares)
            {
                NpcHealthCare.ValidateRecord(row);
                Require(residents.Contains(row.PersonId) && owners.Add(row.PersonId),
                    "Unknown or duplicate NPC health-care owner.");
                if (row.TaskId != null)
                {
                    NpcTaskPlan task;
                    Require(tasks.TryGetValue(row.TaskId, out task) && NpcHealthCare.IsBoundCareTask(row, task),
                        "NPC health-care task binding is invalid.");
                }
                NpcHealthCareRecord copied = NpcHealthCare.CopyRecord(row);
                // Pre-52 saves did not carry this fact; task shape/revision alone is not
                // proof of atomic admission, including after terminal-task retention.
                result.Add(copied);
            }
            return result;
        }
    }
}
