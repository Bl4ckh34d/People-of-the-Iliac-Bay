using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>An exact player utterance with its native conversation ownership.</summary>
    public sealed class NpcGoalClarificationTurn
    {
        public ConversationContextBinding Binding { get; }
        public ConversationTextSpeaker Speaker => ConversationTextSpeaker.Player;
        public string Text { get; }

        internal NpcGoalClarificationTurn(ConversationContextBinding binding, string text)
        { Binding = binding; Text = text; }
    }

    /// <summary>
    /// Bounded, non-executable evidence for a future complete-plan extraction. This contract
    /// never interprets utterances or accepts action effects. Callers must classify cancellation
    /// semantically, call Cancel, and separately validate the complete plan before execution.
    /// </summary>
    public sealed class NpcGoalPendingClarification
    {
        public const int MaximumTurns = 8;
        public const int MaximumTotalLength = NpcGoalChat.MaximumTurnLength * 2;
        private readonly List<NpcGoalClarificationTurn> turns = new List<NpcGoalClarificationTurn>();
        private int totalLength;

        public string OriginalText => turns[0].Text;
        public ConversationContextBinding CurrentBinding => turns[turns.Count - 1].Binding;
        public bool IsActive { get; private set; } = true;
        public int ProposalCount { get; private set; }
        // Return a snapshot: later answers cannot mutate evidence already dispatched to a model.
        public IReadOnlyList<NpcGoalClarificationTurn> Turns =>
            new ReadOnlyCollection<NpcGoalClarificationTurn>(new List<NpcGoalClarificationTurn>(turns));

        public NpcGoalPendingClarification(ConversationContextBinding binding, string originalText)
        {
            if (binding == null || !ValidText(originalText))
                throw new ArgumentException("Pending clarification requires an exact bounded player request and binding.");
            turns.Add(new NpcGoalClarificationTurn(binding, originalText));
            totalLength = originalText.Length;
        }

        public bool TryAppend(ConversationContextBinding binding, string exactPlayerText)
        {
            if (!ValidateOwner(binding) || binding.RequestRevision <= CurrentBinding.RequestRevision ||
                !ValidText(exactPlayerText) || turns.Count >= MaximumTurns ||
                exactPlayerText.Length > MaximumTotalLength - totalLength) return false;
            turns.Add(new NpcGoalClarificationTurn(binding, exactPlayerText));
            totalLength += exactPlayerText.Length;
            // A player clarification is new evidence, not a model-only repair attempt.
            ProposalCount = 0;
            return true;
        }

        public bool TryBeginProposal(ConversationContextBinding binding)
        {
            if (!IsCurrent(binding) || ProposalCount >= NpcGoalParameterAttempts.MaximumAttempts) return false;
            ProposalCount++;
            return true;
        }

        public bool IsCurrent(ConversationContextBinding binding) =>
            ValidateOwner(binding) && CurrentBinding.HasSameRequestBinding(binding);

        public void Cancel() { IsActive = false; }

        /// <summary>
        /// Serializes evidence only, with no reconstructed request or inferred values. The future
        /// extractor must interpret all turns together and validate a complete plan before effects.
        /// This data is deliberately separate from model instructions and executable proposals.
        /// </summary>
        public string ToExtractionData()
        {
            var data = new StringBuilder("{\"playerTurns\":[");
            for (int i = 0; i < turns.Count; i++)
            {
                if (i != 0) data.Append(',');
                var turn = turns[i];
                data.Append("{\"role\":\"").Append(i == 0 ? "original-request" : "clarification")
                    .Append("\",\"speaker\":\"player\",\"recipientPersonId\":").Append(Quote(turn.Binding.PersonId))
                    .Append(",\"recipientEphemeralToken\":").Append(Quote(turn.Binding.EphemeralSpeakerToken))
                    .Append(",\"requestRevision\":").Append(turn.Binding.RequestRevision.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"text\":").Append(Quote(turn.Text)).Append('}');
            }
            return data.Append("]}").ToString();
        }

        private static string Quote(string text) => text == null ? "null" :
            "\"" + LocalDialogueCompletion.EscapeJson(text) + "\"";

        /// <summary>Call on native lifecycle changes even when no clarification arrives.</summary>
        public bool ValidateOwner(ConversationContextBinding binding)
        {
            if (!IsActive) return false;
            var original = turns[0].Binding;
            if (binding == null || original.ActorCategory != binding.ActorCategory ||
                original.IdentityKind != binding.IdentityKind || original.SaveEpoch != binding.SaveEpoch ||
                original.SceneRevision != binding.SceneRevision ||
                !Same(original.PersonId, binding.PersonId) ||
                !Same(original.EphemeralSpeakerToken, binding.EphemeralSpeakerToken) ||
                !Same(original.SessionId, binding.SessionId) || !Same(original.ArchiveId, binding.ArchiveId) ||
                !Same(original.SceneId, binding.SceneId))
            {
                Cancel();
                return false;
            }
            return true;
        }

        private static bool ValidText(string text) =>
            !string.IsNullOrWhiteSpace(text) && text.Length <= NpcGoalChat.MaximumTurnLength;
        private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
    }
}
