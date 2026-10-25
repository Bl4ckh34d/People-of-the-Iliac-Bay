using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Disposable actual-player proof of one exact native self-heal care action.</summary>
    public static class NativeNpcMagicHealingProbe
    {
        public static void Verify(WorldStateStore store, ResidentActor actor)
        {
            Require(store != null && actor && actor.Resident != null && actor.gameObject.activeInHierarchy,
                "Native magic-care probe requires one live ordinary resident.");
            string id = actor.Resident.Id;
            var body = actor.GetComponent<DaggerfallEntityBehaviour>();
            var manager = actor.GetComponent<EntityEffectManager>();
            var binding = actor.GetComponent<NativeNpcVitalsBinding>();
            Require(body && body.Entity != null && manager && binding &&
                ReferenceEquals(manager.EntityBehaviour, body) && !manager.IsPlayerEntity,
                "Native magic-care probe lacks its exact NPC effect manager and vitals binding.");
            NpcArchetype archetype = Archetype(store, id);
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var position = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
                WorldSpaceId = "native-healthcare-probe" };
            ReleasePriorPotionCare(store, id, archetype, position, now);
            bool quiesced = body.Entity.Quiesce;
            try
            {
                body.Entity.Quiesce = true;
                body.Entity.SetHealth(body.Entity.MaxHealth);
                body.Entity.SetMagicka(body.Entity.MaxMagicka);
            }
            finally { body.Entity.Quiesce = quiesced; }
            binding.Capture();
            var full = store.ReadNpcVitals(id, archetype);
            Require(full.IsPersisted && full.CurrentHealth == full.MaximumHealth &&
                full.CurrentHealth == body.Entity.CurrentHealth &&
                full.CurrentMagicka == body.Entity.CurrentMagicka && full.CurrentMagicka > 1,
                "Native magic-care fixture could not establish saved full HP and usable MP.");
            var previousCare = store.ReadNpcHealthCare(id);
            if (previousCare != null && previousCare.Phase != NpcHealthCarePhase.Recovered)
                Require(NpcHealthCareRecoveryHandoff.TryComplete(store, id, previousCare.Revision,
                    archetype, now, position), "Previous controlled potion care did not release at full HP.");
            NpcHealthNeedLifecycle.Observe(store, id, archetype, now);

            int originalSpellCount = body.Entity.SpellbookCount();
            Require(originalSpellCount >= 0 && originalSpellCount < 128 && !manager.HasReadySpell,
                "Native magic-care fixture has no safe spellbook slot or has a ready spell.");
            bool seeded = false;
            try
            {
                EffectSettings magnitude = BaseEntityEffect.DefaultEffectSettings();
                magnitude.MagnitudeBaseMin = 1;
                magnitude.MagnitudeBaseMax = 1;
                magnitude.MagnitudePlusMin = 0;
                magnitude.MagnitudePlusMax = 0;
                magnitude.MagnitudePerLevel = 1;
                var spell = manager.CreateSpellBundle(HealHealth.EffectKey, ElementTypes.Magic, magnitude);
                Require(spell != null, "DFU could not build the controlled native HealHealth spell.");
                EffectBundleSettings settings = spell.Settings;
                settings.TargetType = TargetTypes.CasterOnly;
                settings.Name = "Native magic-care fixture heal";
                DaggerfallEntity caster = manager.UsePlayerCharacterSkillsForEnemyMagicCost ? null : body.Entity;
                (int _, int spellCost) = FormulaHelper.CalculateTotalEffectCosts(settings.Effects,
                    settings.TargetType, caster, settings.MinimumCastingCost);
                Require(spellCost > 0 && spellCost + 1 <= full.CurrentMagicka,
                    "Controlled native HealHealth cost exceeds this resident's saved MP.");
                body.Entity.AddSpell(settings);
                seeded = true;
                Require(body.Entity.SpellbookCount() == originalSpellCount + 1,
                    "Controlled native HealHealth spell was not added once.");

                int desired = Math.Max(1, body.Entity.MaxHealth / 2);
                int damage = body.Entity.CurrentHealth - desired;
                Require(damage > 0, "Controlled resident has too little HP to test self-healing.");
                body.DamageHealthFromSource((DaggerfallEntityBehaviour)null, damage, false, Vector3.zero);
                Require(body.Entity.CurrentHealth > 0 && body.Entity.CurrentHealth < body.Entity.MaxHealth,
                    "Native damage did not create a nonlethal care injury.");
                binding.Capture();
                now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var wounded = store.ReadNpcVitals(id, archetype);
                Require(wounded.IsPersisted && wounded.CurrentHealth == body.Entity.CurrentHealth &&
                    wounded.CurrentMagicka == body.Entity.CurrentMagicka,
                    "Controlled native injury did not reach the saved vitals ledger.");
                Require(NpcHealthNeedLifecycle.Observe(store, id, archetype, now),
                    "Native injury did not create a source-bound Health need.");
                Require(store.TryBeginNpcHealthCare(id, archetype, now, position),
                    "Native injury did not begin its own health-care episode.");
                var care = store.ReadNpcHealthCare(id);
                var goal = RecoveryGoal(store.ReadNpcMotivationState(id));
                Require(care != null && care.Phase == NpcHealthCarePhase.Seeking && goal != null &&
                    goal.Status == NpcMotivationGoalStatus.Active,
                    "Controlled injury has no active source-bound recovery goal.");

                NpcMagicCareOffer selected = null;
                foreach (var offer in NativeNpcMagicAction.DiscoverCareOptions(actor.gameObject, store, () => true))
                    if (offer.Need == NpcMagicCareNeed.Heal && offer.Authorization.SpellId.StartsWith(
                        "native:spellbook:" + originalSpellCount + ":", StringComparison.Ordinal))
                    { selected = offer; break; }
                int minimumHeal;
                Require(selected != null && NativeNpcMagicHealing.TryGetConservativeHealingMagnitude(
                    actor.gameObject, selected, out minimumHeal) && minimumHeal == 1,
                    "Seeded exact HealHealth spell was not discovered with its native one-HP minimum.");
                var spec = NpcMagicHealingSpec.FromOffer(selected);
                Require(spec.MagickaCost == spellCost && spec.HealthBefore == wounded.CurrentHealth &&
                    spec.MagickaBefore == wounded.CurrentMagicka,
                    "Discovered spell cost or pre-cast resources differ from native fixture values.");

                string taskId = NpcHealthCare.MakeTaskId(care);
                var task = new NpcTaskPlan { Id = taskId, ProposalId = taskId, ResidentId = id,
                    Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = now, UpdatedAt = now,
                    DeadlineAt = now + 1200, GoalId = goal.Id, GoalStartRevision = goal.GoalStartRevision };
                task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.CastSpell,
                    Target = NpcActionExecutor.CopyTarget(position), MagicHealing = spec });
                string reason;
                Require(store.TryAcceptAndBindNpcHealthCareTask(id, care.Revision, task, now, out reason),
                    "Exact magic-care task was not bound to its injury and recovery goal: " + reason);
                string actionId = NpcTaskContract.ActionId(task);
                var job = new NpcActionJob { Id = actionId, ProposalId = actionId, ResidentId = id,
                    Kind = NpcActionKind.CastSpell, Target = NpcActionExecutor.CopyTarget(position),
                    LastPosition = position, MagicHealing = spec, Phase = NpcActionPhase.Accepted,
                    Revision = 1, AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 600 };
                Require(store.TryDispatchNpcTaskStep(task.Id, task.Revision, now, job,
                    true, true, true, out reason), "Exact magic-care action could not dispatch: " + reason);
                Require(store.ObserveNpcAction(actionId, job.Revision, now, NpcActionObservation.Approach()),
                    "Exact magic-care action could not approach.");
                job = store.ReadNpcAction(actionId);
                Require(store.ObserveNpcAction(actionId, job.Revision, now, NpcActionObservation.Arrival()),
                    "Exact magic-care action could not arrive.");
                job = store.ReadNpcAction(actionId);
                Require(job.Phase == NpcActionPhase.Executing, "Exact magic-care action is not executing.");

                int beforeHealth = body.Entity.CurrentHealth;
                int beforeMagicka = body.Entity.CurrentMagicka;
                long beforeRevision = store.ReadNpcVitals(id, archetype).Revision;
                LiveEffectBundle[] beforeBundles = manager.EffectBundles;
                EntityEffectManager.OnCastReadySpellEventHandler extraDebit =
                    ignored => { body.Entity.DecreaseMagicka(1); };
                manager.OnCastReadySpell += extraDebit;
                bool rejected;
                try { rejected = !NativeNpcMagicHealing.TryExecute(store, job, actor.gameObject, now,
                    () => true, out reason); }
                finally { manager.OnCastReadySpell -= extraDebit; }
                var rejectedAction = store.ReadNpcAction(actionId);
                var rejectedVitals = store.ReadNpcVitals(id, archetype);
                Require(rejected && body.Entity.CurrentHealth == beforeHealth &&
                    body.Entity.CurrentMagicka == beforeMagicka &&
                    rejectedVitals.Revision == beforeRevision &&
                    rejectedVitals.CurrentHealth == beforeHealth &&
                    rejectedVitals.CurrentMagicka == beforeMagicka &&
                    rejectedAction.Phase == NpcActionPhase.Executing && rejectedAction.TreatmentReceipt == null &&
                    !manager.HasReadySpell && SameBundles(beforeBundles, manager.EffectBundles),
                    "Extra native MP debit was accepted or failed to roll back HP, MP, action or receipt.");

                now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                Require(NativeNpcMagicHealing.TryExecute(store, job, actor.gameObject, now,
                    () => true, out reason), "Actual native HealHealth self-cast did not commit: " + reason);
                var completed = store.ReadNpcAction(actionId);
                var after = store.ReadNpcVitals(id, archetype);
                Require(completed.Phase == NpcActionPhase.Completed && completed.TreatmentReceipt != null &&
                    completed.TreatmentReceipt.Kind == NpcTreatmentKind.MagicSelfCast &&
                    after.Revision == beforeRevision + 1 &&
                    after.CurrentHealth == body.Entity.CurrentHealth && after.CurrentHealth > beforeHealth &&
                    after.CurrentMagicka == body.Entity.CurrentMagicka &&
                    after.CurrentMagicka == beforeMagicka - spellCost && !manager.HasReadySpell &&
                    SameBundles(beforeBundles, manager.EffectBundles),
                    "Actual native cast did not persist exact healing, MP debit, receipt and effect cleanup.");
                binding.Capture();
                Require(store.ReadNpcVitals(id, archetype).Revision == after.Revision,
                    "Sampler replayed an already committed native self-heal.");
                Require(!NativeNpcMagicHealing.TryExecute(store, job, actor.gameObject, now,
                    () => true, out reason) && body.Entity.CurrentHealth == after.CurrentHealth &&
                    body.Entity.CurrentMagicka == after.CurrentMagicka &&
                    store.ReadNpcVitals(id, archetype).Revision == after.Revision,
                    "Completed exact magic-care action replay changed native or saved resources.");
                string encoded = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
                var restored = new WorldStateStore(0, PeopleOfTheIliacBayMod.ConversationArchiveRoot());
                restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), encoded));
                var persisted = restored.ReadNpcAction(actionId);
                Require(persisted != null && persisted.TreatmentReceipt != null &&
                    persisted.TreatmentReceipt.SpellId == spec.SpellId &&
                    restored.ReadNpcVitals(id, archetype).CurrentHealth == after.CurrentHealth &&
                    restored.ReadNpcVitals(id, archetype).CurrentMagicka == after.CurrentMagicka,
                    "DFU native serializer lost exact magic-care receipt or committed vitals.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_MAGIC_HEALING_NATIVE_PASS: realHealHealth=true; " +
                    "failedExtraDebitRollback=true; exactMagicka=true; sourceBoundCare=true; " +
                    "staleReplayRejected=true; nativeSerializer=true; samplerRebased=true");
            }
            finally
            {
                if (seeded && body.Entity.SpellbookCount() > originalSpellCount)
                    body.Entity.DeleteSpell(originalSpellCount);
            }
        }

        private static void ReleasePriorPotionCare(WorldStateStore store, string id,
            NpcArchetype archetype, NpcActionTarget position, double now)
        {
            var task = store.ActiveNpcTask(id);
            if (task == null) return;
            var action = store.ReadNpcAction(task.ActiveActionId);
            Require(action != null && action.Phase == NpcActionPhase.Completed &&
                action.TreatmentReceipt != null,
                "Previous controlled care task is not an observed terminal treatment.");
            Require(store.SaveNpcActionPosition(action.Id, action.Revision, position, true, now),
                "Previous controlled care action could not save its recovery origin.");
            Require(store.ReconcileNpcTask(task.Id, task.Revision, now),
                "Previous controlled potion task did not reconcile.");
            var care = store.ReadNpcHealthCare(id);
            var vitals = store.ReadNpcVitals(id, archetype);
            if (care != null && care.Phase == NpcHealthCarePhase.Acting &&
                vitals.CurrentHealth < vitals.MaximumHealth)
                Require(store.TryContinueNpcHealthCareAfterTreatment(id, care.Revision,
                    archetype, now, position),
                    "Previous partial potion care did not release its completed task.");
        }

        private static NpcArchetype Archetype(WorldStateStore store, string id)
        {
            var progress = store.ReadNpcProgress(id);
            return NpcVitalsBindingPolicy.ResolveArchetype(progress != null,
                progress == null ? NpcArchetype.Citizen : progress.Archetype);
        }

        private static NpcMotivationGoalView RecoveryGoal(NpcMotivationStateView motivation)
        {
            if (motivation == null) return null;
            foreach (var goal in motivation.Goals)
                if (goal.Id == NpcHealthNeedLifecycle.RecoveryGoalId &&
                    goal.Kind == NpcMotivationGoalKind.Recovery &&
                    goal.Status == NpcMotivationGoalStatus.Active &&
                    goal.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                    goal.EvidenceKind == NpcMotivationStateEvidenceKind.NativeObservation)
                    return goal;
            return null;
        }

        private static bool SameBundles(LiveEffectBundle[] first, LiveEffectBundle[] second)
        {
            if (first == null || second == null || first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; i++)
                if (!ReferenceEquals(first[i], second[i])) return false;
            return true;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
