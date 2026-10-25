using System;
using System.Collections.Generic;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>One bounded retrieval turn: the model chooses at most one listed public topic ID.</summary>
    public static class NpcKnowledgeLookup
    {
        public const int MaximumInputCharacters = 1200;
        public const int MaximumHistoryCharacters = 1800;

        public static string Grammar(IList<NpcDisclosurePublicTopic> topics)
        {
            var grammar = new StringBuilder("root ::= \"NONE\"");
            int count = 0;
            if (topics != null)
                for (int i = 0; i < topics.Count && count < ConversationContextTools.MaximumKnowledgeTopics; i++)
                {
                    NpcDisclosurePublicTopic topic = topics[i];
                    if (topic == null || !NpcDisclosurePublicTopic.IsModelSelectableId(topic.PropositionId)) continue;
                    grammar.Append(" | \"READ_TOPIC|").Append(topic.PropositionId).Append('\"');
                    count++;
                }
            return grammar.Append('\n').ToString();
        }

        public static string Prompt(string playerText, string history, IList<NpcDisclosurePublicTopic> topics)
        {
            if (playerText == null || playerText.Length > MaximumInputCharacters) return null;
            var prompt = new StringBuilder();
            prompt.Append("Select at most one public fact topic the NPC may use to answer the player's current question. ")
                .Append("Use only the exact IDs listed below. Output NONE if none directly helps. This selects retrieval only; ")
                .Append("it does not choose what to disclose. Preserve each listed certainty label. Do not infer facts.\n");
            prompt.Append("Public topic IDs and certainty labels:\n");
            int count = 0;
            if (topics != null)
                for (int i = 0; i < topics.Count && count < ConversationContextTools.MaximumKnowledgeTopics; i++)
                {
                    NpcDisclosurePublicTopic topic = topics[i];
                    if (topic == null || !NpcDisclosurePublicTopic.IsModelSelectableId(topic.PropositionId)) continue;
                    prompt.Append(topic.PropositionId).Append(" | ").Append(topic.EpistemicKind).Append('\n');
                    count++;
                }
            prompt.Append("Current player text:\n").Append(playerText);
            if (!string.IsNullOrWhiteSpace(history))
                prompt.Append("\nRecent conversation history:\n").Append(Bound(history, MaximumHistoryCharacters));
            return prompt.ToString();
        }

        public static bool IsSameSnapshot(NpcKnowledgeSnapshot captured, NpcKnowledgeSnapshot current, string actorId)
        {
            return captured != null && current != null && !string.IsNullOrWhiteSpace(actorId) &&
                string.Equals(captured.ActorId, actorId, StringComparison.Ordinal) &&
                string.Equals(current.ActorId, actorId, StringComparison.Ordinal) &&
                string.Equals(captured.SaveEpochId, current.SaveEpochId, StringComparison.Ordinal) &&
                captured.Revision == current.Revision;
        }

        public static string ParseSelection(string output, IList<NpcDisclosurePublicTopic> topics)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;
            string value = output.Trim();
            if (string.Equals(value, "NONE", StringComparison.Ordinal)) return string.Empty;
            const string prefix = "READ_TOPIC|";
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string id = value.Substring(prefix.Length);
            if (!NpcDisclosurePublicTopic.IsModelSelectableId(id) || topics == null) return null;
            int matches = 0;
            for (int i = 0; i < topics.Count && i < ConversationContextTools.MaximumKnowledgeTopics; i++)
                if (topics[i] != null && string.Equals(topics[i].PropositionId, id, StringComparison.Ordinal)) matches++;
            return matches == 1 ? id : null;
        }

        private static string Bound(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length <= maximum) return value;
            return value.Substring(value.Length - maximum, maximum);
        }
    }
}
