using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcGoalDecomposition
    {
        public ConversationContextBinding Binding { get; }
        public string OriginalText { get; }
        public NpcGoalIntent Intent { get; }
        public IReadOnlyList<string> Kinds { get; }

        public NpcGoalDecomposition(ConversationContextBinding binding, string originalText, NpcGoalIntent intent,
            IList<string> kinds)
        {
            if (binding == null || originalText == null || originalText.Length > NpcGoalChat.MaximumTurnLength ||
                !NpcGoalTwoPassChat.IsWellFormedUtf16(originalText) || !Enum.IsDefined(typeof(NpcGoalIntent), intent))
                throw new ArgumentException("Invalid NPC goal decomposition.");
            if (kinds == null || kinds.Count > NpcGoalChat.MaximumSteps ||
                (intent == NpcGoalIntent.Request && kinds.Count == 0) ||
                (intent != NpcGoalIntent.Request && kinds.Count != 0))
                throw new ArgumentException("NPC goal intent conflicts with its kinds.");

            var copy = new List<string>(kinds.Count);
            foreach (string kind in kinds)
            {
                if (!NpcGoalChat.IsCanonicalKind(kind)) throw new ArgumentException("Unknown NPC goal kind.");
                copy.Add(kind);
            }
            Binding = binding;
            OriginalText = originalText;
            Intent = intent;
            Kinds = new ReadOnlyCollection<string>(copy);
        }
    }

    public sealed class NpcGoalDecompositionResult
    {
        public NpcGoalExtractionStatus Status { get; }
        public NpcGoalDecomposition Decomposition { get; }
        public string Reason { get; }

        public NpcGoalDecompositionResult(NpcGoalExtractionStatus status, NpcGoalDecomposition decomposition, string reason)
        {
            if (!Enum.IsDefined(typeof(NpcGoalExtractionStatus), status) ||
                (status == NpcGoalExtractionStatus.Valid) != (decomposition != null))
                throw new ArgumentException("Invalid NPC goal decomposition result.");
            Status = status;
            Decomposition = decomposition;
            Reason = NpcGoalExtractionResult.LimitReason(reason);
        }
    }

    /// <summary>Strict first-pass structure and second-pass parameter extraction for player goals.</summary>
    public static class NpcGoalTwoPassChat
    {
        private const int MaximumOutputLength = 32768;
        private static readonly string[] Kinds =
        {
            "Wait", "Follow", "MoveTo", "Purchase", "Transfer", "Interact", "TakeSeat",
            "ChangeSchedule", "Attack", "CastSpell", "Lockpick", "Climb", "Other"
        };

        public static string DecompositionGrammar()
        {
            var grammar = new StringBuilder();
            grammar.Append("root ::= ").Append(Term("{\"intent\":" )).Append(" intent ")
                .Append(Term(",\"steps\":[")).Append(" steps ").Append(Term("]}" )).Append('\n');
            grammar.Append("intent ::= ").Append(Term("\"request\"")).Append(" | ").Append(Term("\"none\""))
                .Append(" | ").Append(Term("\"clarify\"")).Append('\n');
            grammar.Append("steps ::= step (").Append(Term(",")).Append(" step)* | ").Append(Term("")).Append('\n');
            grammar.Append("step ::= ").Append(Term("{\"kind\":" )).Append(" kind ").Append(Term("}" )).Append('\n');
            grammar.Append("kind ::= ");
            for (int i = 0; i < Kinds.Length; i++)
            {
                if (i > 0) grammar.Append(" | ");
                grammar.Append(Term("\"" + Kinds[i] + "\""));
            }
            grammar.Append('\n').Append("string ::= ").Append(Term("\""))
                .Append(@" ( [^""\\\x00-\x1f] | ").Append(Term("\\"))
                .Append(" ( ").Append(Term("\"")).Append(" | ").Append(Term("\\"));
            foreach (string escape in new[] { "/", "b", "f", "n", "r", "t" })
                grammar.Append(" | ").Append(Term(escape));
            grammar.Append(" | ").Append(Term("u")).Append(" [0-9a-fA-F]{4} ) )* ").Append(Term("\""));
            return grammar.ToString();
        }

        public static string DecompositionInstructions(string originalText, string factualContext)
        {
            var prompt = new StringBuilder();
            prompt.Append("Identify only the player's requested intent and ordered canonical action kinds. Canonical kinds: Wait, Follow, MoveTo, Purchase, Transfer, Interact, TakeSeat, ChangeSchedule, Attack, CastSpell, Lockpick, Climb, Other. Return one JSON object with exact schema {\"intent\":\"request|none|clarify\",\"steps\":[{\"kind\":\"canonical kind\"}]}.\n");
            prompt.Append("Use request only for a current-turn action; include one kind per distinct requested step, in order, up to eight. Use none when no action is requested. Use clarify when intent or required meaning is unclear. None and clarify require empty steps. Do not extract targets or parameters. Do not infer willingness, feasibility, consent, or completion.\n");
            prompt.Append("Hiring or recruiting this NPC as a paid travel companion requests the existing Follow action. A wage or daily rate is payment context for that service, not a separate Purchase or Transfer action. Ordinary unpaid following remains distinct from paid recruitment; this pass records only the requested action kind.\n");
            prompt.Append("Quoted orders, reports of earlier orders, fictional dialogue, denied requests, and hypothetical questions are not current orders. Use none for ordinary social questions and questions about abilities without a present action request. Do not classify talking itself as Interact; Interact means a requested game-world interaction. A current polite request such as 'Could you follow me now?' is request.\n");
            prompt.Append("Conversation history is supplied separately. Factual context contains no action candidate menu:\n")
                .Append(factualContext ?? "").Append("\nExact current player turn:\n").Append(originalText ?? "").Append("\nJSON:");
            return prompt.ToString();
        }

        public static NpcGoalDecompositionResult ParseDecomposition(string output, ConversationContextBinding binding,
            string originalText)
        {
            if (string.IsNullOrWhiteSpace(output))
                return new NpcGoalDecompositionResult(NpcGoalExtractionStatus.Unavailable, null, "No decomposition output.");
            if (binding == null || originalText == null)
                return InvalidDecomposition("Missing request binding or original turn.");
            if (originalText.Length > NpcGoalChat.MaximumTurnLength)
                return InvalidDecomposition("Original turn exceeds length limit.");
            if (!IsWellFormedUtf16(originalText))
                return InvalidDecomposition("Original turn has invalid UTF16.");
            if (output.Length > MaximumOutputLength)
                return InvalidDecomposition("Decomposition output exceeds length limit.");

            try
            {
                var reader = new DecompositionJsonReader(output);
                reader.Expect('{');
                reader.Field("intent");
                string intentText = reader.String(16);
                NpcGoalIntent intent;
                if (intentText == "request") intent = NpcGoalIntent.Request;
                else if (intentText == "none") intent = NpcGoalIntent.None;
                else if (intentText == "clarify") intent = NpcGoalIntent.Clarify;
                else throw new FormatException("Unknown intent.");
                reader.CommaField("steps");
                List<string> kinds = reader.Steps();
                reader.Expect('}');
                reader.End();
                if ((intent == NpcGoalIntent.Request && kinds.Count == 0) ||
                    (intent != NpcGoalIntent.Request && kinds.Count != 0))
                    throw new FormatException("Intent and steps conflict.");
                var decomposition = new NpcGoalDecomposition(binding, originalText, intent, kinds);
                return new NpcGoalDecompositionResult(NpcGoalExtractionStatus.Valid, decomposition, "");
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
            {
                return InvalidDecomposition(ex.Message);
            }
        }

        public static string ParameterGrammar(NpcGoalDecomposition decomposition)
        {
            if (decomposition == null) return "";
            var grammar = new StringBuilder();
            grammar.Append("root ::= ").Append(Term("{\"intent\":\"" + IntentText(decomposition.Intent) + "\",\"steps\":["));
            for (int i = 0; i < decomposition.Kinds.Count; i++)
            {
                if (i > 0) grammar.Append(Term(","));
                grammar.Append("step").Append(i).Append(' ');
            }
            grammar.Append(Term("],\"conditions\":[")).Append(" strings ").Append(Term("],\"exclusions\":["))
                .Append(" strings ").Append(Term("],\"missing\":[")).Append(" strings ").Append(Term("]}")).Append('\n');
            for (int i = 0; i < decomposition.Kinds.Count; i++)
                grammar.Append("step").Append(i).Append(" ::= ").Append(Term("{\"kind\":\"" + decomposition.Kinds[i] + "\",\"source\":"))
                    .Append(" string ").Append(Term(",\"target\":" )).Append(" string ").Append(Term(",\"duration\":"))
                    .Append(" nullable-duration ").Append(Term(",\"quantity\":" )).Append(" nullable-quantity ")
                    .Append(Term(",\"missing\":[")).Append(" strings ").Append(Term("]}")).Append('\n');
            grammar.Append("strings ::= string (").Append(Term(",")).Append(" string)* | ").Append(Term("")).Append('\n')
                .Append("nullable-duration ::= ").Append(Term("null")).Append(" | ").Append(Term("{\"amount\":"))
                .Append(" positive-decimal ").Append(Term(",\"unit\":" )).Append(" time-unit ").Append(Term(",\"source\":"))
                .Append(" string ").Append(Term("}")).Append('\n').Append("nullable-quantity ::= ").Append(Term("null"))
                .Append(" | ").Append(Term("{\"amount\":" )).Append(" positive-integer ").Append(Term(",\"source\":"))
                .Append(" string ").Append(Term("}")).Append('\n').Append("time-unit ::= ").Append(Term("\"game_seconds\""))
                .Append(" | ").Append(Term("\"game_minutes\"")).Append(" | ").Append(Term("\"game_hours\"")).Append('\n')
                .Append("positive-decimal ::= [1-9] [0-9]* (" ).Append(Term(".")).Append(" [0-9]+)? | ")
                .Append(Term("0")).Append(" ").Append(Term(".")).Append(" [0-9]* [1-9] [0-9]*\n")
                .Append("positive-integer ::= [1-9] [0-9]*\n");
            grammar.Append("string ::= ").Append(Term("\"")).Append(@" ( [^""\\\x00-\x1f] | ").Append(Term("\\"))
                .Append(" ( ").Append(Term("\"")).Append(" | ").Append(Term("\\"));
            foreach (string escape in new[] { "/", "b", "f", "n", "r", "t" }) grammar.Append(" | ").Append(Term(escape));
            grammar.Append(" | ").Append(Term("u")).Append(" [0-9a-fA-F]{4} ) )* ").Append(Term("\""));
            return grammar.ToString();
        }

        public static string ParameterInstructions(NpcGoalDecomposition decomposition)
        {
            if (decomposition == null) return "";
            var prompt = new StringBuilder();
            prompt.Append("Extract only quoted parameters and constraints for the frozen intent and ordered kinds. Return JSON matching the supplied parameter grammar exactly.\n");
            prompt.Append("Intent is ").Append(IntentText(decomposition.Intent)).Append(". Frozen ordered kinds: ")
                .Append(string.Join(", ", decomposition.Kinds)).Append(". Do not add, remove, reorder, or rename steps.\n");
            prompt.Append("Each source must be an exact ordered quote from the current turn. Copy target text exactly from its source quote; target is empty when absent. Duration and quantity belong only to their own step; derive each only from that step's source quote. Duration is never a target. Duration is null when unstated; otherwise translate an explicit duration to a positive number and game_seconds, game_minutes, or game_hours. All duration units mean in-game time, never real time. Quantity is null when unstated; otherwise translate an explicitly stated quantity to a positive integer. For duration and quantity, preserve the exact written expression in source. Do not invent numbers or convert units. Preserve unresolved requirements in missing fields. Do not add candidates, feasibility, willingness, consent, or claims that an action happened.\n");
            prompt.Append("Copy a named destination or object into target; never leave a stated target empty. MoveTo, Purchase and Transfer need a target. Null optional quantity/duration is normal, not a missing requirement: walking to a place needs no quantity or duration. A Wait needs its stated duration. Conditions and exclusions contain only actual conditional/restrictive clauses, never action kinds. Empty lists are correct when no such clause exists.\n");
            prompt.Append("For paid travel-companion Follow recruitment, the stated service length is duration. A stated day of service means 24 hours: express one day as amount 24, unit game_hours, preserving the exact original day phrase as source. This service-day conversion is allowed; otherwise preserve duration units. The wage or daily rate is payment context, never an item quantity; quantity remains null. Native quote, NPC willingness and player confirmation occur later; do not infer them.\n");
            prompt.Append("For a missing parameter, use the exact step missing label target, duration, or quantity. For other unresolved meaning, preserve a specific clarification question. Never claim an unrelated uncertainty was resolved by filling a parameter.\n");
            // The sampler grammar constrains tokens but is not visible to the model.
            prompt.Append("Full parameter JSON contract (replace placeholders with extracted data):\n");
            prompt.Append("{\"intent\":\"request\",\"steps\":[{\"kind\":\"frozen kind\",\"source\":\"exact quote for this step only\",\"target\":\"exact target or empty string\",\"duration\":null,\"quantity\":null,\"missing\":[]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}\n");
            prompt.Append("A non-null duration has exact shape {\"amount\":number,\"unit\":\"game_seconds|game_minutes|game_hours\",\"source\":\"exact duration phrase\"}. ");
            prompt.Append("A non-null quantity has exact shape {\"amount\":integer,\"source\":\"exact quantity phrase\"}. ");
            prompt.Append("Each step source covers only its own action and parameters. Sources must not overlap. Use an empty string for absent target.\n");
            prompt.Append("Conversation history is supplied separately. The exact current player turn is authoritative.\nExact current player turn:\n")
                .Append(decomposition.OriginalText).Append("\nJSON:");
            return prompt.ToString();
        }

        public static bool TryPrepareParameterRepair(NpcGoalExtractionResult extraction,
            out NpcGoalDecomposition decomposition, out string issue)
        {
            decomposition = null;
            issue = "";
            if (extraction == null || extraction.Status != NpcGoalExtractionStatus.Valid ||
                extraction.Proposal == null || extraction.Proposal.Intent != NpcGoalIntent.Request) return false;
            NpcGoalProposal proposal = extraction.Proposal;
            issue = ParameterRepairIssue(proposal);
            if (issue.Length == 0) return false;
            var kinds = new List<string>(proposal.Steps.Count);
            foreach (NpcGoalStep step in proposal.Steps) kinds.Add(step.Kind);
            decomposition = new NpcGoalDecomposition(proposal.Binding, proposal.OriginalText, proposal.Intent, kinds);
            return true;
        }

        public static string ParameterRepairIssue(NpcGoalProposal proposal)
        {
            if (proposal == null) return "";
            var fields = new List<string>();
            for (int i = 0; i < proposal.Steps.Count; i++)
            {
                NpcGoalStep step = proposal.Steps[i];
                if (string.Equals(step.Kind, "Wait", StringComparison.Ordinal) && step.Duration == null)
                    fields.Add("steps[" + i + "].duration");
                if (RequiresExplicitTarget(step.Kind) && string.IsNullOrWhiteSpace(step.TargetText))
                    fields.Add("steps[" + i + "].target");
                for (int j = 0; j < step.Missing.Count; j++)
                    if (!string.IsNullOrEmpty(step.Missing[j])) fields.Add("steps[" + i + "].missing[" + j + "]");
            }
            for (int i = 0; i < proposal.Missing.Count; i++)
                if (!string.IsNullOrEmpty(proposal.Missing[i])) fields.Add("missing[" + i + "]");
            if (fields.Count == 0) return "";

            const int maximumIssueLength = 160;
            const string prefix = "Missing fields: ";
            const string moreMarker = "; ...";
            var issue = new StringBuilder(prefix);
            for (int i = 0; i < fields.Count; i++)
            {
                string separator = i == 0 ? "" : ", ";
                bool hasMore = i + 1 < fields.Count;
                int needed = separator.Length + fields[i].Length + (hasMore ? moreMarker.Length : 0);
                if (issue.Length + needed > maximumIssueLength)
                {
                    if (issue.Length + moreMarker.Length <= maximumIssueLength) issue.Append(moreMarker);
                    break;
                }
                issue.Append(separator).Append(fields[i]);
            }
            return issue.ToString();
        }

        public static string RequiredParameterIssue(NpcGoalProposal proposal)
        {
            if (proposal == null) return "";
            var fields = new List<string>();
            for (int i = 0; i < proposal.Steps.Count; i++)
            {
                NpcGoalStep step = proposal.Steps[i];
                if (!string.Equals(step.Kind, "Wait", StringComparison.Ordinal) || step.Duration != null) continue;
                fields.Add("steps[" + i + "].duration");
            }
            return fields.Count == 0 ? "" : "Wait missing: " + string.Join(",", fields);
        }

        public static string RepairInstructions(NpcGoalDecomposition decomposition, string previousOutput,
            string validationReason)
        {
            if (decomposition == null || previousOutput == null || validationReason == null ||
                previousOutput.Length > MaximumOutputLength || validationReason.Length > 160 ||
                !IsWellFormedUtf16(previousOutput) || !IsWellFormedUtf16(validationReason)) return "";

            string parameterInstructions = ParameterInstructions(decomposition);
            const string jsonSuffix = "\nJSON:";
            if (parameterInstructions.Length < jsonSuffix.Length ||
                !parameterInstructions.EndsWith(jsonSuffix, StringComparison.Ordinal)) return "";

            var prompt = new StringBuilder(parameterInstructions.Length + previousOutput.Length + validationReason.Length + 256);
            prompt.Append(parameterInstructions, 0, parameterInstructions.Length - jsonSuffix.Length);
            NpcGoalExtractionResult previous = ParseParameters(previousOutput, decomposition);
            prompt.Append("\nAn empty target on MoveTo, Purchase or Transfer may be filled from its existing source quote even without a missing hint. All existing nonempty values still remain fixed.\n");
            prompt.Append("Missing hints may be removed ONLY for the exact field filled in this repair: step hints target/duration/quantity, or root hints steps[index].target/duration/quantity. Preserve all other hints, including world facts or recipient clarification. A parameter fill does not resolve an unrelated uncertainty.\n");
            if (previous.Status == NpcGoalExtractionStatus.Valid && previous.Proposal != null)
            {
                prompt.Append("\nSafe-fill repair of a previously valid proposal. Keep every existing source quote, kind, step order, nonempty target, typed duration, typed quantity, condition, and exclusion unchanged. Only fill an empty target, null duration, or null quantity when a step or root missing hint is present; the null duration of a Wait may always be filled from an explicit duration in the exact current turn. Never overwrite or remove an existing value. Missing lists may only keep or remove existing hints; never add, rewrite, or reorder them. Removing any missing hint for a step requires filling at least one parameter in that same step. Removing any root missing hint requires filling at least one parameter somewhere. Keep unresolved hints. If a value cannot be resolved from the original current turn, preserve it as missing instead of guessing.\n");
            }
            else
                prompt.Append("\nRepair the previous parameter output using the same frozen intent, ordered kinds, exact current turn, and parameter grammar. The repair must fix only the listed issues. Do not change the plan or add, remove, reorder, or rename any step. If a value cannot be resolved from the original current turn, preserve it as missing instead of guessing.\n");
            prompt.Append("Previous output (quoted data):\n").Append(JsonQuoteData(previousOutput)).Append('\n');
            prompt.Append("Validation feedback (quoted data):\n").Append(JsonQuoteData(validationReason));
            prompt.Append(jsonSuffix);
            return prompt.ToString();
        }

        public static NpcGoalExtractionResult ParseParameterRepair(string output, NpcGoalDecomposition decomposition,
            string previousOutput)
        {
            NpcGoalExtractionResult repaired = ParseParameters(output, decomposition);
            if (repaired.Status != NpcGoalExtractionStatus.Valid) return repaired;
            NpcGoalExtractionResult previous = ParseParameters(previousOutput, decomposition);
            // A malformed first parameter pass has no trusted full proposal. Its
            // replacement still has to satisfy the frozen structure and native gates.
            if (previous.Status != NpcGoalExtractionStatus.Valid) return repaired;
            NpcGoalProposal old = previous.Proposal, current = repaired.Proposal;
            bool same = old.Conditions.SequenceEqual(current.Conditions, StringComparer.Ordinal) &&
                old.Exclusions.SequenceEqual(current.Exclusions, StringComparer.Ordinal) &&
                IsOrderedSubset(old.Missing, current.Missing);
            var filledRootFields = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; same && i < old.Steps.Count; i++)
            {
                NpcGoalStep a = old.Steps[i], b = current.Steps[i];
                if (a.Source.Text != b.Source.Text || !IsOrderedSubset(a.Missing, b.Missing))
                {
                    same = false;
                    break;
                }

                var filledStepFields = new HashSet<string>(StringComparer.Ordinal);
                bool hasMissingHint = HasNonemptyHint(a.Missing) || HasNonemptyHint(old.Missing);
                if (a.TargetText.Length == 0 && b.TargetText.Length != 0)
                {
                    if (!hasMissingHint && !RequiresExplicitTarget(a.Kind)) { same = false; break; }
                    filledStepFields.Add("target");
                }
                else if (a.TargetText.Length != 0 && a.TargetText != b.TargetText)
                {
                    same = false;
                    break;
                }

                if (a.Quantity == null && b.Quantity != null)
                {
                    if (!hasMissingHint) { same = false; break; }
                    filledStepFields.Add("quantity");
                }
                else if (a.Quantity != null && (b.Quantity == null ||
                    a.Quantity.Amount != b.Quantity.Amount || a.Quantity.Source != b.Quantity.Source))
                {
                    same = false;
                    break;
                }

                if (a.Duration == null && b.Duration != null)
                {
                    if (a.Kind != "Wait" && !hasMissingHint) { same = false; break; }
                    filledStepFields.Add("duration");
                }
                else if (a.Duration != null && (b.Duration == null || a.Duration.Amount != b.Duration.Amount ||
                    a.Duration.Unit != b.Duration.Unit || a.Duration.Source != b.Duration.Source))
                {
                    same = false;
                    break;
                }

                if (!RemovedHintsWereFilled(a.Missing, b.Missing, filledStepFields)) { same = false; break; }
                foreach (string field in filledStepFields) filledRootFields.Add("steps[" + i + "]." + field);
            }
            if (same && !RemovedHintsWereFilled(old.Missing, current.Missing, filledRootFields)) same = false;
            return same ? repaired : new NpcGoalExtractionResult(NpcGoalExtractionStatus.Invalid, null,
                "Parameter repair changed an existing constraint or unrelated parameter.");
        }

        private static bool IsOrderedSubset(IReadOnlyList<string> original, IReadOnlyList<string> repaired)
        {
            int next = 0;
            for (int i = 0; i < repaired.Count; i++)
            {
                while (next < original.Count && !string.Equals(original[next], repaired[i], StringComparison.Ordinal)) next++;
                if (next >= original.Count) return false;
                next++;
            }
            return true;
        }

        private static bool RemovedHintsWereFilled(IReadOnlyList<string> original, IReadOnlyList<string> repaired,
            HashSet<string> filledFields)
        {
            int next = 0;
            foreach (string hint in original)
            {
                if (next < repaired.Count && string.Equals(hint, repaired[next], StringComparison.Ordinal)) next++;
                else if (!filledFields.Contains(hint)) return false;
            }
            return next == repaired.Count;
        }

        private static bool HasNonemptyHint(IReadOnlyList<string> hints)
        {
            for (int i = 0; i < hints.Count; i++)
                if (!string.IsNullOrEmpty(hints[i])) return true;
            return false;
        }

        private static bool RequiresExplicitTarget(string kind)
        {
            // These existing binding families cannot resolve an empty object/destination.
            // Wait and Follow retain their current-place/player defaults.
            return kind == "MoveTo" || kind == "Purchase" || kind == "Transfer";
        }

        public static NpcGoalExtractionResult ParseParameters(string output, NpcGoalDecomposition decomposition)
        {
            if (decomposition == null)
                return new NpcGoalExtractionResult(NpcGoalExtractionStatus.Invalid, null, "Missing decomposition binding.");
            NpcGoalExtractionResult parsed = NpcGoalChat.Parse(output, decomposition.Binding, decomposition.OriginalText);
            if (parsed.Status != NpcGoalExtractionStatus.Valid) return parsed;
            NpcGoalProposal proposal = parsed.Proposal;
            if (proposal.Intent != decomposition.Intent || proposal.Steps.Count != decomposition.Kinds.Count)
                return new NpcGoalExtractionResult(NpcGoalExtractionStatus.Invalid, null,
                    "Parameter pass changed frozen intent or step count.");
            for (int i = 0; i < decomposition.Kinds.Count; i++)
            {
                if (!string.Equals(proposal.Steps[i].Kind, decomposition.Kinds[i], StringComparison.Ordinal))
                    return new NpcGoalExtractionResult(NpcGoalExtractionStatus.Invalid, null,
                        "Parameter pass changed frozen step kind or order.");
            }
            return parsed;
        }

        private static NpcGoalDecompositionResult InvalidDecomposition(string reason)
        {
            return new NpcGoalDecompositionResult(NpcGoalExtractionStatus.Invalid, null,
                NpcGoalExtractionResult.LimitReason(reason));
        }

        private static string IntentText(NpcGoalIntent intent)
        {
            return intent == NpcGoalIntent.Request ? "request" : intent == NpcGoalIntent.None ? "none" : "clarify";
        }

        private static string Term(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string JsonQuoteData(string value)
        {
            var quoted = new StringBuilder(value.Length + 2);
            quoted.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\b': quoted.Append("\\b"); break;
                    case '\f': quoted.Append("\\f"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < 0x20) quoted.Append("\\u").Append(((int)c).ToString("x4"));
                        else quoted.Append(c);
                        break;
                }
            }
            quoted.Append('"');
            return quoted.ToString();
        }

        internal static bool IsWellFormedUtf16(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }

        private sealed class DecompositionJsonReader
        {
            private readonly string text;
            private int position;

            public DecompositionJsonReader(string text) { this.text = text; }

            public void Expect(char expected)
            {
                White();
                if (position >= text.Length || text[position] != expected)
                    throw new FormatException("Expected '" + expected + "'.");
                position++;
            }

            public void Field(string expected)
            {
                string actual = String(32);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new FormatException("Unexpected or out-of-order field.");
                Expect(':');
            }

            public void CommaField(string expected)
            {
                Expect(',');
                Field(expected);
            }

            public List<string> Steps()
            {
                var result = new List<string>();
                Expect('[');
                White();
                if (TryConsume(']')) return result;
                while (true)
                {
                    if (result.Count >= NpcGoalChat.MaximumSteps) throw new FormatException("Too many steps.");
                    Expect('{');
                    Field("kind");
                    string kind = String(32);
                    if (!NpcGoalChat.IsCanonicalKind(kind)) throw new FormatException("Unknown step kind.");
                    Expect('}');
                    result.Add(kind);
                    White();
                    if (TryConsume(']')) return result;
                    Expect(',');
                }
            }

            public string String(int maximumLength)
            {
                White();
                if (position >= text.Length || text[position++] != '\"') throw new FormatException("Expected string.");
                var value = new StringBuilder();
                while (position < text.Length)
                {
                    char c = text[position++];
                    if (c == '\"')
                    {
                        string decoded = value.ToString();
                        if (decoded.Length > maximumLength || !IsWellFormedUtf16(decoded))
                            throw new FormatException("String exceeds limit or has invalid UTF16.");
                        return decoded;
                    }
                    if (c < 0x20) throw new FormatException("Unescaped control character.");
                    if (c == '\\')
                    {
                        if (position >= text.Length) throw new FormatException("Incomplete string escape.");
                        char escape = text[position++];
                        switch (escape)
                        {
                            case '\"': value.Append('\"'); break;
                            case '\\': value.Append('\\'); break;
                            case '/': value.Append('/'); break;
                            case 'b': value.Append('\b'); break;
                            case 'f': value.Append('\f'); break;
                            case 'n': value.Append('\n'); break;
                            case 'r': value.Append('\r'); break;
                            case 't': value.Append('\t'); break;
                            case 'u': value.Append((char)Hex4()); break;
                            default: throw new FormatException("Invalid string escape.");
                        }
                    }
                    else value.Append(c);
                    if (value.Length > maximumLength) throw new FormatException("String exceeds length limit.");
                }
                throw new FormatException("Unterminated string.");
            }

            private int Hex4()
            {
                if (position + 4 > text.Length) throw new FormatException("Incomplete Unicode escape.");
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = text[position++];
                    int digit = c >= '0' && c <= '9' ? c - '0' :
                        c >= 'a' && c <= 'f' ? c - 'a' + 10 :
                        c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0) throw new FormatException("Invalid Unicode escape.");
                    value = (value << 4) | digit;
                }
                return value;
            }

            public void End()
            {
                White();
                if (position != text.Length) throw new FormatException("Trailing data after JSON object.");
            }

            private bool TryConsume(char value)
            {
                White();
                if (position < text.Length && text[position] == value)
                {
                    position++;
                    return true;
                }
                return false;
            }

            private void White()
            {
                while (position < text.Length && (text[position] == ' ' || text[position] == '\t' ||
                    text[position] == '\r' || text[position] == '\n')) position++;
            }
        }
    }
}
