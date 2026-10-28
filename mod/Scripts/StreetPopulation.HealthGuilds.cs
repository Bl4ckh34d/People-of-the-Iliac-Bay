using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool TryCreateGuildHealthTask(ResidentActor actor, NpcHealthCareRecord care, double now,
            out NpcTaskPlan task, out string reason)
        {
            task = null;
            reason = "No reachable guild provider has an affordable native healing potion.";
            if (!actor || care == null || worldStore == null || !interior || indoorGeometry == null ||
                !actor.gameObject.activeInHierarchy || actor.Resident == null || !actor.Resident.IsAlive ||
                actor.Resident.IsQuestProtected || actor.Resident.AgeGroup != ResidentAgeGroup.Adult ||
                actor.Resident.Id != care.PersonId || QuestOwned(actor.Resident) || IsAlarmed(care.PersonId) ||
                worldStore.IsResidentAway(care.PersonId, now)) return false;

            NativeNpcInventoryOwner buyerOwner = actor.GetComponent<NativeNpcInventoryOwner>();
            if (!buyerOwner || buyerOwner.Binding == null || buyerOwner.Binding.PersonId != care.PersonId ||
                !NativeNpcInventoryBridge.TryCapture(worldStore, buyerOwner.Binding, out reason)) return false;

            Func<DaggerfallUnityItem, int> quote = item =>
            {
                int value;
                return QuoteShopItem(actor, item, 1, out value) ? value : 0;
            };
            var providers = new List<StaticNPC>(interior.GetComponentsInChildren<StaticNPC>());
            providers.Sort((left, right) =>
            {
                NativePerson a = KeeperPerson(left);
                NativePerson b = KeeperPerson(right);
                return string.CompareOrdinal(a == null ? string.Empty : a.Id, b == null ? string.Empty : b.Id);
            });

            foreach (StaticNPC npc in providers)
            {
                NativePerson provider;
                string providerReason;
                Vector3 approach;
                if (!TryGetLiveGuildProvider(actor, npc, now, out provider, out providerReason) ||
                    !TryGuildProviderApproach(actor, npc, out approach)) continue;

                NativeNpcGuildPotionOffer offer;
                if (!NativeNpcGuildPotionSupply.TryPrepareOffer(worldStore, actor, npc, KeeperPerson,
                    now, quote, out offer, out providerReason))
                {
                    reason = providerReason ?? reason;
                    continue;
                }
                if (offer == null || offer.Provider == null || offer.Provider.Id != provider.Id ||
                    offer.Item == null || offer.RecipeOfferId == 0 || offer.Price < 1 || offer.SellerBinding == null ||
                    offer.SellerBinding.PersonId != provider.Id) continue;

                NpcActionTarget target = ActionPosition(approach);
                target.PersonId = provider.Id;
                target.ContainerId = offer.ContainerId;
                target.ItemId = offer.RecipeOfferId.ToString(CultureInfo.InvariantCulture);
                target.Quantity = 1;
                target.QuotedGold = offer.Price;
                task = HealthPurchaseTask(care, now, target);
                reason = null;
                return true;
            }
            return false;
        }

        private bool IsGuildPotionTarget(NpcActionTarget target)
        {
            ulong container, item;int recipeKey;
            return target != null && target.Kind == NpcActionTargetKind.IndoorPoint &&
                !string.IsNullOrWhiteSpace(target.PersonId) &&
                target.ContainerId == NpcGuildPotionSupply.GuildPotionContainerId.ToString(CultureInfo.InvariantCulture) &&
                ulong.TryParse(target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out item) && NpcGuildPotionSupply.TryRecipeKey(item,out recipeKey) &&
                ulong.TryParse(target.ContainerId, NumberStyles.None, CultureInfo.InvariantCulture, out container) &&
                container == NpcGuildPotionSupply.GuildPotionContainerId && target.Quantity == 1 && target.QuotedGold > 0;
        }

        private bool PrepareGuildPotionPurchaseAction(ResidentActor actor, NpcActionJob job, out string reason)
        {
            reason = "The exact guild provider offer is no longer available.";
            if (!actor || job == null || job.Kind != NpcActionKind.Purchase || !IsGuildPotionTarget(job.Target) ||
                actor.Resident == null || actor.Resident.Id != job.ResidentId || !interior ||
                !ActionSpaceMatches(job.Target) || worldStore == null || QuestOwned(actor.Resident) ||
                actor.Resident.IsQuestProtected || IsAlarmed(job.ResidentId)) return false;

            NpcTaskPlan task = worldStore.ActiveNpcTask(job.ResidentId);
            NpcHealthCareRecord care = worldStore.ReadNpcHealthCare(job.ResidentId);
            if (task == null || !IsHealthCareTask(task) || care == null || care.Phase != NpcHealthCarePhase.Acting ||
                care.TaskId != task.Id || task.Steps == null || task.CurrentStepIndex >= task.Steps.Count ||
                task.Steps[task.CurrentStepIndex].Kind != NpcActionKind.Purchase ||
                !IsGuildPotionTarget(task.Steps[task.CurrentStepIndex].Target)) return false;

            StaticNPC npc = FindLiveGuildProvider(job.Target.PersonId, ActionNow(), out NativePerson provider, out reason);
            if (!npc || provider == null || provider.Id != job.Target.PersonId) return false;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            if (!owner || owner.Binding == null || owner.Binding.PersonId != job.ResidentId ||
                !NativeNpcInventoryBridge.TryCapture(worldStore, owner.Binding, out reason)) return false;

            Func<DaggerfallUnityItem, int> quote = item =>
            {
                int value;
                return QuoteShopItem(actor, item, 1, out value) ? value : 0;
            };
            NativeNpcGuildPotionOffer offer;
            double gameNow = ActionNow();
            if (!NativeNpcGuildPotionSupply.TryPrepareOffer(worldStore, actor, npc, KeeperPerson,
                gameNow, quote, out offer, out reason)) return false;
            ulong expectedOfferId;
            if (offer == null || offer.Provider == null || offer.Provider.Id != provider.Id ||
                !ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out expectedOfferId) ||
                offer.RecipeOfferId != expectedOfferId || offer.ContainerId != job.Target.ContainerId ||
                offer.Price != job.Target.QuotedGold || offer.Price < 1 || offer.SellerBinding == null ||
                offer.SellerBinding.PersonId != provider.Id || offer.SellerInventoryRevision < 0)
            {
                reason = "The guild potion recipe, price, provider or inventory revision changed.";
                return false;
            }

            Vector3 approach;
            if (!TryGuildProviderApproach(actor, npc, out approach))
            {
                reason = "No clear indoor route reaches the guild provider.";
                return false;
            }
            NpcActionTarget target = ActionPosition(approach);
            target.PersonId = provider.Id;
            target.ContainerId = offer.ContainerId;
            target.ItemId = offer.RecipeOfferId.ToString(CultureInfo.InvariantCulture);
            target.Quantity = 1;
            target.QuotedGold = offer.Price;
            job.Target = target;
            job.ExpectedInventoryRevision = owner.Binding.Revision;
            job.ExpectedTargetInventoryRevision = offer.SellerInventoryRevision;
            job.ServiceCode = NpcShopCommerce.ServiceCode;
            job.ServicePrice = offer.Price;
            job.ServiceStep = 0;
            reason = null;
            return true;
        }

        private bool ExecuteGuildPotionPurchaseAction(NpcActionJob job, ResidentActor actor, double now,
            out string reason)
        {
            reason = "The native guild potion purchase is no longer available.";
            if (!actor || job == null || job.Kind != NpcActionKind.Purchase || job.ServiceCode != NpcShopCommerce.ServiceCode ||
                !IsGuildPotionTarget(job.Target) || actor.Resident == null || actor.Resident.Id != job.ResidentId ||
                !interior || worldStore == null || !ActionSpaceMatches(job.Target) || QuestOwned(actor.Resident) ||
                actor.Resident.IsQuestProtected || IsAlarmed(job.ResidentId)) return false;

            NpcTaskPlan task = worldStore.ActiveNpcTask(job.ResidentId);
            NpcHealthCareRecord care = worldStore.ReadNpcHealthCare(job.ResidentId);
            if (task == null || !IsHealthCareTask(task) || care == null || care.Phase != NpcHealthCarePhase.Acting ||
                care.TaskId != task.Id) return false;

            StaticNPC npc = FindLiveGuildProvider(job.Target.PersonId, now, out NativePerson provider, out reason);
            if (!npc || provider == null || provider.Id != job.Target.PersonId) return false;
            NativeNpcInventoryOwner owner = actor.GetComponent<NativeNpcInventoryOwner>();
            if (!owner || owner.Binding == null || owner.Binding.PersonId != job.ResidentId ||
                !NativeNpcInventoryBridge.TryCapture(worldStore, owner.Binding, out reason)) return false;
            if (owner.Binding.Revision != job.ExpectedInventoryRevision)
            {
                reason = "The resident inventory changed before the guild purchase arrived.";
                return false;
            }

            Func<DaggerfallUnityItem, int> quote = item =>
            {
                int value;
                return QuoteShopItem(actor, item, 1, out value) ? value : 0;
            };
            ulong expectedOfferId;
            if (!ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out expectedOfferId))
            {
                reason = "The guild purchase no longer names a valid recipe offer.";
                return false;
            }

            // Revalidate the provider wallet and native recipe at arrival.
            NativeNpcInventoryBinding seller;
            if (!NativeNpcInventoryBridge.TryOpen(worldStore, provider.Id, null,
                NativeNpcInventoryBridge.InitialWalletGold(worldStore, provider.Id), out seller, out reason) ||
                seller == null || seller.PersonId != provider.Id || seller.Revision != job.ExpectedTargetInventoryRevision ||
                seller.Items == null)
            {
                if (string.IsNullOrEmpty(reason)) reason = "The guild provider inventory changed before arrival.";
                return false;
            }
            int recipeKey;
            if (!NpcGuildPotionSupply.TryRecipeKey(expectedOfferId, out recipeKey)) return false;
            DaggerfallUnityItem reservedItem = ItemBuilder.CreatePotion(recipeKey);
            if (!NativeNpcHealingItem.IsSupportedPotion(reservedItem))
            {
                reason = "The quoted recipe is no longer a supported native healing potion.";
                return false;
            }
            int currentPrice;
            try { currentPrice = quote(reservedItem); }
            catch (Exception error) { reason = error.Message; return false; }
            if (currentPrice != job.ServicePrice || currentPrice != job.Target.QuotedGold)
            {
                reason = "The quoted guild potion's live price changed before arrival.";
                return false;
            }

            Func<bool> arrivalAccess = () => GuildPotionArrivalAccess(actor, npc, job.Target, ActionNow());
            return NativeNpcInventoryBridge.TryCompleteActionGuildPotionPurchase(worldStore, job, now,
                owner.Binding, seller, npc, KeeperPerson, quote, arrivalAccess, out reason);
        }

        private bool GuildPotionArrivalAccess(ResidentActor actor, StaticNPC npc, NpcActionTarget target, double now)
        {
            NativePerson provider;
            string ignored;
            if (!TryGetLiveGuildProvider(actor, npc, now, out provider, out ignored) ||
                !ActionSpaceMatches(target) || target.PersonId != provider.Id) return false;

            Vector3 body = actor.transform.position - Vector3.up;
            Vector3 goal = ActionWorldPoint(target);
            if (Vector3.Distance(body, goal) > 0.55f || Vector3.Distance(body, npc.transform.position) > 2.2f ||
                !CivilianResponses.Sees(body + Vector3.up, npc.transform.position + Vector3.up, 2.2f)) return false;

            Vector3 from = interior.transform.InverseTransformPoint(body);
            Vector3 to = interior.transform.InverseTransformPoint(goal);
            List<GuardPoint3> route = shopGeometry != null ?
                shopGeometry.ActivityRouteBetween(from, to) : indoorGeometry.RouteBetween(from, to);
            return Vector3.Distance(body, goal) <= 0.25f || route != null && route.Count >= 2;
        }

        private bool TryGuildProviderApproach(ResidentActor actor, StaticNPC npc, out Vector3 point)
        {
            point = Vector3.zero;
            if (!interior || indoorGeometry == null || !actor || !npc) return false;
            Vector3 actual = actor.transform.position - Vector3.up;
            Vector3 target = npc.transform.position;
            Vector3 away = actual - target;
            away.y = 0;
            if (away.sqrMagnitude < 0.001f)
            {
                away = -npc.transform.forward;
                away.y = 0;
            }
            if (away.sqrMagnitude < 0.001f) away = Vector3.forward;
            Vector3 preferred = target + away.normalized * 1.0f;
            preferred.y = target.y;
            List<GuardPoint3> route = IndoorPersonApproachRoute(actual, target, preferred, 1.8f);
            if (route == null || route.Count == 0) return false;
            GuardPoint3 end = route[route.Count - 1];
            point = interior.transform.TransformPoint(new Vector3((float)end.X, (float)end.Y, (float)end.Z));
            return Vector3.Distance(point, target) <= 1.8f &&
                CivilianResponses.Sees(point + Vector3.up, target + Vector3.up, 1.8f);
        }

        private StaticNPC FindLiveGuildProvider(string providerId, double now, out NativePerson provider, out string reason)
        {
            provider = null;
            reason = "The native guild provider is not active in this interior.";
            if (string.IsNullOrWhiteSpace(providerId) || !interior || population == null || worldStore == null) return null;
            foreach (StaticNPC npc in interior.GetComponentsInChildren<StaticNPC>())
            {
                NativePerson candidate;
                string candidateReason;
                if (KeeperPerson(npc) == null || KeeperPerson(npc).Id != providerId ||
                    !TryGetLiveGuildProvider(null, npc, now, out candidate, out candidateReason)) continue;
                provider = candidate;
                reason = null;
                return npc;
            }
            return null;
        }

        private bool TryGetLiveGuildProvider(ResidentActor patient, StaticNPC npc, double now,
            out NativePerson provider, out string reason)
        {
            provider = null;
            reason = "The target is not an awake, reachable native guild potion provider.";
            if (!interior || indoorGeometry == null || worldStore == null || population == null || !npc ||
                !npc.gameObject.activeInHierarchy || npc.gameObject.scene != interior.gameObject.scene ||
                npc.GetComponentInParent<DaggerfallInterior>() != interior) return false;
            if (!NativeNpcGuildPotionSupply.TryResolveProvider(npc, KeeperPerson, out provider, out reason)) return false;
            if (!provider.IsAlive || provider.SettlementId != population.SettlementId ||
                provider.WorkKey != interior.EntryDoor.buildingKey || worldStore.NativeResponse(provider.Id) != null ||
                IsAlarmed(provider.Id) || KeeperSleeping(provider, now)) return false;
            SleepingPose sleep = npc.GetComponent<SleepingPose>();
            if (sleep && sleep.Active) return false;

            string providerId = provider.Id;
            Household family = population.Households.Find(home => home.Residents.Exists(resident => resident.Id == providerId));
            Resident member = family == null ? null : family.Residents.Find(resident => resident.Id == providerId);
            if (member != null && (member.IsQuestProtected || QuestOwned(member))) return false;
            if (patient && (!patient.gameObject.activeInHierarchy || patient.Resident == null ||
                !patient.Resident.IsAlive || QuestOwned(patient.Resident) || IsAlarmed(patient.Resident.Id))) return false;
            reason = null;
            return true;
        }

    }
}
