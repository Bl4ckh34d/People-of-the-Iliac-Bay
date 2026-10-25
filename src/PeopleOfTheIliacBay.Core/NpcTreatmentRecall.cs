using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    internal sealed class NpcTreatmentRecallEvidence
    {
        internal string Text, Authority;
        internal double NextFutureTreatmentAt;
    }

    public sealed partial class WorldStateStore
    {
        private const int MaximumTreatmentRecallTaskRows = 1000;
        private const int MaximumTreatmentRecallActionRows = 10000;

        internal NpcTreatmentRecallEvidence CaptureNpcTreatmentRecall(string personId, string query,
            double now, int maximumCharacters)
        {
            var result = new NpcTreatmentRecallEvidence
            {
                Text = "",
                Authority = TreatmentRecallAuthority(personId),
                NextFutureTreatmentAt = double.PositiveInfinity
            };
            if (state.NpcActions == null || state.NpcTasks == null || state.NpcActions.Count > MaximumTreatmentRecallActionRows ||
                state.NpcTasks.Count > MaximumTreatmentRecallTaskRows) return result;

            bool careQuery = IsNpcTreatmentRecallQuery(query);
            NpcActionJob latest = null;
            NpcTreatmentReceipt latestReceipt = null;
            NpcActionJob latestPurchase = null;
            NpcTreatmentOutcome latestOutcome = null;
            int latestScore = -1;

            var taskStepsByAction = new Dictionary<string, Tuple<NpcTaskPlan, int>>(StringComparer.Ordinal);
            foreach (NpcTaskPlan taskRow in state.NpcTasks)
            {
                if (taskRow == null || taskRow.ResidentId != personId) continue;
                NpcTaskPlan task = ReadNpcTask(taskRow.Id);
                if (task == null || task.Steps == null) continue;
                for (int i = 0; i < task.Steps.Count; i++)
                {
                    NpcTaskStep step = task.Steps[i];
                    if (step == null || !IsTreatmentAction(step.Kind)) continue;
                    taskStepsByAction[task.Id + ":step:" + i.ToString(CultureInfo.InvariantCulture)] = Tuple.Create(task, i);
                }
            }

            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcActionJob actionRow in state.NpcActions)
                if (actionRow != null && actionRow.ResidentId == personId && IsTreatmentAction(actionRow.Kind))
                    candidates.Add(actionRow.Id);
            foreach (string actionId in taskStepsByAction.Keys) candidates.Add(actionId);

            foreach (string actionId in candidates)
            {
                NpcActionJob action = ReadNpcAction(actionId);
                NpcTreatmentReceipt receipt = action == null ? null : action.TreatmentReceipt;
                if (action == null || action.ResidentId != personId || action.Phase != NpcActionPhase.Completed ||
                    string.IsNullOrWhiteSpace(action.CompletedEffectId) || action.CompletedEffectId != action.Id + ":observed" ||
                    receipt == null || !TreatmentReceiptMatches(action, receipt)) continue;

                // Once projected, the bounded outcome is the recall source. This
                // avoids double-counting a detailed action while it awaits prune.
                if (action.TreatmentOutcomeRevision > 0) continue;

                if (receipt.ObservedGameTime > now || action.UpdatedAt > now)
                {
                    double eligibleAt = Math.Max(receipt.ObservedGameTime, action.UpdatedAt);
                    if (eligibleAt < result.NextFutureTreatmentAt) result.NextFutureTreatmentAt = eligibleAt;
                    continue;
                }
                if (!careQuery) continue;

                Tuple<NpcTaskPlan, int> binding;
                NpcTaskPlan task = taskStepsByAction.TryGetValue(action.Id, out binding) ? binding.Item1 : null;
                int taskStep = task == null ? -1 : binding.Item2;
                NpcActionJob purchase = task != null && taskStep >= 0 && action.Kind == NpcActionKind.UseHealingItem ?
                    LinkedPurchaseForTreatment(action, receipt, task, taskStep) : null;
                if (task != null && !TreatmentActionMatchesTask(action, receipt, task, taskStep))
                {
                    task = null;
                    taskStep = -1;
                    purchase = null;
                }

                int score = TreatmentRecallQueryScore(query, receipt, purchase);
                if (latest == null || score > latestScore || score == latestScore &&
                    (receipt.ObservedGameTime > latestReceipt.ObservedGameTime ||
                    receipt.ObservedGameTime == latestReceipt.ObservedGameTime && string.CompareOrdinal(action.Id, latest.Id) < 0))
                {
                    latest = action;
                    latestReceipt = receipt;
                    latestPurchase = purchase;
                    latestScore = score;
                }
            }

            NpcTreatmentOutcomeHistory history=state.NpcTreatmentOutcomeHistories==null?null:
                state.NpcTreatmentOutcomeHistories.Find(value=>value.PersonId==personId);
            if(history!=null && history.OutcomeBaseRevision>0 &&
                history.DroppedEarliestObservedGameTime>now &&
                history.DroppedEarliestObservedGameTime<result.NextFutureTreatmentAt)
                result.NextFutureTreatmentAt=history.DroppedEarliestObservedGameTime;
            if(history!=null)
                foreach(NpcTreatmentOutcome outcome in history.Outcomes)
                {
                    if(outcome.ObservedGameTime>now)
                    {
                        if(outcome.ObservedGameTime<result.NextFutureTreatmentAt)
                            result.NextFutureTreatmentAt=outcome.ObservedGameTime;
                        continue;
                    }
                    if(!careQuery)continue;
                    int score=TreatmentRecallQueryScore(query,outcome);
                    double latestTime=latestOutcome!=null?latestOutcome.ObservedGameTime:
                        latestReceipt==null?double.NegativeInfinity:latestReceipt.ObservedGameTime;
                    string latestId=latestOutcome!=null?latestOutcome.ActionId:
                        latest==null?null:latest.Id;
                    if(score>latestScore || score==latestScore &&
                        (outcome.ObservedGameTime>latestTime || outcome.ObservedGameTime==latestTime &&
                         string.CompareOrdinal(outcome.ActionId,latestId)<0))
                    {
                        latestOutcome=outcome;
                        latestScore=score;
                    }
                }

            if(latestOutcome!=null)
                result.Text=FormatTreatmentOutcomeRecall(latestOutcome,maximumCharacters);
            else if (latest != null)
                result.Text = FormatTreatmentRecall(latest, latestReceipt, latestPurchase, maximumCharacters);
            else if(careQuery && history!=null && history.OutcomeBaseRevision>0 &&
                history.DroppedEarliestObservedGameTime<=now)
                result.Text=CleanMemory("Older treatment details are unavailable.",maximumCharacters);
            return result;
        }

        private string TreatmentRecallAuthority(string personId)
        {
            if (state.NpcActions == null || state.NpcTasks == null || state.NpcActions.Count > MaximumTreatmentRecallActionRows ||
                state.NpcTasks.Count > MaximumTreatmentRecallTaskRows) return "invalid";

            var records = new List<string>();
            var actionsById = new Dictionary<string, NpcActionJob>(StringComparer.Ordinal);
            foreach (NpcActionJob action in state.NpcActions)
                if (action != null) actionsById[action.Id] = action;

            var trackedActionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcActionJob action in state.NpcActions)
            {
                if (action == null || action.ResidentId != personId || !IsTreatmentAction(action.Kind)) continue;
                trackedActionIds.Add(action.Id);
            }

            foreach (NpcTaskPlan task in state.NpcTasks)
            {
                if (task == null || task.ResidentId != personId || task.Steps == null) continue;
                bool hasTreatment = false;
                for (int i = 0; i < task.Steps.Count; i++)
                {
                    NpcTaskStep step = task.Steps[i];
                    if (step == null || !IsTreatmentAction(step.Kind)) continue;
                    hasTreatment = true;
                    trackedActionIds.Add(task.Id + ":step:" + i.ToString(CultureInfo.InvariantCulture));
                    if (step.Kind == NpcActionKind.UseHealingItem && step.ItemSourceStep > 0 &&
                        step.ItemSourceStep <= i)
                        trackedActionIds.Add(task.Id + ":step:" + (step.ItemSourceStep - 1).ToString(CultureInfo.InvariantCulture));
                }
                if (hasTreatment) records.Add("T:" + task.Id.Length.ToString(CultureInfo.InvariantCulture) + ":" + task.Id + ":" +
                    task.Revision.ToString(CultureInfo.InvariantCulture));
            }

            foreach (string id in trackedActionIds)
            {
                NpcActionJob action;
                if (!actionsById.TryGetValue(id, out action))
                {
                    records.Add("A:" + id.Length.ToString(CultureInfo.InvariantCulture) + ":" + id + ":missing");
                    continue;
                }
                records.Add("A:" + id.Length.ToString(CultureInfo.InvariantCulture) + ":" + id + ":" +
                    action.ResidentId.Length.ToString(CultureInfo.InvariantCulture) + ":" + action.ResidentId + ":" +
                    ((int)action.Kind).ToString(CultureInfo.InvariantCulture) + ":" + action.Revision.ToString(CultureInfo.InvariantCulture));
            }

            NpcTreatmentOutcomeHistory history=state.NpcTreatmentOutcomeHistories==null?null:
                state.NpcTreatmentOutcomeHistories.Find(value=>value.PersonId==personId);
            if(history!=null)
                records.Add("H:"+history.Revision.ToString(CultureInfo.InvariantCulture)+":"+
                    history.OutcomeBaseRevision.ToString(CultureInfo.InvariantCulture)+":"+
                    TreatmentRecallNumber(history.DroppedEarliestObservedGameTime));

            records.Sort(StringComparer.Ordinal);
            var signature = new StringBuilder();
            foreach (string record in records) signature.Append(record).Append('\n');
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private NpcActionJob LinkedPurchaseForTreatment(NpcActionJob treatment, NpcTreatmentReceipt receipt,
            NpcTaskPlan task, int treatmentStepIndex)
        {
            if (treatment == null || receipt == null || task == null || task.ResidentId != treatment.ResidentId ||
                treatmentStepIndex < 0 || treatmentStepIndex >= task.Steps.Count ||
                task.CurrentStepIndex < treatmentStepIndex) return null;
            NpcTaskStep treatmentStep = task.Steps[treatmentStepIndex];
            int source = treatmentStep.ItemSourceStep - 1;
            if (treatmentStep.Kind != NpcActionKind.UseHealingItem || source < 0 || source >= treatmentStepIndex ||
                source >= task.Steps.Count || task.Steps[source].Kind != NpcActionKind.Purchase ||
                task.CurrentStepIndex <= source) return null;

            NpcTaskStep purchaseStep = task.Steps[source];
            NpcActionJob purchase = ReadNpcAction(task.Id + ":step:" + source.ToString(CultureInfo.InvariantCulture));
            ulong receiptUid;
            if (purchase == null || purchase.ResidentId != task.ResidentId || purchase.Kind != NpcActionKind.Purchase ||
                purchase.Phase != NpcActionPhase.Completed || purchase.CompletedEffectId != purchase.Id + ":observed" ||
                purchaseStep.CompletedEffectId != purchase.CompletedEffectId ||
                purchase.ServiceCode != NpcShopCommerce.ServiceCode || purchase.ServicePrice < 1 ||
                purchase.Target == null || purchase.Target.Kind != NpcActionTargetKind.IndoorPoint ||
                purchase.Target.Quantity != 1 || purchase.Target.QuotedGold != purchase.ServicePrice ||
                string.IsNullOrWhiteSpace(purchase.Target.PersonId) || string.IsNullOrWhiteSpace(purchase.AcquiredItemId) ||
                purchaseStep.Target == null || purchaseStep.Target.Kind != NpcActionTargetKind.IndoorPoint ||
                purchaseStep.Target.Quantity != 1 || purchaseStep.Target.QuotedGold != purchase.ServicePrice ||
                !NpcTaskContract.TargetMatches(purchase.Target, purchaseStep.Target) ||
                !ulong.TryParse(purchase.AcquiredItemId, NumberStyles.None, CultureInfo.InvariantCulture, out receiptUid) || receiptUid == 0 ||
                receipt.SourceItemUid != receiptUid || treatment.Target == null ||
                treatment.Target.ItemId != purchase.AcquiredItemId || treatment.Target.Quantity != 1 ||
                !NpcTaskContract.ConfirmsAcquiredItem(purchaseStep, purchase) ||
                purchase.UpdatedAt > receipt.ObservedGameTime || purchase.AcceptedAt > receipt.ObservedGameTime)
                return null;

            NpcActionTarget resolvedTarget = NpcActionExecutor.CopyTarget(treatmentStep.Target);
            if (resolvedTarget == null) return null;
            resolvedTarget.ItemId = purchase.AcquiredItemId;
            if (!NpcTaskContract.TargetMatches(resolvedTarget, treatment.Target)) return null;
            return purchase;
        }

        private bool TreatmentActionMatchesTask(NpcActionJob action, NpcTreatmentReceipt receipt,
            NpcTaskPlan task, int taskStepIndex)
        {
            if (task == null || action == null || receipt == null || task.ResidentId != action.ResidentId ||
                taskStepIndex < 0 || taskStepIndex >= task.Steps.Count) return false;
            NpcTaskStep step = task.Steps[taskStepIndex];
            if (step.Kind != action.Kind || step.CompletedEffectId != null && step.CompletedEffectId != action.CompletedEffectId)
                return false;
            if (step.Kind == NpcActionKind.CastSpell &&
                !NpcMagicHealingSpec.Same(step.MagicHealing,action.MagicHealing)) return false;
            if (step.ItemSourceStep <= 0)
                return NpcTaskContract.TargetMatches(step.Target, action.Target);
            if (step.Kind != NpcActionKind.UseHealingItem || LinkedPurchaseForTreatment(action, receipt, task, taskStepIndex) == null)
                return false;
            return true;
        }

        private static bool TreatmentReceiptMatches(NpcActionJob action, NpcTreatmentReceipt receipt)
        {
            return receipt != null && (receipt.Kind == NpcTreatmentKind.HealingItem && action.Kind == NpcActionKind.UseHealingItem ||
                receipt.Kind == NpcTreatmentKind.Rest && action.Kind == NpcActionKind.RestToHeal ||
                receipt.Kind == NpcTreatmentKind.MagicSelfCast && action.Kind == NpcActionKind.CastSpell &&
                action.MagicHealing != null) &&
                receipt.HealthAfter > receipt.HealthBefore && !double.IsNaN(receipt.ObservedGameTime) &&
                !double.IsInfinity(receipt.ObservedGameTime) && receipt.ObservedGameTime >= action.AcceptedAt &&
                receipt.ObservedGameTime <= action.UpdatedAt;
        }

        private static bool IsTreatmentAction(NpcActionKind kind)
        { return kind == NpcActionKind.UseHealingItem || kind == NpcActionKind.RestToHeal || kind == NpcActionKind.CastSpell; }

        private static bool IsNpcTreatmentRecallQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;
            string normalized = query.ToLowerInvariant();
            string[] terms = { "health", "heal", "potion", "hurt", "injur", "wound", "treat", "medicine", "sick", "rest", "recover", "damage",
                "cost", "price", "gold", "bought", "buy", "purchase", "seller", "shop", "pay", "paid",
                "spell", "cast", "magic", "magicka" };
            foreach (string term in terms) if (normalized.Contains(term)) return true;
            return false;
        }

        private static int TreatmentRecallQueryScore(string query, NpcTreatmentReceipt receipt, NpcActionJob purchase)
        {
            string normalized = (query ?? "").ToLowerInvariant();
            int score = 0;
            if (ContainsAny(normalized, "cost", "price", "gold", "bought", "buy", "purchase", "seller", "shop", "pay", "paid", "how much") &&
                purchase != null) score += 100;
            if (normalized.Contains("rest") && receipt.Kind == NpcTreatmentKind.Rest) score += 20;
            if ((normalized.Contains("potion") || normalized.Contains("healing item")) &&
                receipt.Kind == NpcTreatmentKind.HealingItem) score += 10;
            if (ContainsAny(normalized,"spell","cast","magic","magicka") &&
                receipt.Kind == NpcTreatmentKind.MagicSelfCast) score += 20;
            return score;
        }

        private static int TreatmentRecallQueryScore(string query,NpcTreatmentOutcome outcome)
        {
            string normalized=(query??"").ToLowerInvariant();
            int score=0;
            if(ContainsAny(normalized,"cost","price","gold","bought","buy","purchase","seller","shop","pay","paid","how much") &&
                outcome.PurchaseActionId!=null)score+=100;
            if(normalized.Contains("rest") && outcome.Kind==NpcTreatmentKind.Rest)score+=20;
            if((normalized.Contains("potion") || normalized.Contains("healing item")) &&
                outcome.Kind==NpcTreatmentKind.HealingItem)score+=10;
            if(ContainsAny(normalized,"spell","cast","magic","magicka") &&
                outcome.Kind==NpcTreatmentKind.MagicSelfCast)score+=20;
            return score;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            foreach (string term in terms) if (value.Contains(term)) return true;
            return false;
        }

        private string FormatTreatmentRecall(NpcActionJob action, NpcTreatmentReceipt receipt,
            NpcActionJob purchase, int maximumCharacters)
        {
            string time = TreatmentRecallNumber(receipt.ObservedGameTime);
            var text = new StringBuilder();
            if (receipt.Kind == NpcTreatmentKind.HealingItem)
                text.Append("I used a healing item at game time ").Append(time).Append("; my health improved from ")
                    .Append(receipt.HealthBefore.ToString(CultureInfo.InvariantCulture)).Append(" to ")
                    .Append(receipt.HealthAfter.ToString(CultureInfo.InvariantCulture)).Append('.');
            else if(receipt.Kind == NpcTreatmentKind.MagicSelfCast)
                text.Append("I cast Heal-Health on myself at game time ").Append(time)
                    .Append("; my health improved from ").Append(receipt.HealthBefore.ToString(CultureInfo.InvariantCulture))
                    .Append(" to ").Append(receipt.HealthAfter.ToString(CultureInfo.InvariantCulture))
                    .Append(". The spell spent ").Append(receipt.MagickaCost.ToString(CultureInfo.InvariantCulture))
                    .Append(" magicka.");
            else
                text.Append("I rested from game time ").Append(TreatmentRecallNumber(receipt.RestStartedAt))
                    .Append(" for ").Append(TreatmentRecallNumber(receipt.RestDurationSeconds))
                    .Append(" seconds; my health improved from ")
                    .Append(receipt.HealthBefore.ToString(CultureInfo.InvariantCulture)).Append(" to ")
                    .Append(receipt.HealthAfter.ToString(CultureInfo.InvariantCulture)).Append('.');

            if (purchase != null)
            {
                string provider = SafeTreatmentProviderName(purchase.Target.PersonId);
                text.Append(" I bought the healing item from ").Append(provider).Append(" for ")
                    .Append(purchase.ServicePrice.ToString(CultureInfo.InvariantCulture)).Append(" gold.");
            }
            text.Append(" The cause of the earlier health loss is not recorded.");
            return CleanMemory(text.ToString(), maximumCharacters);
        }

        private string FormatTreatmentOutcomeRecall(NpcTreatmentOutcome outcome,int maximumCharacters)
        {
            var text=new StringBuilder();
            if(outcome.Kind==NpcTreatmentKind.HealingItem)
                text.Append("I used a healing item at game time ").Append(TreatmentRecallNumber(outcome.ObservedGameTime))
                    .Append("; my health improved from ").Append(outcome.HealthBefore.ToString(CultureInfo.InvariantCulture))
                    .Append(" to ").Append(outcome.HealthAfter.ToString(CultureInfo.InvariantCulture)).Append('.');
            else if(outcome.Kind==NpcTreatmentKind.MagicSelfCast)
                text.Append("I cast Heal-Health on myself at game time ").Append(TreatmentRecallNumber(outcome.ObservedGameTime))
                    .Append("; my health improved from ").Append(outcome.HealthBefore.ToString(CultureInfo.InvariantCulture))
                    .Append(" to ").Append(outcome.HealthAfter.ToString(CultureInfo.InvariantCulture))
                    .Append(". The spell spent ").Append(outcome.MagickaCost.ToString(CultureInfo.InvariantCulture))
                    .Append(" magicka.");
            else
                text.Append("I rested from game time ").Append(TreatmentRecallNumber(outcome.RestStartedAt))
                    .Append(" for ").Append(TreatmentRecallNumber(outcome.RestDurationSeconds))
                    .Append(" seconds; my health improved from ").Append(outcome.HealthBefore.ToString(CultureInfo.InvariantCulture))
                    .Append(" to ").Append(outcome.HealthAfter.ToString(CultureInfo.InvariantCulture)).Append('.');
            if(outcome.PurchaseActionId!=null)
                text.Append(" I bought the healing item from ").Append(SafeTreatmentProviderName(outcome.SellerPersonId))
                    .Append(" for ").Append(outcome.PaidGold.ToString(CultureInfo.InvariantCulture)).Append(" gold.");
            text.Append(" The cause of the earlier health loss is not recorded.");
            return CleanMemory(text.ToString(),maximumCharacters);
        }

        private static string TreatmentRecallNumber(double value)
        { return value.ToString("G17", CultureInfo.InvariantCulture); }

        private string SafeTreatmentProviderName(string personId)
        {
            string name = null;
            Resident resident = ResolveResident(personId);
            if (resident != null) name = resident.FullName;
            if (string.IsNullOrWhiteSpace(name) && state.NativePeople != null)
            {
                NativePerson native = state.NativePeople.Find(person => person != null && person.Id == personId);
                if (native != null) name = native.Name;
            }
            if (string.IsNullOrWhiteSpace(name)) return "the shop";
            name = CleanMemory(name, 40).Trim();
            return string.IsNullOrWhiteSpace(name) ? "the shop" : name;
        }
    }
}
