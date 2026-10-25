using System;
using System.Collections.Generic;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public static class DialogueHistoryWindow
    {
        public const string UnavailableResponseLabel = "NPC response unavailable: ";

        // Only replace the explicit history block. Everything else is immutable.
        public static string Fit(string prompt, string history, int budget, Func<string, int> tokens, bool structured = false)
        {
            if (tokens(prompt) <= budget) return prompt;
            if (string.IsNullOrEmpty(history)) return null;
            string block = structured ? Messages(history) : LocalDialogue.HistoryContext(history);
            if (block.Length == 0) return null;
            int start = prompt.IndexOf(block, StringComparison.Ordinal);
            if (start < 0) return null;
            string prefix = prompt.Substring(0, start), suffix = prompt.Substring(start + block.Length);
            string fixedPrompt = prefix + suffix;
            if (tokens(fixedPrompt) > budget) return null;
            var starts = new List<int>();
            for (int i = 0; i < history.Length; i++)
                if ((i == 0 || history[i - 1] == '\n') && history.IndexOf("Player: ", i, StringComparison.Ordinal) == i)
                    starts.Add(i);
            int low = 0, high = starts.Count - 1;
            string best = fixedPrompt;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                string recent = history.Substring(starts[middle]);
                string candidate = prefix + (structured ? Messages(recent) : LocalDialogue.HistoryContext(recent)) + suffix;
                if (tokens(candidate) <= budget) { best = candidate; high = middle - 1; }
                else low = middle + 1;
            }
            return best;
        }

        public static string Messages(string history)
        {
            var result = new StringBuilder();
            string[] lines=(history ?? string.Empty).Split('\n');int latest=-1;
            for(int i=0;i<lines.Length;i++) if(lines[i].StartsWith("Conversation: ",StringComparison.Ordinal)) latest=i;
            for(int i=0;i<lines.Length;i++)
            {
                string line=lines[i];
                if(line.StartsWith("Conversation: ",StringComparison.Ordinal))
                {
                    // Latest timestamp is pinned in fixed context, so shrinking
                    // the moving history cannot remove the current date/time.
                    if(i!=latest) result.Append("<|im_start|>system\nRecorded conversation: ").Append(line.Substring(14).TrimEnd('\r').Replace("<|","< |"))
                        .Append("<|im_end|>\n");
                    continue;
                }
                if(line.StartsWith(UnavailableResponseLabel,StringComparison.Ordinal))
                {
                    result.Append("<|im_start|>system\nRecorded transport fallback, not NPC speech: ")
                        .Append(line.Substring(UnavailableResponseLabel.Length).TrimEnd('\r').Replace("<|", "< |"))
                        .Append("<|im_end|>\n");
                    continue;
                }
                bool player = line.StartsWith("Player: ", StringComparison.Ordinal);
                if (!player && !line.StartsWith("NPC: ", StringComparison.Ordinal)) continue;
                result.Append("<|im_start|>").Append(player ? "user" : "assistant").Append('\n')
                    .Append(line.Substring(player ? 8 : 5).TrimEnd('\r').Replace("<|", "< |"))
                    .Append("<|im_end|>\n");
            }
            return result.ToString();
        }
        public static string CurrentTimestamp(string history)
        {
            string current="";
            foreach(string line in (history??"").Split('\n'))
                if(line.StartsWith("Conversation: ",StringComparison.Ordinal)) current=line.TrimEnd('\r');
            return current;
        }

        public static string LastNpcText(string history)
        {
            string[] lines = (history ?? string.Empty).Split('\n');
            for (int index = lines.Length - 1; index >= 0; index--)
            {
                string line = lines[index].TrimEnd('\r');
                if (line.StartsWith(UnavailableResponseLabel, StringComparison.Ordinal)) continue;
                if (!line.StartsWith("NPC: ", StringComparison.Ordinal)) continue;
                string text = DialogueCompletionText.ForClassicFont(line.Substring(5));
                text = LocalDialogue.NormalizeContext(text, int.MaxValue);
                if (text.Length > 0) return text;
            }
            return string.Empty;
        }

        public static bool IsDuplicateReply(string previousText, string candidateText)
        {
            string previous = NormalizeReplyForComparison(previousText);
            string candidate = NormalizeReplyForComparison(candidateText);
            return previous.Length > 0 && candidate.Length > 0 && string.Equals(previous, candidate, StringComparison.Ordinal);
        }

        public static bool IsPotentialDuplicatePrefix(string previousText, string cumulativeCandidate)
        {
            string previous = NormalizeReplyForComparison(previousText);
            string candidate = NormalizeReplyForComparison(cumulativeCandidate);
            return previous.Length > 0 && candidate.Length > 0 && previous.StartsWith(candidate, StringComparison.Ordinal);
        }

        private static string NormalizeReplyForComparison(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return LocalDialogue.NormalizeContext(DialogueCompletionText.ForClassicFont(value), int.MaxValue);
        }
    }
}
