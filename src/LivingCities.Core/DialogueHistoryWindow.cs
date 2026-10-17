using System;
using System.Collections.Generic;
using System.Text;

namespace LivingCities.Core
{
    public static class DialogueHistoryWindow
    {
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
            foreach (string line in (history ?? string.Empty).Split('\n'))
            {
                bool player = line.StartsWith("Player: ", StringComparison.Ordinal);
                if (!player && !line.StartsWith("NPC: ", StringComparison.Ordinal)) continue;
                result.Append("<|im_start|>").Append(player ? "user" : "assistant").Append('\n')
                    .Append(line.Substring(player ? 8 : 5).TrimEnd('\r').Replace("<|", "< |"))
                    .Append("<|im_end|>\n");
            }
            return result.ToString();
        }
    }
}
