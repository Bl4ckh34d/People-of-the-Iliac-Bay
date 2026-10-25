using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Immutable display-only input for wording a verified saved action or task receipt.</summary>
    public sealed class ConversationActionRenderRequest
    {
        private const int MaximumOriginalTextCharacters = 8192;
        private const int MaximumHistoryCharacters = 16384;
        private const int VoiceContextCharacters = 2400;

        public ConversationContextSnapshot Snapshot { get; }
        public string OriginalPlayerText { get; }
        public ConversationActionReply Receipt { get; }
        public string History { get; }

        public ConversationActionRenderRequest(ConversationContextSnapshot snapshot, string exactOriginalPlayerText,
            ConversationActionReply receipt, string attributedHistory)
        {
            if (snapshot == null || snapshot.Binding == null || string.IsNullOrWhiteSpace(exactOriginalPlayerText) ||
                exactOriginalPlayerText.Length > MaximumOriginalTextCharacters || !ValidUtf16(exactOriginalPlayerText) ||
                attributedHistory == null || attributedHistory.Length > MaximumHistoryCharacters || !ValidUtf16(attributedHistory) ||
                !ValidReceiptFor(snapshot.Binding, receipt))
                throw new ArgumentException("Invalid receipt-grounded action wording request.");

            Snapshot = snapshot;
            OriginalPlayerText = exactOriginalPlayerText;
            Receipt = receipt;
            History = attributedHistory;
        }

        public DialogueRequest ToDialogueRequest(string avoidDuplicateReply = null,
            bool moodEnabled = false, int moodValue = 0)
        {
            if (avoidDuplicateReply != null && (avoidDuplicateReply.Length > 800 || !ValidUtf16(avoidDuplicateReply)))
                throw new ArgumentException("Previous reply text must be valid UTF-16 and at most 800 characters.", nameof(avoidDuplicateReply));
            string npcName;
            string race;
            string gender;
            string role;
            if (!Snapshot.Name.TryGetVerified(out npcName)) npcName = "unknown";
            if (!Snapshot.PersonalState.Race.TryGetVerified(out race)) race = "unknown";
            if (!Snapshot.PersonalState.Gender.TryGetVerified(out gender)) gender = "unknown";
            ConversationDialogueRole verifiedRole;
            role = Snapshot.Role.TryGetVerified(out verifiedRole) ? verifiedRole.ToString() : "role not established";

            return new DialogueRequest(OriginalPlayerText, Receipt.NpcText, npcName, race, gender, role,
                Snapshot.EffectiveDisposition, socialOnly: false,
                conversationReaction: ConversationContextFormatter.ForActionVoice(Snapshot, VoiceContextCharacters),
                history: History, knowledgeResult: ReceiptFacts(Receipt), actionReply: Receipt,
                avoidDuplicateReply: avoidDuplicateReply, moodEnabled: moodEnabled, moodValue: moodValue);
        }

        private static bool ValidReceiptFor(ConversationContextBinding binding, ConversationActionReply receipt)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.PersonId) || receipt == null ||
                string.IsNullOrWhiteSpace(receipt.NpcText) || !Enum.IsDefined(typeof(ConversationActionReplyStatus), receipt.Status) ||
                receipt.CompletedSteps < 0 || receipt.TotalSteps < 0 ||
                (receipt.ActionKind.HasValue && !Enum.IsDefined(typeof(NpcActionKind), receipt.ActionKind.Value)))
                return false;

            if (receipt.Source == ConversationActionReplySource.None)
                return (receipt.Status == ConversationActionReplyStatus.NoChange || receipt.Status == ConversationActionReplyStatus.Rejected) &&
                    string.IsNullOrEmpty(receipt.SourceId) && string.IsNullOrEmpty(receipt.PersonId) &&
                    string.IsNullOrEmpty(receipt.ReceiptKey) && receipt.CompletedSteps == 0 && receipt.TotalSteps == 0 && receipt.EffectReceiptId == null;

            return (receipt.Source == ConversationActionReplySource.Action || receipt.Source == ConversationActionReplySource.Task) &&
                !string.IsNullOrWhiteSpace(receipt.SourceId) && !string.IsNullOrWhiteSpace(receipt.ReceiptKey) &&
                !string.IsNullOrWhiteSpace(receipt.PersonId) && string.Equals(receipt.PersonId, binding.PersonId, StringComparison.Ordinal) &&
                receipt.TotalSteps >= 1 && receipt.CompletedSteps <= receipt.TotalSteps;
        }

        private static string ReceiptFacts(ConversationActionReply receipt)
        {
            var facts = "verified_status=" + receipt.Status + "; source=" + receipt.Source + ";";
            facts += " agreed=" + Agreement(receipt) + "; execution_started=" + ExecutionStarted(receipt) +
                "; starts_after_chat=" + (receipt.Status == ConversationActionReplyStatus.AcceptedPending ? "true" : "false") + ";";
            if (receipt.ActionKind.HasValue) facts += " action_kind=" + receipt.ActionKind.Value + ";";
            if (receipt.Source == ConversationActionReplySource.Task)
                facts += " completed_steps=" + receipt.CompletedSteps.ToString(CultureInfo.InvariantCulture) +
                    "; total_steps=" + receipt.TotalSteps.ToString(CultureInfo.InvariantCulture) + ";";
            facts += " effect_receipt_present=" + (receipt.EffectReceiptId == null ? "false" : "true") + ";";
            facts += " cancellation_attempt_failed=" + (receipt.CancellationAttemptFailed ? "true" : "false") + ";";
            if (receipt.UnresolvedReason != null)
                facts += " unresolved_reason_json=\"" + LocalDialogueCompletion.EscapeJson(receipt.UnresolvedReason) + "\";";
            return facts;
        }

        private static string Agreement(ConversationActionReply receipt)
        {
            switch (receipt.Status)
            {
                case ConversationActionReplyStatus.AcceptedPending:
                case ConversationActionReplyStatus.InProgress:
                case ConversationActionReplyStatus.Completed:
                case ConversationActionReplyStatus.Failed:
                case ConversationActionReplyStatus.Cancelled: return "true";
                case ConversationActionReplyStatus.NoChange:
                case ConversationActionReplyStatus.Rejected: return "false";
                default: return "unknown";
            }
        }

        private static string ExecutionStarted(ConversationActionReply receipt)
        {
            switch (receipt.Status)
            {
                case ConversationActionReplyStatus.AcceptedPending:
                case ConversationActionReplyStatus.NoChange:
                case ConversationActionReplyStatus.Rejected: return "false";
                case ConversationActionReplyStatus.InProgress:
                case ConversationActionReplyStatus.Completed: return "true";
                default: return "unknown";
            }
        }

        private static bool ValidUtf16(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsHighSurrogate(current))
                {
                    if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
                }
                else if (char.IsLowSurrogate(current)) return false;
            }
            return true;
        }
    }
}
