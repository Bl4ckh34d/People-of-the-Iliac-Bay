using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Finite source cursor for one managed person's canonical player turns.</summary>
    [Serializable]
    public sealed class NpcConversationCommandCursor
    {
        public string PersonId;
        public long LastIssuedOrdinal;
        public long LastAcceptedOrdinal;

        public static NpcConversationCommandCursor CopyValidated(NpcConversationCommandCursor source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.PersonId) || source.PersonId.Length > 256 ||
                source.LastIssuedOrdinal < 0 || source.LastIssuedOrdinal == long.MaxValue ||
                source.LastAcceptedOrdinal < 0 || source.LastAcceptedOrdinal > source.LastIssuedOrdinal)
                throw new ArgumentException("Invalid conversation command cursor.");
            return new NpcConversationCommandCursor { PersonId = source.PersonId,
                LastIssuedOrdinal = source.LastIssuedOrdinal, LastAcceptedOrdinal = source.LastAcceptedOrdinal };
        }
    }

    public enum NpcConversationCommandKind { Task, Action, Departure }

    /// <summary>Opaque live Talk session authority. Only the canonical conversation entry retains it.</summary>
    public sealed class NpcConversationCommandSession
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal string ScopeId;
        internal string PersonId;
        internal bool Closed;
        internal long LastCapturedRequestRevision = -1;
        internal NpcConversationCommandTurn CurrentTurn;
        public string SessionId { get; internal set; }
    }

    /// <summary>Live, save-binding-bound source identity captured once at canonical player submission.</summary>
    public sealed class NpcConversationCommandTurn
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal string ScopeId;
        internal string PersonId;
        internal string Digest;
        internal bool Closed;
        internal NpcConversationCommandCapability Issued;
        internal NpcConversationCommandSession Session;
        public long Ordinal { get; internal set; }
        public string TurnDigest { get { return Digest; } }
    }

    /// <summary>One typed, exact-candidate admission capability; never serialized.</summary>
    public sealed class NpcConversationCommandCapability
    {
        internal NpcConversationCommandTurn Turn;
        internal NpcConversationCommandKind Kind;
        internal string CandidateDigest;
        internal bool Accepted;
        public long Ordinal { get { return Turn == null ? 0 : Turn.Ordinal; } }
    }

    public sealed partial class WorldStateStore
    {
        private object liveConversationCommandEpoch;
        private Dictionary<string, NpcConversationCommandSession> liveConversationCommandSessions;

        private void RefreshConversationCommandEpoch()
        {
            if (ReferenceEquals(liveConversationCommandEpoch, bindingEpoch) && liveConversationCommandSessions != null) return;
            liveConversationCommandEpoch = bindingEpoch;
            liveConversationCommandSessions = new Dictionary<string, NpcConversationCommandSession>(StringComparer.Ordinal);
        }

        /// <summary>Call only when a real Talk session opens, never from a model or willingness callback.</summary>
        public bool TryBeginNpcConversationCommandSession(string personId, string sessionId,
            out NpcConversationCommandSession session)
        {
            session = null;
            if (!IsKnownNpcExperienceIdentity(personId) || string.IsNullOrWhiteSpace(sessionId) ||
                sessionId.Length > 128 || string.IsNullOrWhiteSpace(NpcFrameworkScopeId)) return false;
            RefreshConversationCommandEpoch();
            NpcConversationCommandSession previous;
            if (liveConversationCommandSessions.TryGetValue(personId, out previous))
            {
                if (previous.SessionId == sessionId) return false;
                previous.Closed = true;
                if (previous.CurrentTurn != null) previous.CurrentTurn.Closed = true;
            }
            session = new NpcConversationCommandSession { Owner = this, Epoch = bindingEpoch,
                ScopeId = NpcFrameworkScopeId, PersonId = personId, SessionId = sessionId };
            liveConversationCommandSessions[personId] = session;
            return true;
        }

        public bool TryCloseNpcConversationCommandSession(NpcConversationCommandSession session)
        {
            if (!CurrentConversationCommandSession(session)) return false;
            session.Closed = true;
            if (session.CurrentTurn != null) session.CurrentTurn.Closed = true;
            liveConversationCommandSessions.Remove(session.PersonId);
            return true;
        }

        private bool CurrentConversationCommandSession(NpcConversationCommandSession session)
        {
            RefreshConversationCommandEpoch();
            NpcConversationCommandSession current;
            return session != null && session.Owner == this && !session.Closed &&
                ReferenceEquals(session.Epoch, bindingEpoch) &&
                string.Equals(session.ScopeId, NpcFrameworkScopeId, StringComparison.Ordinal) &&
                liveConversationCommandSessions.TryGetValue(session.PersonId, out current) &&
                ReferenceEquals(current, session);
        }

        private static List<NpcConversationCommandCursor> CopyConversationCursors(IList<NpcConversationCommandCursor> source)
        {
            if (source == null) throw new ArgumentException("Missing conversation command cursors.");
            var result = new List<NpcConversationCommandCursor>(source.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in source)
            {
                var copy = NpcConversationCommandCursor.CopyValidated(row);
                if (!seen.Add(copy.PersonId)) throw new ArgumentException("Duplicate conversation command cursor.");
                result.Add(copy);
            }
            return result;
        }

        /// <summary>Called once when a new typed player message gets its canonical request binding.</summary>
        public bool TryCaptureNpcConversationCommandTurn(NpcConversationCommandSession session,
            ConversationContextBinding binding, string canonicalPlayerText, out NpcConversationCommandTurn turn)
        {
            turn = null;
            if (!CurrentConversationCommandSession(session) || binding == null ||
                binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                binding.PersonId != session.PersonId || binding.SessionId != session.SessionId ||
                binding.RequestRevision <= session.LastCapturedRequestRevision ||
                string.IsNullOrWhiteSpace(canonicalPlayerText) || canonicalPlayerText.Length > 8192 ||
                string.IsNullOrWhiteSpace(NpcFrameworkScopeId)) return false;
            List<NpcConversationCommandCursor> next;
            string digest;
            try
            {
                next = CopyConversationCursors(state.NpcConversationCommandCursors);
            }
            catch (ArgumentException) { return false; }
            int index = next.FindIndex(x => x.PersonId == binding.PersonId);
            var cursor = index < 0 ? new NpcConversationCommandCursor { PersonId = binding.PersonId } : next[index];
            if (cursor.LastIssuedOrdinal >= long.MaxValue - 1) return false;
            cursor.LastIssuedOrdinal++;
            try { digest = NpcConversationCommandDigest.Turn(binding, canonicalPlayerText, cursor.LastIssuedOrdinal); }
            catch (ArgumentException) { return false; }
            if (index < 0) next.Add(cursor);
            if (session.CurrentTurn != null) session.CurrentTurn.Closed = true;
            turn = new NpcConversationCommandTurn { Owner = this, Epoch = bindingEpoch,
                ScopeId = NpcFrameworkScopeId, PersonId = binding.PersonId,
                Ordinal = cursor.LastIssuedOrdinal, Digest = digest, Session = session };
            state.NpcConversationCommandCursors = next;
            session.CurrentTurn = turn;
            session.LastCapturedRequestRevision = binding.RequestRevision;
            return true;
        }

        /// <summary>Closes a turn at Talk close or explicit cancellation; old callbacks stay invalid.</summary>
        public bool TryCloseNpcConversationCommandTurn(NpcConversationCommandTurn turn)
        {
            if (!CurrentConversationCommandTurn(turn)) return false;
            turn.Closed = true;
            turn.Session.CurrentTurn = null;
            return true;
        }

        private bool CurrentConversationCommandTurn(NpcConversationCommandTurn turn)
        {
            RefreshConversationCommandEpoch();
            return turn != null && turn.Owner == this && !turn.Closed &&
                ReferenceEquals(turn.Epoch, bindingEpoch) &&
                string.Equals(turn.ScopeId, NpcFrameworkScopeId, StringComparison.Ordinal) &&
                CurrentConversationCommandSession(turn.Session) &&
                ReferenceEquals(turn.Session.CurrentTurn, turn);
        }

        /// <summary>Returns a detached, source-stamped task. Issuance and admission remain separate.</summary>
        public bool TryPrepareNpcConversationTask(NpcConversationCommandTurn turn, NpcTaskPlan proposed,
            out NpcTaskPlan prepared)
        {
            prepared = null;
            if (!CurrentConversationCommandTurn(turn) || proposed == null || proposed.ResidentId != turn.PersonId ||
                proposed.Admission != null) return false;
            try
            {
                NpcTaskContract.Validate(proposed);
                prepared = NpcTaskContract.Copy(proposed);
                prepared.Id = NpcOperationAdmission.ConversationTaskId(NpcFrameworkScopeId, turn.PersonId, turn.Ordinal);
                prepared.Admission = NpcOperationAdmission.FromConversation(turn);
                NpcTaskContract.Validate(prepared);
                return true;
            }
            catch (ArgumentException) { prepared = null; return false; }
        }

        /// <summary>Returns a detached, source-stamped action after native details are ready, before route construction.</summary>
        public bool TryPrepareNpcConversationAction(NpcConversationCommandTurn turn, NpcActionJob proposed,
            bool departure, out NpcActionJob prepared)
        {
            prepared = null;
            if (!CurrentConversationCommandTurn(turn) || proposed == null || proposed.ResidentId != turn.PersonId ||
                proposed.Admission != null || departure && proposed.Kind != NpcActionKind.ChangeSchedule) return false;
            try
            {
                NpcActionExecutor.Validate(proposed);
                prepared = NpcActionExecutor.Copy(proposed);
                prepared.Id = departure
                    ? NpcOperationAdmission.ConversationDepartureId(NpcFrameworkScopeId, turn.PersonId, turn.Ordinal)
                    : NpcOperationAdmission.ConversationActionId(NpcFrameworkScopeId, turn.PersonId, turn.Ordinal);
                prepared.Admission = NpcOperationAdmission.FromConversation(turn);
                NpcActionExecutor.Validate(prepared);
                return true;
            }
            catch (ArgumentException) { prepared = null; return false; }
        }

        public bool TryIssueNpcConversationTask(NpcConversationCommandTurn turn, NpcTaskPlan proposed,
            out NpcConversationCommandCapability capability)
        { return TryIssueNpcConversationCommand(turn, NpcConversationCommandKind.Task, proposed, null, capability: out capability); }

        public bool TryIssueNpcConversationAction(NpcConversationCommandTurn turn, NpcActionJob proposed,
            out NpcConversationCommandCapability capability)
        { return TryIssueNpcConversationCommand(turn, NpcConversationCommandKind.Action, proposed, null, capability: out capability); }

        public bool TryIssueNpcConversationDeparture(NpcConversationCommandTurn turn, NpcActionJob proposed,
            VisitDeparture departure, out NpcConversationCommandCapability capability)
        { return TryIssueNpcConversationCommand(turn, NpcConversationCommandKind.Departure, proposed, departure,
            capability: out capability); }

        private bool TryIssueNpcConversationCommand(NpcConversationCommandTurn turn, NpcConversationCommandKind kind,
            object proposed, VisitDeparture departure, out NpcConversationCommandCapability capability)
        {
            capability = null;
            if (!CurrentConversationCommandTurn(turn) || turn.Issued != null ||
                !CandidatePersonMatches(turn.PersonId, kind, proposed, departure) ||
                !MatchesConversationAdmission(turn, kind, proposed)) return false;
            NpcConversationCommandCursor cursor;
            try { cursor = FindConversationCursor(turn.PersonId); }
            catch (ArgumentException) { return false; }
            if (cursor == null || cursor.LastIssuedOrdinal != turn.Ordinal || cursor.LastAcceptedOrdinal >= turn.Ordinal)
                return false;
            string candidateDigest;
            try { candidateDigest = NpcConversationCommandDigest.Candidate(turn.Digest, kind, proposed, departure); }
            catch (ArgumentException) { return false; }
            capability = new NpcConversationCommandCapability { Turn = turn, Kind = kind,
                CandidateDigest = candidateDigest };
            turn.Issued = capability;
            return true;
        }

        private NpcConversationCommandCursor FindConversationCursor(string personId)
        {
            var rows = state.NpcConversationCommandCursors;
            if (rows == null) throw new ArgumentException("Missing conversation command cursors.");
            NpcConversationCommandCursor found = null;
            foreach (var row in rows)
                if (row != null && row.PersonId == personId)
                {
                    if (found != null) throw new ArgumentException("Duplicate conversation command cursor.");
                    found = NpcConversationCommandCursor.CopyValidated(row);
                }
            return found;
        }

        private static bool CandidatePersonMatches(string personId, NpcConversationCommandKind kind,
            object proposed, VisitDeparture departure)
        {
            try
            {
                if (kind == NpcConversationCommandKind.Task)
                {
                    var task = proposed as NpcTaskPlan;
                    if (task == null || task.ResidentId != personId || departure != null) return false;
                    NpcTaskContract.Validate(task);
                    return true;
                }
                var action = proposed as NpcActionJob;
                if (action == null || action.ResidentId != personId) return false;
                NpcActionExecutor.Validate(action);
                if (kind == NpcConversationCommandKind.Action) return departure == null;
                if (kind != NpcConversationCommandKind.Departure || departure == null ||
                    departure.ResidentId != personId || action.Kind != NpcActionKind.ChangeSchedule) return false;
                VisitDeparture.Copy(departure);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private bool MatchesConversationAdmission(NpcConversationCommandTurn turn,
            NpcConversationCommandKind kind, object proposed)
        {
            NpcOperationAdmission admission;
            string id;
            if (kind == NpcConversationCommandKind.Task)
            {
                var task = proposed as NpcTaskPlan;
                if (task == null) return false;
                admission = task.Admission;
                id = task.Id;
            }
            else
            {
                var action = proposed as NpcActionJob;
                if (action == null) return false;
                admission = action.Admission;
                id = action.Id;
            }
            if (admission == null) return false;
            string expectedId = kind == NpcConversationCommandKind.Task
                ? NpcOperationAdmission.ConversationTaskId(NpcFrameworkScopeId, turn.PersonId, turn.Ordinal)
                : kind == NpcConversationCommandKind.Departure
                    ? NpcOperationAdmission.ConversationDepartureId(NpcFrameworkScopeId, turn.PersonId, turn.Ordinal)
                    : NpcOperationAdmission.ConversationActionId(NpcFrameworkScopeId, turn.PersonId, turn.Ordinal);
            return id == expectedId &&
                NpcOperationAdmission.Same(admission, NpcOperationAdmission.FromConversation(turn));
        }

        private bool PrepareConversationCommandAcceptance(NpcConversationCommandCapability capability,
            NpcConversationCommandKind kind, object proposed, VisitDeparture departure,
            out List<NpcConversationCommandCursor> next)
        {
            next = null;
            if (capability == null || capability.Accepted || capability.Turn == null ||
                !CurrentConversationCommandTurn(capability.Turn) || capability.Kind != kind ||
                !ReferenceEquals(capability.Turn.Issued, capability) ||
                !CandidatePersonMatches(capability.Turn.PersonId, kind, proposed, departure) ||
                !MatchesConversationAdmission(capability.Turn, kind, proposed)) return false;
            string candidateDigest;
            try
            {
                candidateDigest = NpcConversationCommandDigest.Candidate(capability.Turn.Digest,
                    kind, proposed, departure);
                next = CopyConversationCursors(state.NpcConversationCommandCursors);
            }
            catch (ArgumentException) { next = null; return false; }
            if (!string.Equals(candidateDigest, capability.CandidateDigest, StringComparison.Ordinal))
            { next = null; return false; }
            int index = next.FindIndex(x => x.PersonId == capability.Turn.PersonId);
            if (index < 0 || next[index].LastIssuedOrdinal != capability.Turn.Ordinal ||
                next[index].LastAcceptedOrdinal >= capability.Turn.Ordinal)
            { next = null; return false; }
            next[index].LastAcceptedOrdinal = capability.Turn.Ordinal;
            return true;
        }

        private void PublishAcceptedConversationCommand(NpcConversationCommandCapability capability,
            List<NpcConversationCommandCursor> prepared)
        {
            state.NpcConversationCommandCursors = prepared;
            capability.Accepted = true;
            capability.Turn.Closed = true;
            capability.Turn.Session.CurrentTurn = null;
        }

        public bool TryAcceptNpcTaskFromConversation(NpcConversationCommandCapability capability,
            NpcTaskPlan proposed, bool consent, out string reason)
        {
            reason = "The player command is no longer current.";
            List<NpcConversationCommandCursor> prepared;
            if (!PrepareConversationCommandAcceptance(capability, NpcConversationCommandKind.Task,
                proposed, null, out prepared)) return false;
            if (proposed.Admission == null ||
                !TryAcceptNpcTaskClassified(proposed, consent,
                    NpcOperationAdmission.FromConversation(capability.Turn), out reason)) return false;
            PublishAcceptedConversationCommand(capability, prepared);
            return true;
        }

        public bool TryAcceptNpcActionFromConversation(NpcConversationCommandCapability capability,
            NpcActionJob proposed, bool adapterAvailable, bool consent, bool scheduleCompatible, out string reason)
        {
            reason = "The player command is no longer current.";
            List<NpcConversationCommandCursor> prepared;
            if (!PrepareConversationCommandAcceptance(capability, NpcConversationCommandKind.Action,
                proposed, null, out prepared)) return false;
            if (proposed.Admission == null ||
                !TryAcceptNpcActionClassified(proposed, adapterAvailable, consent, scheduleCompatible,
                    NpcOperationAdmission.FromConversation(capability.Turn), out reason)) return false;
            PublishAcceptedConversationCommand(capability, prepared);
            return true;
        }

        public bool TryAcceptNpcDepartureFromConversation(NpcConversationCommandCapability capability,
            NpcActionJob proposed, VisitDeparture departure, bool consent, out string reason)
        {
            reason = "The player command is no longer current.";
            List<NpcConversationCommandCursor> prepared;
            if (!PrepareConversationCommandAcceptance(capability, NpcConversationCommandKind.Departure,
                proposed, departure, out prepared)) return false;
            if (proposed.Admission == null ||
                !TryAcceptNpcDepartureClassified(proposed, departure, consent,
                    NpcOperationAdmission.FromConversation(capability.Turn), out reason)) return false;
            PublishAcceptedConversationCommand(capability, prepared);
            return true;
        }
    }

    internal static class NpcConversationCommandDigest
    {
        internal static string Turn(ConversationContextBinding binding, string text, long ordinal)
        {
            return Hash("turn", binding.ActorCategory, binding.IdentityKind, binding.PersonId,
                binding.EphemeralSpeakerToken, binding.SessionId, binding.RequestRevision,
                binding.SaveEpoch, binding.ArchiveId, binding.ArchiveHead, binding.SceneId,
                binding.SceneRevision, text, ordinal);
        }

        internal static string Candidate(string turnDigest, NpcConversationCommandKind kind,
            object proposed, VisitDeparture departure)
        { return Hash("candidate", turnDigest, kind, proposed, departure); }

        private static string Hash(params object[] values)
        {
            var builder = new StringBuilder();
            var stack = new HashSet<object>();
            int nodes = 0;
            foreach (var value in values) Append(builder, value, stack, 0, ref nodes);
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))
                    .Replace("-", "").ToLowerInvariant();
        }

        private static void Append(StringBuilder builder, object value, HashSet<object> stack,
            int depth, ref int nodes)
        {
            if (++nodes > 4096 || depth > 24 || builder.Length > 262144)
                throw new ArgumentException("Conversation candidate exceeds digest bounds.");
            if (value == null) { builder.Append("N;"); return; }
            Type type = value.GetType();
            builder.Append('T').Append(type.FullName.Length).Append(':').Append(type.FullName).Append(';');
            if (value is string)
            {
                string text = (string)value;
                builder.Append('S').Append(text.Length).Append(':').Append(text).Append(';');
                return;
            }
            if (type.IsPrimitive || type.IsEnum || value is decimal || value is DateTime || value is Guid)
            {
                string scalar = type.IsEnum ? Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) :
                    value is double ? ((double)value).ToString("R", CultureInfo.InvariantCulture) :
                    value is float ? ((float)value).ToString("R", CultureInfo.InvariantCulture) :
                    value is DateTime ? ((DateTime)value).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) :
                    Convert.ToString(value, CultureInfo.InvariantCulture);
                builder.Append('V').Append(scalar.Length).Append(':').Append(scalar).Append(';');
                return;
            }
            if (!stack.Add(value)) throw new ArgumentException("Cyclic conversation candidate.");
            try
            {
                var list = value as IList;
                if (list != null)
                {
                    builder.Append('L').Append(list.Count).Append(';');
                    foreach (var item in list) Append(builder, item, stack, depth + 1, ref nodes);
                    return;
                }
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                if (fields.Length == 0) throw new ArgumentException("Candidate has unbound fields.");
                Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                builder.Append('F').Append(fields.Length).Append(';');
                foreach (var field in fields)
                {
                    builder.Append(field.Name.Length).Append(':').Append(field.Name).Append(';');
                    Append(builder, field.GetValue(value), stack, depth + 1, ref nodes);
                }
            }
            finally { stack.Remove(value); }
        }
    }
}
