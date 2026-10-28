using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcBoundGoal
    {
        private readonly List<NpcActionDecision> decisions;

        public ConversationContextBinding Binding { get; }
        public string OriginalText { get; }

        internal NpcBoundGoal(ConversationContextBinding binding, string originalText,
            IList<NpcActionDecision> decisions)
        {
            Binding = binding;
            OriginalText = originalText;
            this.decisions = CopyDecisions(decisions);
        }

        public IList<NpcActionDecision> CopyDecisions()
        {
            return CopyDecisions(decisions);
        }

        private static List<NpcActionDecision> CopyDecisions(IList<NpcActionDecision> source)
        {
            var result = new List<NpcActionDecision>();
            if (source == null) return result;
            foreach (var decision in source)
            {
                if (decision == null || decision.Option == null) continue;
                var option = decision.Option;
                result.Add(new NpcActionDecision
                {
                    DurationSeconds = decision.DurationSeconds,
                    ContinueAfterFailure = decision.ContinueAfterFailure,
                    Option = new NpcActionOption
                    {
                        Id = option.Id,
                        Kind = option.Kind,
                        Target = NpcActionExecutor.CopyTarget(option.Target),
                        Label = option.Label,
                        ItemSourceStep = option.ItemSourceStep,
                        MaximumQuantity = option.MaximumQuantity,
                        PaidCompanion = option.PaidCompanion,
                        CompanionCast = option.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(option.CompanionCast)
                    }
                });
            }
            return result;
        }
    }

    /// <summary>Resolves extracted goals only against current offered native action options.</summary>
    public static class NpcGoalBinding
    {
        private const int MaximumQuantity = 1000000;
        private const int MinimumDurationSeconds = 10;
        private const int MaximumDurationSeconds = 900;
        private const int DefaultDurationSeconds = 300;

        public static bool TryBind(NpcGoalProposal proposal, IList<IList<NpcActionOption>> candidates,
            IList<int> selectedOptionIds, out NpcBoundGoal bound, out string reason)
        {
            bound = null;
            reason = "The complete goal could not be bound to current native options.";
            if (!ValidateProposal(proposal, out reason) || candidates == null || selectedOptionIds == null ||
                candidates.Count != proposal.Steps.Count || selectedOptionIds.Count != proposal.Steps.Count)
            {
                if (string.IsNullOrEmpty(reason)) reason = "Candidate count does not cover every requested step.";
                return false;
            }

            var decisions = new List<NpcActionDecision>();
            for (int i = 0; i < proposal.Steps.Count; i++)
            {
                NpcActionDecision decision;
                if (!TryBindStepCore(proposal, i, candidates[i], selectedOptionIds[i], decisions,
                    out decision, out reason)) return false;
                decisions.Add(decision);
            }

            bound = new NpcBoundGoal(proposal.Binding, proposal.OriginalText, decisions);
            reason = "";
            return true;
        }

        public static bool TryBindStep(NpcGoalProposal proposal, int index, IList<NpcActionOption> candidates,
            int selectedOptionId, IList<NpcActionDecision> previous, out NpcActionDecision decision, out string reason)
        {
            decision = null;
            if (!ValidateProposal(proposal, out reason)) return false;
            if (index < 0 || index >= proposal.Steps.Count || previous == null || previous.Count != index)
            {
                reason = "Previous bound steps do not match the requested step index.";
                return false;
            }

            var verifiedPrevious = new List<NpcActionDecision>();
            for (int i = 0; i < index; i++)
            {
                if (!VerifyPreviousDecision(proposal, i, previous, verifiedPrevious, out reason)) return false;
            }
            return TryBindStepCore(proposal, index, candidates, selectedOptionId, verifiedPrevious,
                out decision, out reason);
        }

        /// <summary>
        /// Filters a complete native candidate catalog to options that the binder can
        /// structurally bind at this step. Candidate ids are checked against the full
        /// catalog so duplicate ids remain ambiguous and are never offered.
        /// </summary>
        public static IList<NpcActionOption> FilterCandidates(NpcGoalProposal proposal, int index,
            IList<NpcActionOption> candidates, IList<NpcActionDecision> previous)
        {
            var filtered = new List<NpcActionOption>();
            if (proposal == null || proposal.Steps == null || candidates == null || previous == null ||
                index < 0 || index >= proposal.Steps.Count) return filtered;

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                NpcActionDecision ignored;
                string reason;
                if (!TryBindStep(proposal, index, candidates, candidate.Id, previous, out ignored, out reason))
                    continue;

                filtered.Add(new NpcActionOption
                {
                    Id = candidate.Id,
                    Kind = candidate.Kind,
                    Target = NpcActionExecutor.CopyTarget(candidate.Target),
                    Label = candidate.Label,
                    ItemSourceStep = candidate.ItemSourceStep,
                    MaximumQuantity = candidate.MaximumQuantity,
                    PaidCompanion = candidate.PaidCompanion,
                    CompanionCast = candidate.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(candidate.CompanionCast)
                });
            }
            return filtered;
        }

        private static bool VerifyPreviousDecision(NpcGoalProposal proposal, int index,
            IList<NpcActionDecision> supplied, IList<NpcActionDecision> verified, out string reason)
        {
            reason = "A previous decision does not match its extracted step.";
            var source = supplied[index];
            if (source == null || source.Option == null) return false;
            NpcActionDecision rebound;
            if (!TryBindStepCore(proposal, index, new[] { source.Option }, source.Option.Id,
                verified, out rebound, out reason)) return false;
            if (!SameDecision(source, rebound))
            {
                reason = "A previous decision changed its selected native option or parameters.";
                return false;
            }
            verified.Add(rebound);
            return true;
        }

        private static bool TryBindStepCore(NpcGoalProposal proposal, int index,
            IList<NpcActionOption> candidates, int selectedOptionId, IList<NpcActionDecision> previous,
            out NpcActionDecision decision, out string reason)
        {
            decision = null;
            reason = "Selected option is not one unique current native candidate of the requested kind.";
            if (candidates == null || selectedOptionId < 1) return false;
            NpcActionOption selected = null;
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Id != selectedOptionId) continue;
                if (selected != null) return false;
                selected = candidate;
            }
            if (selected == null) return false;

            var step = proposal.Steps[index];
            NpcActionKind requestedKind;
            bool taskStep = proposal.Steps.Count > 1;
            if (!TryParseKind(step.Kind, out requestedKind) ||
                taskStep && !NpcTaskContract.Supported(requestedKind) ||
                requestedKind != selected.Kind || selected.Target == null || selected.Id < 1 ||
                selected.MaximumQuantity < 0 || selected.MaximumQuantity > MaximumQuantity ||
                selected.ItemSourceStep < 0 || selected.ItemSourceStep > index ||
                selected.MaximumQuantity > 0 && selected.Kind != NpcActionKind.Transfer && selected.Kind != NpcActionKind.Purchase ||
                selected.PaidCompanion && taskStep ||
                selected.Kind == NpcActionKind.Follow && index != proposal.Steps.Count - 1)
            {
                reason = "Selected option kind or native parameters are unsupported for this step.";
                return false;
            }

            int duration;
            if (!ResolveDuration(step, selected, out duration, out reason)) return false;
            int quantity;
            if (!ResolveQuantity(step, selected, out quantity, out reason)) return false;

            var target = NpcActionExecutor.CopyTarget(selected.Target);
            target.Quantity = quantity;
            try { NpcActionContract.ValidateTarget(target); }
            catch (ArgumentException)
            {
                reason = "Selected option has an invalid native target.";
                return false;
            }

            var option = new NpcActionOption
            {
                Id = selected.Id,
                Kind = selected.Kind,
                Target = target,
                Label = selected.Label,
                ItemSourceStep = selected.ItemSourceStep,
                MaximumQuantity = selected.MaximumQuantity,
                PaidCompanion = selected.PaidCompanion,
                CompanionCast = selected.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(selected.CompanionCast)
            };
            var result = new NpcActionDecision { Option = option, DurationSeconds = duration };

            if (!ValidateNativeAction(proposal.Binding.PersonId, result, out reason)) return false;

            if (option.ItemSourceStep > 0 && !MatchesAcquiredSource(index, option, previous, out reason)) return false;
            if (option.Kind == NpcActionKind.Wait && index > 0)
            {
                NpcActionTarget boundTarget;
                if (!NpcTaskPositionBinding.TryBindWaitAfterMoveTo(previous[index - 1], result,
                    out boundTarget, out reason)) return false;
                if (!NpcTaskContract.TargetMatches(boundTarget, option.Target))
                {
                    reason = "Wait option does not use the exact preceding MoveTo target.";
                    return false;
                }
                option.Target = NpcActionExecutor.CopyTarget(boundTarget);
            }

            decision = result;
            reason = "";
            return true;
        }

        private static bool MatchesAcquiredSource(int index, NpcActionOption option,
            IList<NpcActionDecision> previous, out string reason)
        {
            reason = "Conditional transfer does not match one exact preceding acquired-item step.";
            int sourceIndex = option.ItemSourceStep - 1;
            if (option.Kind != NpcActionKind.Transfer || sourceIndex < 0 || sourceIndex >= index ||
                previous == null || sourceIndex >= previous.Count || option.MaximumQuantity != 0) return false;

            for (int i = 0; i < previous.Count; i++)
                if (i != sourceIndex && previous[i] != null && previous[i].Option != null &&
                    previous[i].Option.ItemSourceStep == option.ItemSourceStep) return false;

            var expected = NpcActionSequenceChat.AcquiredItemHandoverSemantic(sourceIndex, previous[sourceIndex]);
            if (expected == null || expected.Kind != option.Kind || expected.ItemSourceStep != option.ItemSourceStep ||
                !NpcTaskContract.TargetMatches(expected.Target, option.Target)) return false;
            reason = "";
            return true;
        }

        private static bool ValidateProposal(NpcGoalProposal proposal, out string reason)
        {
            reason = "Goal is incomplete or contains unsupported conditions, exclusions, or missing fields.";
            if (proposal == null || proposal.Binding == null || proposal.Intent != NpcGoalIntent.Request ||
                string.IsNullOrEmpty(proposal.OriginalText) || proposal.Steps == null || proposal.Steps.Count < 1 ||
                proposal.Steps.Count > NpcTaskContract.MaximumSteps || proposal.Conditions == null ||
                proposal.Exclusions == null || proposal.Missing == null || proposal.Conditions.Count != 0 ||
                proposal.Exclusions.Count != 0 || proposal.Missing.Count != 0) return false;

            int previousEnd = 0;
            for (int i = 0; i < proposal.Steps.Count; i++)
            {
                var step = proposal.Steps[i];
                if (step == null || step.Source == null || step.Missing == null || step.Missing.Count != 0 ||
                    step.Source.Start < previousEnd || step.Source.Start > proposal.OriginalText.Length ||
                    step.Source.Length > proposal.OriginalText.Length - step.Source.Start ||
                    !string.Equals(proposal.OriginalText.Substring(step.Source.Start, step.Source.Length),
                        step.Source.Text, StringComparison.Ordinal) ||
                    !ContainsExact(step.Source.Text, step.TargetText) ||
                    step.Duration != null && !ContainsExact(step.Source.Text, step.Duration.Source) ||
                    step.Quantity != null && !ContainsExact(step.Source.Text, step.Quantity.Source)) return false;

                previousEnd = step.Source.Start + step.Source.Length;
            }

            reason = "";
            return true;
        }

        private static bool ResolveDuration(NpcGoalStep step, NpcActionOption option, out int seconds, out string reason)
        {
            var kind = option.Kind;
            seconds = DefaultDurationSeconds;
            reason = "Duration literal does not match the requested step or supported native bounds.";
            bool timed = kind == NpcActionKind.Wait || kind == NpcActionKind.Follow || kind == NpcActionKind.TakeSeat;
            if (step.Duration == null)
            {
                if (option.PaidCompanion)
                { seconds = NpcPaidCompanionAction.DurationSeconds; reason = ""; return true; }
                if (kind == NpcActionKind.Wait)
                {
                    seconds = 0;
                    reason = "Wait requires an explicit game-time duration.";
                    return false;
                }
                reason = "";
                return true;
            }
            if (!timed) return false;
            decimal multiplier;
            switch (step.Duration.Unit)
            {
                case NpcGoalTimeUnit.GameSeconds: multiplier = 1m; break;
                case NpcGoalTimeUnit.GameMinutes: multiplier = 60m; break;
                case NpcGoalTimeUnit.GameHours: multiplier = 3600m; break;
                default: return false;
            }
            decimal totalSeconds;
            try { totalSeconds = checked(step.Duration.Amount * multiplier); }
            catch (OverflowException) { return false; }
            if (option.PaidCompanion)
            {
                if (totalSeconds != NpcPaidCompanionAction.DurationSeconds) return false;
                seconds = NpcPaidCompanionAction.DurationSeconds; reason = ""; return true;
            }
            if (totalSeconds < MinimumDurationSeconds || totalSeconds > MaximumDurationSeconds ||
                decimal.Truncate(totalSeconds) != totalSeconds) return false;
            seconds = (int)totalSeconds;
            reason = "";
            return true;
        }

        private static bool ResolveQuantity(NpcGoalStep step, NpcActionOption candidate,
            out int quantity, out string reason)
        {
            quantity = 0;
            reason = "Quantity literal does not match the offered native quantity bounds.";
            int fixedQuantity = candidate.Target.Quantity;
            bool quantityAction = candidate.Kind == NpcActionKind.Transfer || candidate.Kind == NpcActionKind.Purchase;
            if (!quantityAction)
            {
                if (step.Quantity != null) return false;
                quantity = fixedQuantity;
                reason = "";
                return true;
            }
            if (candidate.MaximumQuantity == 0)
            {
                if (fixedQuantity < 1 || fixedQuantity > MaximumQuantity) return false;
                if (step.Quantity == null)
                {
                    quantity = fixedQuantity;
                    reason = "";
                    return true;
                }
                quantity = step.Quantity.Amount;
                if (quantity != fixedQuantity) return false;
                reason = "";
                return true;
            }

            if (candidate.MaximumQuantity < 1 || candidate.MaximumQuantity > MaximumQuantity) return false;
            quantity = step.Quantity == null ? fixedQuantity : step.Quantity.Amount;
            if (quantity < 1 || quantity > candidate.MaximumQuantity) return false;
            reason = "";
            return true;
        }

        private static bool TryParseKind(string value, out NpcActionKind kind)
        {
            kind = default(NpcActionKind);
            return !string.IsNullOrEmpty(value) && Enum.TryParse(value, false, out kind) &&
                Enum.IsDefined(typeof(NpcActionKind), kind) && string.Equals(value, kind.ToString(), StringComparison.Ordinal);
        }

        private static bool ValidateNativeAction(string residentId, NpcActionDecision decision, out string reason)
        {
            reason = "Selected option violates the native single-action contract.";
            if (decision == null || decision.Option == null || decision.Option.Target == null) return false;
            var option = decision.Option;
            if (option.PaidCompanion)
            {
                // Binding is advisory only. Native admission still requires a separate
                // player confirmation and funding through the paid companion adapter.
                if (option.Kind != NpcActionKind.Follow || option.CompanionCast != null ||
                    option.Target.Kind != NpcActionTargetKind.Person ||
                    option.Target.PersonId != NpcDirectedRelationship.PlayerTargetId ||
                    decision.DurationSeconds != NpcPaidCompanionAction.DurationSeconds) return false;
                reason = ""; return true;
            }
            if (string.IsNullOrWhiteSpace(residentId)) residentId = "goal-binding-validation";
            var job = new NpcActionJob
            {
                Id = "goal-binding-validation",
                ProposalId = "goal-binding-validation",
                ResidentId = residentId,
                Kind = option.Kind,
                Target = NpcActionExecutor.CopyTarget(option.Target),
                Phase = NpcActionPhase.Accepted,
                Revision = 1,
                AcceptedAt = 0,
                UpdatedAt = 0,
                DeadlineAt = 86400,
                ServiceCode = option.Kind == NpcActionKind.Purchase ? NpcShopCommerce.ServiceCode : null,
                ServicePrice = option.Kind == NpcActionKind.Purchase ? option.Target.QuotedGold : 0,
                CompanionCast = option.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(option.CompanionCast)
            };
            try { NpcActionExecutor.Validate(job); }
            catch (ArgumentException)
            {
                return false;
            }
            reason = "";
            return true;
        }

        private static bool ContainsExact(string source, string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return source != null && source.IndexOf(value, StringComparison.Ordinal) >= 0;
        }

        private static bool SameDecision(NpcActionDecision a, NpcActionDecision b)
        {
            return a != null && b != null && a.Option != null && b.Option != null &&
                a.DurationSeconds == b.DurationSeconds && a.Option.Id == b.Option.Id && a.Option.Kind == b.Option.Kind &&
                a.Option.ItemSourceStep == b.Option.ItemSourceStep && a.Option.MaximumQuantity == b.Option.MaximumQuantity &&
                a.Option.PaidCompanion == b.Option.PaidCompanion &&
                string.Equals(a.Option.Label, b.Option.Label, StringComparison.Ordinal) &&
                NpcTaskContract.TargetMatches(a.Option.Target, b.Option.Target) &&
                (a.Option.CompanionCast==null && b.Option.CompanionCast==null ||
                 NpcCompanionCastSpec.Same(a.Option.CompanionCast,b.Option.CompanionCast));
        }
    }
}
