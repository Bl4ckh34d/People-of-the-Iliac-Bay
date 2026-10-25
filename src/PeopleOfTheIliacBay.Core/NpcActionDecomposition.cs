using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcActionDecompositionKind
    {
        None,
        Single,
        Chain
    }

    /// <summary>Exact, ordered partition of one original player utterance.</summary>
    public sealed class NpcActionDecomposition
    {
        private readonly ReadOnlyCollection<string> clauses;
        public readonly NpcActionDecompositionKind Kind;
        public IList<string> Clauses { get { return clauses; } }

        private NpcActionDecomposition(NpcActionDecompositionKind kind, IList<string> clauses)
        {
            Kind = kind;
            this.clauses = new ReadOnlyCollection<string>(new List<string>(clauses ?? new string[0]));
        }

        /// <summary>
        /// Parses verbatim action anchors and locates later actions in the
        /// original source. Returned clauses are sliced from source, never model text.
        /// </summary>
        public static NpcActionDecomposition Parse(string output, string originalText)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrEmpty(originalText) ||
                originalText.Length > 2000) return null;

            string value = output.Trim();
            if (value == "NONE")
                return new NpcActionDecomposition(NpcActionDecompositionKind.None, null);
            if (value == "SINGLE")
                return new NpcActionDecomposition(NpcActionDecompositionKind.Single, new[] { originalText });

            string[] fields = value.Split('|');
            if (fields.Length < 3 || fields.Length > NpcTaskContract.MaximumSteps + 1 || fields[0] != "CHAIN") return null;
            var actionStarts = new List<int>(fields.Length - 1);
            int searchFrom = 0;
            for (int i = 1; i < fields.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(fields[i])) return null;
                int found = originalText.IndexOf(fields[i], searchFrom, StringComparison.OrdinalIgnoreCase);
                if (found < searchFrom) return null;
                actionStarts.Add(found);
                searchFrom = found + fields[i].Length;
            }

            var clauses = new List<string>(actionStarts.Count);
            int sourceStart = 0;
            for (int i = 0; i < actionStarts.Count; i++)
            {
                int sourceEnd = i + 1 < actionStarts.Count ? actionStarts[i + 1] : originalText.Length;
                string clause = originalText.Substring(sourceStart, sourceEnd - sourceStart);
                if (string.IsNullOrWhiteSpace(clause)) return null;
                clauses.Add(clause);
                sourceStart = sourceEnd;
            }
            return new NpcActionDecomposition(NpcActionDecompositionKind.Chain, clauses);
        }

        public static string Grammar()
        {
            var grammar = new StringBuilder("root ::= \"NONE\" | \"SINGLE\" | chain\nchain ::= ");
            for (int count = 2; count <= NpcTaskContract.MaximumSteps; count++)
            {
                if (count > 2) grammar.Append(" | ");
                grammar.Append("chain").Append(count.ToString(CultureInfo.InvariantCulture));
            }
            grammar.Append('\n').Append("chunk ::= [^|]+\n");
            for (int count = 2; count <= NpcTaskContract.MaximumSteps; count++)
            {
                grammar.Append("chain").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" ::= \"CHAIN|\" chunk");
                for (int i = 1; i < count; i++) grammar.Append(" \"|\" chunk");
                grammar.Append('\n');
            }
            return grammar.ToString();
        }

        public static string Prompt(string originalText, string context)
        {
            return "Count the physical actions requested in the current message only. Return NONE for no current request, " +
                "SINGLE for one action, or CHAIN|verbatim anchor|verbatim anchor for 2.." + NpcTaskContract.MaximumSteps + " requested actions. " +
                "An anchor is a short exact phrase copied from the current message at the start of that action. " +
                "Keep source order. Do not paraphrase, repeat or overlap anchors. Do not copy candidate names in place of the player's words. " +
                "Do not add actions from context, capabilities or history. A duration is part of its action, not an extra waiting action. " +
                "A polite question can request actions. Count each explicitly requested distinct effect; do not combine sequential timed actions. " +
                "Following a person for a duration is one action. Moving to a place then waiting there is two actions. " +
                "Buying an item then giving it to someone is two actions. Do not invent steps for arrival or completion. " +
                "Quoted, reported, negated, hypothetical or conditional actions are not current requests. " +
                "A polite question asking this NPC to act is a current request, not a hypothetical or conditional statement. " +
                "Classify the whole current message, not individual keywords. This only identifies steps; willingness and native targets are checked later.\n" +
                "Available capabilities (reference data, not requests):\n" + (context ?? string.Empty) +
                "\nCURRENT player message (exact source):\n" + (originalText ?? string.Empty) + "\nDecomposition:";
        }
    }
}
