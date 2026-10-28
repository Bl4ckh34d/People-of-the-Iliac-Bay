using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class NativeNpcTempleTrainingOffer
    {
        public NativePerson Trainer { get; internal set; }
        public int Skill { get; internal set; }
        public int Price { get; internal set; }
        public long ExpectedInventoryRevision { get; internal set; }
        public long ExpectedActorRevision { get; internal set; }
    }

    // One native temple service that admits non-members. No NPC guild membership.
    public static class NativeNpcTempleTraining
    {
        public const string ServiceCode = NpcShopCommerce.TrainingServiceCode;
        public const string TargetContainerId = NpcShopCommerce.TrainingTargetContainerId;
        public const double DurationSeconds = 10800d;

        public static bool TryPrepareOffer(WorldStateStore store, ResidentActor trainee, StaticNPC trainerNpc,
            Func<StaticNPC, NativePerson> resolveTrainer, int buildingFactionId, int nativeSkill,
            out NativeNpcTempleTrainingOffer offer, out string reason)
        { return TryPrepareOffer(store, trainee, trainerNpc, resolveTrainer, buildingFactionId, nativeSkill, true, out offer, out reason); }

        public static bool TryPrepareOffer(WorldStateStore store, ResidentActor trainee, StaticNPC trainerNpc,
            Func<StaticNPC, NativePerson> resolveTrainer, int buildingFactionId, int nativeSkill, bool requireFunds,
            out NativeNpcTempleTrainingOffer offer, out string reason)
        {
            offer = null;
            reason = "No current native temple training offer is available.";
            if (store == null || !trainee || !trainerNpc || resolveTrainer == null || nativeSkill < 0 || nativeSkill >= 35)
                return false;
            NativePerson trainer; List<DFCareer.Skills> skills; IGuild guild;
            if (!TryResolveTrainer(trainerNpc, resolveTrainer, buildingFactionId, out trainer, out guild, out skills, out reason)) return false;
            if (!NativeNpcTempleStartingBelongings.TryEnsure(store, trainerNpc, trainer, buildingFactionId, out reason)) return false;
            Resident resident = trainee.Resident;
            if (resident == null || !resident.IsAlive || resident.IsQuestProtected || resident.AgeGroup != ResidentAgeGroup.Adult ||
                store.ResolveResident(resident.Id) == null) { reason = "Only a living ordinary adult resident can train here."; return false; }
            DFCareer.Skills skill = (DFCareer.Skills)nativeSkill;
            if (!skills.Contains(skill)) { reason = "This live temple trainer does not offer that skill."; return false; }
            NpcArchetype archetype = store.ResolveNpcArchetype(resident.Id); NpcActorView actor;
            try { actor = store.ReadNpcActor(resident.Id, archetype); } catch (ArgumentException) { return false; }
            if (actor.Skill(nativeSkill) > guild.GetTrainingMax(skill)) { reason = "The resident is already too skilled for this native trainer."; return false; }
            DaggerfallEntityBehaviour traineeBody = trainee.GetComponent<DaggerfallEntityBehaviour>();
            if (traineeBody == null || traineeBody.Entity == null || traineeBody.Entity.Level < 1)
            { reason = "The resident has no valid native training level."; return false; }
            // Guild.GetTrainingPrice() correctly chooses the non-member rule but
            // multiplies it by PlayerEntity.Level. NPC training keeps that native
            // non-member rate and multiplies only by this resident's real level.
            int price;
            try { price = checked(Guild.nonMemberTrainingCost * traineeBody.Entity.Level); }
            catch (OverflowException) { reason = "The native temple training price is invalid."; return false; }
            if (price < 1 || price > NpcInventoryLimits.MaximumWalletGold) { reason = "The native temple training price is invalid."; return false; }
            NativeNpcInventoryOwner owner = trainee.GetComponent<NativeNpcInventoryOwner>();
            if (owner == null || owner.Binding == null || owner.Binding.PersonId != resident.Id ||
                !NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out reason)) return false;
            NpcInventoryState inventory = store.ReadNpcInventory(resident.Id);
            if (inventory == null || inventory.Revision != owner.Binding.Revision || requireFunds && inventory.WalletGold < price)
            { reason = "The resident cannot afford this native training."; return false; }
            offer = new NativeNpcTempleTrainingOffer { Trainer = trainer, Skill = nativeSkill, Price = price,
                ExpectedInventoryRevision = inventory.Revision, ExpectedActorRevision = actor.Revision };
            reason = null; return true;
        }

        // Action executor calls only after observed arrival at this exact trainer.
        // Step zero records one payment; step one completes only after three game hours.
        public static bool TryExecuteStage(WorldStateStore store, NpcActionJob job, ResidentActor trainee,
            StaticNPC trainerNpc, Func<StaticNPC, NativePerson> resolveTrainer, int buildingFactionId,
            double now, out bool completed, out string reason)
        {
            completed = false;
            reason = "The native temple training service is no longer available.";
            int skill;
            if (store == null || job == null || !trainee || trainee.Resident == null ||
                trainee.Resident.Id != job.ResidentId || GameManager.IsGamePaused || trainee.IsConversing ||
                job.Phase != NpcActionPhase.Executing || job.Kind != NpcActionKind.Purchase || job.ServiceCode != ServiceCode ||
                job.Target == null || job.Target.Kind != NpcActionTargetKind.IndoorPoint || job.Target.ContainerId != TargetContainerId ||
                job.Target.Quantity != 1 || !int.TryParse(job.Target.ItemId, out skill) || skill < 0 || skill >= 35 ||
                job.ServiceStep < 0 || job.ServiceStep > 1) return false;
            NativeNpcTempleTrainingOffer offer;
            if (!TryPrepareOffer(store, trainee, trainerNpc, resolveTrainer, buildingFactionId, skill, job.ServiceStep == 0, out offer, out reason) ||
                offer.Trainer == null || offer.Trainer.Id != job.Target.PersonId || offer.Price != job.ServicePrice ||
                offer.Price != job.Target.QuotedGold || offer.ExpectedInventoryRevision != job.ExpectedInventoryRevision)
            { reason = "The trainer, price, skill eligibility, or resident wallet changed before training."; return false; }
            if (job.ServiceStep == 0)
            {
                if (!store.TryPayNpcActionService(job.Id, job.Revision, now))
                { reason = "The resident wallet changed before the native lesson could begin."; return false; }
                return true;
            }
            if (job.FinishAt == 0 || now < job.FinishAt) return true;
            completed = store.TryCompleteNpcActionEffect(job.Id, job.Revision, now,
                () => ApplyNativeTraining(store, trainee, job, offer));
            if (!completed) reason = "The training action changed before its native result could be recorded.";
            return completed;
        }

        private static bool ApplyNativeTraining(WorldStateStore store, ResidentActor trainee, NpcActionJob job,
            NativeNpcTempleTrainingOffer offer)
        {
            if (GameManager.IsGamePaused || trainee.IsConversing || job.Phase != NpcActionPhase.Executing) return false;
            DaggerfallEntityBehaviour body = trainee.GetComponent<DaggerfallEntityBehaviour>();
            if (body == null || body.Entity == null) return false;
            NpcActorView actor;
            try { actor = store.ReadNpcActor(job.ResidentId, store.ResolveNpcArchetype(job.ResidentId)); }
            catch (ArgumentException) { return false; }
            if (actor.Revision != offer.ExpectedActorRevision) return false;
            int tally = UnityEngine.Random.Range(10, 21) * DaggerfallSkills.GetAdvancementMultiplier((DFCareer.Skills)offer.Skill);
            if (tally < 1 || !store.AwardNpcSkillExperience(job.ResidentId, actor.Profile.Archetype, offer.Skill, tally, actor.Revision)) return false;
            int trained = store.ReadNpcActor(job.ResidentId, actor.Profile.Archetype).Skill(offer.Skill);
            body.Entity.Skills.SetPermanentSkillValue((DFCareer.Skills)offer.Skill, (short)trained);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_TEMPLE_TRAINING: resident=" + job.ResidentId + "; trainer=" +
                offer.Trainer.Id + "; skill=" + offer.Skill + "; price=" + offer.Price + "; tally=" + tally);
            return true;
        }

        private static bool TryResolveTrainer(StaticNPC npc, Func<StaticNPC, NativePerson> resolveTrainer,
            int buildingFactionId, out NativePerson trainer, out IGuild guild, out List<DFCareer.Skills> skills, out string reason)
        {
            trainer = null; guild = null; skills = null; reason = "The target is not a live native temple trainer.";
            if (!npc || !npc.gameObject.activeInHierarchy || resolveTrainer == null || buildingFactionId <= 0 ||
                npc.Data.context != StaticNPC.Context.Building || npc.GetComponent<QuestResourceBehaviour>() != null ||
                npc.GetComponent<DaggerfallAction>() != null || !Services.HasGuildService(npc.Data.factionID) ||
                Services.GetService((GuildNpcServices)npc.Data.factionID) != GuildServices.Training) return false;
            NativePerson resolved; try { resolved = resolveTrainer(npc); } catch { return false; }
            if (resolved == null || !resolved.IsAlive || resolved.RetiredBedroomFlat || resolved.FactionId != npc.Data.factionID ||
                resolved.SettlementId != npc.Data.mapID || resolved.WorkKey != npc.Data.buildingKey || resolved.NativeHash != npc.Data.hash) return false;
            GuildManager manager = GameManager.Instance == null ? null : GameManager.Instance.GuildManager;
            if (manager == null || manager.GetGuildGroup(buildingFactionId) != FactionFile.GuildGroups.HolyOrder) return false;
            // Native temple exception, with native non-member price. Player membership never becomes NPC membership.
            guild = new NonMemberGuild(true);
            if (!guild.CanAccessService(GuildServices.Training)) return false;
            try { skills = Temple.GetTrainingSkills(Temple.GetDivine(buildingFactionId)); }
            catch (ArgumentOutOfRangeException) { return false; }
            if (skills == null || skills.Count == 0) return false;
            trainer = resolved; reason = null; return true;
        }
    }
}
