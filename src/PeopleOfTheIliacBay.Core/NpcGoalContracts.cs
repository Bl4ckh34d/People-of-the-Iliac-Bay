using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcGoalExtractionStatus { Valid, Invalid, Unavailable }
    public enum NpcGoalIntent { Request, None, Clarify }
    public enum NpcGoalTimeUnit { GameSeconds, GameMinutes, GameHours }

    public sealed class NpcGoalDuration
    {
        public decimal Amount { get; }
        public NpcGoalTimeUnit Unit { get; }
        public string Source { get; }

        public NpcGoalDuration(decimal amount, NpcGoalTimeUnit unit, string source)
        {
            if (amount <= 0 || !Enum.IsDefined(typeof(NpcGoalTimeUnit), unit) ||
                string.IsNullOrEmpty(source) || source.Length > 512)
                throw new ArgumentException("Invalid NPC goal duration.");
            Amount = amount;
            Unit = unit;
            Source = source;
        }
    }

    public sealed class NpcGoalQuantity
    {
        public int Amount { get; }
        public string Source { get; }

        public NpcGoalQuantity(int amount, string source)
        {
            if (amount <= 0 || string.IsNullOrEmpty(source) || source.Length > 512)
                throw new ArgumentException("Invalid NPC goal quantity.");
            Amount = amount;
            Source = source;
        }
    }

    public sealed class NpcGoalSourceSpan
    {
        public int Start { get; }
        public int Length { get; }
        public string Text { get; }

        public NpcGoalSourceSpan(int start, int length, string text)
        {
            if (start < 0 || length <= 0 || start > int.MaxValue - length || text == null || text.Length != length)
                throw new ArgumentException("Invalid NPC goal source span.");
            Start = start;
            Length = length;
            Text = text;
        }
    }

    public sealed class NpcGoalStep
    {
        public string Kind { get; }
        public NpcGoalSourceSpan Source { get; }
        public string TargetText { get; }
        public NpcGoalDuration Duration { get; }
        public NpcGoalQuantity Quantity { get; }
        public IReadOnlyList<string> Missing { get; }

        public NpcGoalStep(string kind, NpcGoalSourceSpan source, string targetText, NpcGoalDuration duration,
            NpcGoalQuantity quantity, IList<string> missing)
        {
            if (!NpcGoalChat.IsCanonicalKind(kind) || source == null)
                throw new ArgumentException("Invalid NPC goal step.");
            if (targetText != null && targetText.Length > 512)
                throw new ArgumentException("NPC goal target exceeds length limit.");
            Kind = kind;
            Source = source;
            TargetText = targetText ?? "";
            Duration = duration;
            Quantity = quantity;
            Missing = CopyStrings(missing, 8, 512, "step missing");
        }

        internal static ReadOnlyCollection<string> CopyStrings(IList<string> source, int maximumCount,
            int maximumLength, string name)
        {
            var copy = new List<string>();
            if (source != null)
            {
                if (source.Count > maximumCount)
                    throw new ArgumentException("Too many " + name + " values.");
                foreach (string value in source)
                {
                    if (value == null || value.Length > maximumLength)
                        throw new ArgumentException("Invalid " + name + " value.");
                    copy.Add(value);
                }
            }
            return new ReadOnlyCollection<string>(copy);
        }
    }

    public sealed class NpcGoalProposal
    {
        public ConversationContextBinding Binding { get; }
        public string OriginalText { get; }
        public NpcGoalIntent Intent { get; }
        public IReadOnlyList<NpcGoalStep> Steps { get; }
        public IReadOnlyList<string> Conditions { get; }
        public IReadOnlyList<string> Exclusions { get; }
        public IReadOnlyList<string> Missing { get; }

        public NpcGoalProposal(ConversationContextBinding binding, string originalText, NpcGoalIntent intent,
            IList<NpcGoalStep> steps, IList<string> conditions, IList<string> exclusions, IList<string> missing)
        {
            if (binding == null || originalText == null || originalText.Length > NpcGoalChat.MaximumTurnLength ||
                !Enum.IsDefined(typeof(NpcGoalIntent), intent))
                throw new ArgumentException("Invalid NPC goal proposal.");
            Binding = binding;
            OriginalText = originalText;
            Intent = intent;
            var stepCopy = new List<NpcGoalStep>();
            if (steps != null)
            {
                if (steps.Count > NpcGoalChat.MaximumSteps)
                    throw new ArgumentException("Too many NPC goal steps.");
                foreach (NpcGoalStep step in steps)
                {
                    if (step == null) throw new ArgumentException("Null NPC goal step.");
                    stepCopy.Add(step);
                }
            }
            Steps = new ReadOnlyCollection<NpcGoalStep>(stepCopy);
            int previousEnd = 0;
            foreach (NpcGoalStep step in stepCopy)
            {
                if (step.Source.Start < previousEnd || step.Source.Start > originalText.Length - step.Source.Length ||
                    !string.Equals(originalText.Substring(step.Source.Start, step.Source.Length), step.Source.Text, StringComparison.Ordinal) ||
                    !NpcGoalChat.OccursWithin(step.TargetText, step.Source.Text) ||
                    (step.Duration != null && !NpcGoalChat.OccursWithin(step.Duration.Source, step.Source.Text)) ||
                    (step.Quantity != null && !NpcGoalChat.OccursWithin(step.Quantity.Source, step.Source.Text)))
                    throw new ArgumentException("NPC goal step source or parameter is not grounded in the original turn.");
                previousEnd = step.Source.Start + step.Source.Length;
            }
            if ((intent == NpcGoalIntent.Request && stepCopy.Count == 0) ||
                (intent != NpcGoalIntent.Request && stepCopy.Count != 0))
                throw new ArgumentException("NPC goal intent conflicts with its steps.");
            Conditions = NpcGoalStep.CopyStrings(conditions, 16, 1024, "condition");
            Exclusions = NpcGoalStep.CopyStrings(exclusions, 16, 1024, "exclusion");
            Missing = NpcGoalStep.CopyStrings(missing, 16, 512, "missing");
        }
    }

    public sealed class NpcGoalExtractionResult
    {
        public NpcGoalExtractionStatus Status { get; }
        public NpcGoalProposal Proposal { get; }
        public string Reason { get; }

        public NpcGoalExtractionResult(NpcGoalExtractionStatus status, NpcGoalProposal proposal, string reason)
        {
            if (!Enum.IsDefined(typeof(NpcGoalExtractionStatus), status) ||
                (status == NpcGoalExtractionStatus.Valid) != (proposal != null))
                throw new ArgumentException("Invalid NPC goal extraction result.");
            Status = status;
            Proposal = proposal;
            Reason = LimitReason(reason);
        }

        internal static string LimitReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "";
            return reason.Length <= 160 ? reason : reason.Substring(0, 160);
        }
    }
}
