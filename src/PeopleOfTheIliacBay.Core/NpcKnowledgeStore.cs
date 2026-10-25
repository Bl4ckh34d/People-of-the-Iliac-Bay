using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Write capability bound to one store, actor, persisted scope, save epoch, and ledger revision.</summary>
    public sealed class NpcKnowledgeWriteTicket
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal string ActorId;
        internal string SaveEpochId;
        internal long ExpectedRevision;
        internal bool LedgerExisted;
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Reads one currently known fact for a known actor. A missing ledger or topic is unknown and never allocates
        /// persisted state; dead actors remain queryable as historical owners.
        /// </summary>
        public bool TryReadNpcKnowledge(string actorId, string propositionId, out NpcKnowledgeFactView fact)
        {
            fact = null;
            if (!IsKnownKnowledgeActor(actorId)) return false;
            NpcKnowledgeState ledger = FindNpcKnowledgeLedger(actorId, out _);
            NpcKnowledgeSnapshot snapshot;
            if (ledger == null || !NpcKnowledgeLedger.TryCapture(ledger, actorId, NpcFrameworkScopeId, out snapshot))
                return false;
            return snapshot.TryGet(propositionId, out fact);
        }

        /// <summary>
        /// Captures an actor-only bounded query view. Known actors with no ledger receive a transient empty snapshot;
        /// no save row is created. Unknown actors receive no view.
        /// </summary>
        public bool TryCaptureNpcKnowledgeSnapshot(string actorId, out NpcKnowledgeSnapshot snapshot)
        {
            snapshot = null;
            if (!IsKnownKnowledgeActor(actorId)) return false;
            NpcKnowledgeState ledger = FindNpcKnowledgeLedger(actorId, out _);
            if (ledger == null) ledger = NpcKnowledgeLedger.Create(actorId, NpcFrameworkScopeId);
            return NpcKnowledgeLedger.TryCapture(ledger, actorId, NpcFrameworkScopeId, out snapshot);
        }

        /// <summary>Captures a CAS write capability without allocating a persisted ledger for a first write.</summary>
        public bool TryCaptureNpcKnowledgeWrite(string actorId, out NpcKnowledgeWriteTicket ticket)
        {
            ticket = null;
            if (!IsKnownKnowledgeActor(actorId)) return false;
            NpcKnowledgeState ledger = FindNpcKnowledgeLedger(actorId, out _);
            ticket = new NpcKnowledgeWriteTicket
            {
                Owner = this,
                Epoch = bindingEpoch,
                ActorId = actorId,
                SaveEpochId = NpcFrameworkScopeId,
                LedgerExisted = ledger != null,
                ExpectedRevision = ledger == null ? -1 : ledger.Revision
            };
            return true;
        }

        /// <summary>
        /// Applies one trusted native-evidence update and publishes a detached replacement row only after all
        /// actor, epoch, scope, revision, replay, and ledger invariants pass. Call on the store owner thread.
        /// </summary>
        public bool TryCommitNpcKnowledge(NpcKnowledgeWriteTicket ticket, NpcKnowledgeUpdate update)
        {
            if (ticket == null || update == null || ticket.Owner != this ||
                !ReferenceEquals(ticket.Epoch, bindingEpoch) ||
                !string.Equals(ticket.SaveEpochId, NpcFrameworkScopeId, StringComparison.Ordinal) ||
                !string.Equals(ticket.ActorId, update.ActorId, StringComparison.Ordinal) ||
                !IsKnownKnowledgeActor(ticket.ActorId) || state.NpcKnowledge == null) return false;

            int index;
            NpcKnowledgeState current = FindNpcKnowledgeLedger(ticket.ActorId, out index);
            if (ticket.LedgerExisted != (current != null) ||
                current != null && current.Revision != ticket.ExpectedRevision ||
                current == null && ticket.ExpectedRevision != -1) return false;

            NpcKnowledgeState baseState = current == null
                ? NpcKnowledgeLedger.Create(ticket.ActorId, ticket.SaveEpochId)
                : NpcKnowledgeLedger.CopyValidated(current);
            NpcKnowledgeState updated;
            if (!NpcKnowledgeLedger.TryApply(baseState, update, ticket.ActorId, ticket.SaveEpochId,
                baseState.Revision, out updated)) return false;

            var replacement = new List<NpcKnowledgeState>(state.NpcKnowledge.Count + (current == null ? 1 : 0));
            foreach (NpcKnowledgeState row in state.NpcKnowledge)
                replacement.Add(NpcKnowledgeLedger.CopyValidated(row));
            if (current == null) replacement.Add(updated);
            else replacement[index] = updated;
            state.NpcKnowledge = replacement;
            return true;
        }

        /// <summary>WorldState schema-38 migration and copy hook; invoke from WorldState.CopyValidated.</summary>
        private static List<NpcKnowledgeState> CopyNpcKnowledge(WorldState source, WorldState validated)
        {
            var result = new List<NpcKnowledgeState>();
            if (source.Version < 38) return result;
            Require(NpcKnowledgeValidation.ValidId(source.NpcFrameworkScopeId, 128),
                "Missing NPC framework knowledge scope.");
            Require(source.NpcKnowledge != null, "Missing NPC knowledge ledgers.");
            var actors = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcKnowledgeState saved in source.NpcKnowledge)
            {
                Require(saved != null, "Null NPC knowledge ledger.");
                NpcKnowledgeState copy;
                try { copy = NpcKnowledgeLedger.CopyValidated(saved); }
                catch (ArgumentException exception) { throw new ArgumentException("Invalid NPC knowledge ledger.", exception); }
                Require(string.Equals(copy.SaveEpochId, source.NpcFrameworkScopeId, StringComparison.Ordinal),
                    "NPC knowledge ledger belongs to a different framework scope.");
                Require(actors.Add(copy.ActorId), "Duplicate NPC knowledge actor ledger.");
                Require(IsKnownKnowledgeActor(validated, copy.ActorId),
                    "NPC knowledge ledger references an unknown actor.");
                result.Add(copy);
            }
            return result;
        }

        private bool IsKnownKnowledgeActor(string actorId)
        { return IsKnownKnowledgeActor(state, actorId); }

        private static bool IsKnownKnowledgeActor(WorldState world, string actorId)
        {
            bool alive;
            return KnownNpcIdentity(world, actorId, out alive);
        }

        private NpcKnowledgeState FindNpcKnowledgeLedger(string actorId, out int index)
        {
            index = -1;
            if (state.NpcKnowledge == null) return null;
            for (int i = 0; i < state.NpcKnowledge.Count; i++)
            {
                NpcKnowledgeState ledger = state.NpcKnowledge[i];
                if (ledger == null || !string.Equals(ledger.ActorId, actorId, StringComparison.Ordinal)) continue;
                if (index >= 0) return null;
                index = i;
            }
            return index < 0 ? null : state.NpcKnowledge[index];
        }
    }
}
