using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>The persisted source class for a newly admitted task or action. Null denotes a legacy row.</summary>
    public enum NpcOperationAdmissionKind { Conversation = 1, HealthCare = 2, TaskStep = 3 }

    [Serializable]
    public sealed class NpcOperationAdmission
    {
        public NpcOperationAdmissionKind Kind;
        public string SourceId;
        public long SourceRevision;
        public int StepIndex;
        public string SourceDigest;

        public static NpcOperationAdmission CopyValidated(NpcOperationAdmission source)
        {
            if (source == null || !ValidId(source.SourceId, 256) || source.SourceRevision < 1 ||
                source.SourceRevision == long.MaxValue || source.StepIndex < 0 ||
                (source.Kind != NpcOperationAdmissionKind.Conversation &&
                 source.Kind != NpcOperationAdmissionKind.HealthCare &&
                 source.Kind != NpcOperationAdmissionKind.TaskStep) ||
                (source.Kind == NpcOperationAdmissionKind.Conversation &&
                    (source.StepIndex != 0 || !LowerHexDigest(source.SourceDigest))) ||
                (source.Kind == NpcOperationAdmissionKind.HealthCare &&
                    (source.StepIndex != 0 || source.SourceDigest != null || !NpcHealthCare.ValidEpisodeId(source.SourceId))) ||
                (source.Kind == NpcOperationAdmissionKind.TaskStep &&
                    (source.SourceDigest != null || source.StepIndex >= NpcTaskContract.MaximumSteps)))
                throw new ArgumentException("Invalid NPC operation admission source.", nameof(source));
            return new NpcOperationAdmission { Kind = source.Kind, SourceId = source.SourceId,
                SourceRevision = source.SourceRevision, StepIndex = source.StepIndex,
                SourceDigest = source.SourceDigest };
        }

        public static string ConversationTaskId(string scopeId, string personId, long ordinal)
        { return ConversationId("task", scopeId, personId, ordinal); }

        public static string ConversationActionId(string scopeId, string personId, long ordinal)
        { return ConversationId("action", scopeId, personId, ordinal); }

        public static string ConversationDepartureId(string scopeId, string personId, long ordinal)
        { return ConversationId("departure", scopeId, personId, ordinal); }

        internal static NpcOperationAdmission FromConversation(NpcConversationCommandTurn turn)
        {
            if (turn == null || !ValidId(turn.PersonId, 256) || turn.Ordinal < 1 || !LowerHexDigest(turn.Digest))
                throw new ArgumentException("Invalid live conversation source.", nameof(turn));
            return new NpcOperationAdmission { Kind = NpcOperationAdmissionKind.Conversation,
                SourceId = turn.PersonId, SourceRevision = turn.Ordinal, SourceDigest = turn.Digest };
        }

        internal static NpcOperationAdmission FromHealthCare(NpcHealthCareRecord care, long expectedRevision)
        {
            if (care == null || !NpcHealthCare.ValidEpisodeId(care.Id) || expectedRevision < 1 ||
                expectedRevision == long.MaxValue || care.Revision != expectedRevision)
                throw new ArgumentException("Invalid live health-care source.", nameof(care));
            return new NpcOperationAdmission { Kind = NpcOperationAdmissionKind.HealthCare,
                SourceId = care.Id, SourceRevision = expectedRevision };
        }

        internal static NpcOperationAdmission FromTaskStep(NpcTaskPlan task)
        {
            if (task == null || !ValidId(task.Id, 256) || task.Revision < 1 ||
                task.Revision == long.MaxValue || task.CurrentStepIndex < 0 ||
                task.CurrentStepIndex >= NpcTaskContract.MaximumSteps)
                throw new ArgumentException("Invalid live task-step source.", nameof(task));
            return new NpcOperationAdmission { Kind = NpcOperationAdmissionKind.TaskStep,
                SourceId = task.Id, SourceRevision = task.Revision, StepIndex = task.CurrentStepIndex };
        }

        internal static bool Same(NpcOperationAdmission left, NpcOperationAdmission right)
        {
            return left != null && right != null && left.Kind == right.Kind &&
                string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal) &&
                left.SourceRevision == right.SourceRevision && left.StepIndex == right.StepIndex &&
                string.Equals(left.SourceDigest, right.SourceDigest, StringComparison.Ordinal);
        }

        private static string ConversationId(string kind, string scopeId, string personId, long ordinal)
        {
            Guid scope;
            if (!Guid.TryParseExact(scopeId, "N", out scope) || !ValidId(personId, 256) ||
                ordinal < 1 || ordinal == long.MaxValue)
                throw new ArgumentException("Invalid conversation operation source.");
            string value = "npc-operation:v2\n" + scopeId + "\n" + personId + "\n" +
                ordinal.ToString(CultureInfo.InvariantCulture) + "\n" + kind;
            using (SHA256 sha = SHA256.Create())
                return kind + ":conversation:v2:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", "").ToLowerInvariant();
        }

        private static bool LowerHexDigest(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static bool ValidId(string value, int maximumLength)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength; }
    }
}
