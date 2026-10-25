using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Native, immediate, self-only HealHealth treatment for an owned care action.</summary>
    public static class NativeNpcMagicHealing
    {
        private const string SpellPrefix = "native:spellbook:";
        private static readonly HashSet<string> activeCasters = new HashSet<string>(StringComparer.Ordinal);

        public static bool TryGetConservativeHealingMagnitude(GameObject actor, NpcMagicCareOffer offer,
            out int gain)
        {
            gain = 0;
            if (!actor || !actor.activeInHierarchy || offer == null || offer.Need != NpcMagicCareNeed.Heal ||
                offer.Authorization == null || offer.Authorization.Hostile ||
                offer.Authorization.Intent != NpcMagicIntent.Heal ||
                offer.Authorization.EffectKind != NpcMagicEffectKind.Health ||
                offer.Authorization.EffectId != HealHealth.EffectKey) return false;
            var resident = actor.GetComponent<ResidentActor>();
            var body = actor.GetComponent<DaggerfallEntityBehaviour>();
            var manager = actor.GetComponent<EntityEffectManager>();
            if (!resident || resident.Resident == null || !body || body.Entity == null || !manager ||
                !body.enabled || !manager.enabled || body.Entity.IsSilenced ||
                !ReferenceEquals(manager.EntityBehaviour, body) || manager.IsPlayerEntity || manager.HasReadySpell ||
                resident.Resident.Id != offer.Authorization.ActorId ||
                offer.Authorization.TargetId != offer.Authorization.ActorId ||
                body.Entity.CurrentHealth != offer.ObservedConditionBefore ||
                body.Entity.CurrentHealth >= body.Entity.MaxHealth ||
                body.Entity.CurrentMagicka != offer.Authorization.MagickaBefore) return false;
            EffectBundleSettings settings;
            IEntityEffect effect;
            if (!TryFindExactSpell(actor, body, manager, offer.Authorization.SpellId,
                    offer.Authorization.MagickaCost, out settings, out effect)) return false;
            try
            {
                EffectSettings magnitude = settings.Effects[0].Settings;
                if (!effect.Properties.SupportMagnitude || effect.Properties.SupportDuration ||
                    magnitude.MagnitudePerLevel <= 0 || magnitude.MagnitudeBaseMin < 0 ||
                    magnitude.MagnitudePlusMin < 0 || magnitude.MagnitudeBaseMax < magnitude.MagnitudeBaseMin ||
                    magnitude.MagnitudePlusMax < magnitude.MagnitudePlusMin) return false;
                int level = FormulaHelper.CalculateCasterLevel(body.Entity, effect);
                long minimum = (long)magnitude.MagnitudeBaseMin +
                    (long)magnitude.MagnitudePlusMin * (level / magnitude.MagnitudePerLevel);
                if (minimum <= 0 || minimum > int.MaxValue) return false;
                gain = (int)minimum;
                return true;
            }
            catch { return false; }
        }

        public static bool TryExecute(WorldStateStore store, NpcActionJob job, GameObject actor,
            double now, Func<bool> dutyStillAvailable, out string reason)
        {
            reason = "The exact self-healing spell is unavailable.";
            if (store == null || job == null || job.Kind != NpcActionKind.CastSpell ||
                job.MagicHealing == null || !actor || !actor.activeInHierarchy || dutyStillAvailable == null ||
                double.IsNaN(now) || double.IsInfinity(now)) return false;
            NpcMagicHealingSpec spec;
            try { spec = NpcMagicHealingSpec.CopyValidated(job.MagicHealing); }
            catch (ArgumentException) { return false; }
            var resident = actor.GetComponent<ResidentActor>();
            var binding = actor.GetComponent<NativeNpcVitalsBinding>();
            var body = actor.GetComponent<DaggerfallEntityBehaviour>();
            var manager = actor.GetComponent<EntityEffectManager>();
            if (!resident || resident.Resident == null || resident.Resident.Id != job.ResidentId ||
                !binding || !binding.enabled || !body || !body.enabled || body.Entity == null ||
                !manager || !manager.enabled || manager.HasReadySpell || body.Entity.IsSilenced ||
                manager.IsPlayerEntity || !ReferenceEquals(manager.EntityBehaviour, body) ||
                !NpcVitalsBindingPolicy.Eligible(resident.Resident.IsAlive,
                    resident.Resident.AgeGroup == ResidentAgeGroup.Adult,
                    resident.Resident.IsQuestProtected)) return false;
            int health, maximumHealth, magicka, maximumMagicka;
            if (!binding.TryReadCurrent(out health, out maximumHealth, out magicka, out maximumMagicka) ||
                health != spec.HealthBefore || magicka != spec.MagickaBefore ||
                health != body.Entity.CurrentHealth || maximumHealth != body.Entity.MaxHealth ||
                magicka != body.Entity.CurrentMagicka || maximumMagicka != body.Entity.MaxMagicka ||
                health >= maximumHealth) return false;
            NpcArchetype archetype;
            NpcVitalsView saved;
            try
            {
                archetype = store.ResolveNpcArchetype(job.ResidentId);
                saved = store.ReadNpcVitals(job.ResidentId, archetype);
            }
            catch { return false; }
            if (saved == null || !saved.IsAlive || !saved.IsPersisted ||
                saved.CurrentHealth != health || saved.MaximumHealth != maximumHealth ||
                saved.CurrentMagicka != magicka || saved.MaximumMagicka != maximumMagicka) return false;
            LiveEffectBundle[] beforeBundles;
            try { beforeBundles = manager.EffectBundles; }
            catch { return false; }
            if (!activeCasters.Add(job.ResidentId)) return false;

            EntityEffectBundle ownedReady = null;
            bool castStarted = false;
            bool quiesceEntered = false;
            bool originalQuiesce = false;
            bool committed = false;
            Exception failure = null;
            try
            {
                committed = store.TryCompleteNpcMagicHealingAction(job.Id, job.Revision, now, archetype,
                    saved.Revision, () =>
                    {
                        if (!actor || !actor.activeInHierarchy || manager.HasReadySpell ||
                            !manager.enabled || body.Entity.IsSilenced ||
                            !ReferenceEquals(actor.GetComponent<ResidentActor>(), resident) ||
                            resident.Resident == null || resident.Resident.Id != job.ResidentId ||
                            !SameResources(binding, body, health, maximumHealth, magicka, maximumMagicka) ||
                            !SameBundles(beforeBundles, manager.EffectBundles) ||
                            !SameSavedVitals(store, job.ResidentId, archetype, saved) ||
                            !dutyStillAvailable())
                            throw new InvalidOperationException("The native healing owner changed before casting.");
                        EffectBundleSettings settings;
                        IEntityEffect effect;
                        if (!TryFindExactSpell(actor, body, manager, spec.SpellId, spec.MagickaCost,
                                out settings, out effect) ||
                            !SameResources(binding, body, health, maximumHealth, magicka, maximumMagicka) ||
                            !SameBundles(beforeBundles, manager.EffectBundles) ||
                            !SameSavedVitals(store, job.ResidentId, archetype, saved))
                            throw new InvalidOperationException("The native healing spell changed before casting.");
                        originalQuiesce = body.Entity.Quiesce;
                        body.Entity.Quiesce = true;
                        quiesceEntered = true;
                        ownedReady = new EntityEffectBundle(settings, body);
                        // OnNewReadySpell runs synchronously inside SetReadySpell.
                        // Treat any mutation from that callback as a possible cast.
                        castStarted = true;
                        if (!manager.SetReadySpell(ownedReady))
                            throw new InvalidOperationException("The native manager rejected the healing spell.");
                        if (!ReferenceEquals(manager.ReadySpell, ownedReady) || !body.Entity.Quiesce ||
                            body.Entity.IsSilenced || !manager.enabled ||
                            !SameResources(binding, body, health, maximumHealth, magicka, maximumMagicka) ||
                            !SameBundles(beforeBundles, manager.EffectBundles) ||
                            !SameSavedVitals(store, job.ResidentId, archetype, saved) ||
                            resident.Resident == null || resident.Resident.Id != job.ResidentId)
                            throw new InvalidOperationException("The ready healing spell changed before casting.");
                        manager.CastNoAnimSpell();
                        int afterHealth = body.Entity.CurrentHealth;
                        int afterMagicka = body.Entity.CurrentMagicka;
                        if (!actor || !actor.activeInHierarchy || manager.HasReadySpell ||
                            resident.Resident == null || resident.Resident.Id != job.ResidentId ||
                            afterHealth <= health || afterHealth > maximumHealth ||
                            afterMagicka != magicka - spec.MagickaCost ||
                            !OnlyOneImmediateHealAdded(beforeBundles, manager.EffectBundles))
                            throw new InvalidOperationException("The native cast had no exact immediate healing result.");
                        var outcome = new NpcMagicObservedOutcome {
                            ActorId = job.ResidentId, TargetId = job.ResidentId,
                            SpellId = spec.SpellId, EffectId = HealHealth.EffectKey,
                            EffectKind = NpcMagicEffectKind.Health, NativeAdapterExecuted = true,
                            MagickaBefore = magicka, MagickaAfter = afterMagicka,
                            EffectValueBefore = health, EffectValueAfter = afterHealth
                        };
                        // HealHealth has no duration. Remove its sole live bundle before
                        // Core commits, so cleanup failure still permits native rollback.
                        RemoveNewHealBundles(manager, beforeBundles);
                        if (body.Entity.CurrentHealth != afterHealth ||
                            body.Entity.CurrentMagicka != afterMagicka)
                            throw new InvalidOperationException("Healing resources changed during native cleanup.");
                        return outcome;
                    });
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try
                {
                    if (ownedReady != null && manager &&
                        ReferenceEquals(manager.ReadySpell, ownedReady))
                        manager.AbortReadySpell();
                }
                catch (Exception error) { if (failure == null) failure = error; }
            }

            try
            {
                if (!committed)
                {
                    if (castStarted)
                    {
                        try { Rollback(body.Entity, manager, beforeBundles, health, magicka); }
                        catch (Exception rollbackError)
                        {
                            reason = "Native healing rollback failed: " + rollbackError.Message;
                            return false;
                        }
                    }
                    reason = failure == null ? "The healing action changed before its native result committed." : failure.Message;
                    return false;
                }
                try
                {
                    if (!binding.RebaselineAfterExternalCommit())
                        Debug.LogWarning("Native self-healing committed, but its vital sampler could not rebaseline.");
                }
                catch (Exception error)
                { Debug.LogWarning("Native self-healing committed, but sampler rebaseline failed: " + error.Message); }
                if (failure != null)
                    Debug.LogWarning("Native self-healing committed, but ready-spell cleanup reported: " + failure.Message);
                reason = null;
                return true;
            }
            finally
            {
                try { if (quiesceEntered) body.Entity.Quiesce = originalQuiesce; }
                finally { activeCasters.Remove(job.ResidentId); }
            }
        }

        private static bool SameResources(NativeNpcVitalsBinding binding, DaggerfallEntityBehaviour body,
            int health, int maximumHealth, int magicka, int maximumMagicka)
        {
            int h, mh, m, mm;
            return binding.TryReadCurrent(out h, out mh, out m, out mm) &&
                h == health && mh == maximumHealth && m == magicka && mm == maximumMagicka &&
                body.Entity.CurrentHealth == health && body.Entity.MaxHealth == maximumHealth &&
                body.Entity.CurrentMagicka == magicka && body.Entity.MaxMagicka == maximumMagicka;
        }

        private static bool SameSavedVitals(WorldStateStore store, string personId, NpcArchetype archetype,
            NpcVitalsView expected)
        {
            var current = store.ReadNpcVitals(personId, archetype);
            return current != null && current.IsAlive && current.IsPersisted &&
                current.Revision == expected.Revision && current.CurrentHealth == expected.CurrentHealth &&
                current.MaximumHealth == expected.MaximumHealth &&
                current.CurrentMagicka == expected.CurrentMagicka &&
                current.MaximumMagicka == expected.MaximumMagicka;
        }

        private static bool TryFindExactSpell(GameObject actor, DaggerfallEntityBehaviour body,
            EntityEffectManager manager, string spellId, int cost,
            out EffectBundleSettings settings, out IEntityEffect effect)
        {
            settings = new EffectBundleSettings();
            effect = null;
            try
            {
            int index;
            if (string.IsNullOrEmpty(spellId) || !spellId.StartsWith(SpellPrefix, StringComparison.Ordinal) ||
                !int.TryParse(spellId.Substring(SpellPrefix.Length).Split(':')[0],
                    NumberStyles.None, CultureInfo.InvariantCulture, out index) ||
                index < 0 || index >= 128 || index >= body.Entity.SpellbookCount()) return false;
            int matches = 0;
            foreach (var known in NativeNpcMagicAction.DiscoverSelfEffects(actor))
                if (known != null && known.SpellId == spellId && known.EffectId == HealHealth.EffectKey &&
                    known.EffectKind == NpcMagicEffectKind.Health && known.MagickaCost == cost && !known.Hostile)
                    matches++;
            if (matches != 1 || !body.Entity.GetSpell(index, out settings) ||
                settings.BundleType != BundleTypes.Spell || settings.TargetType != TargetTypes.CasterOnly ||
                settings.Effects == null || settings.Effects.Length != 1 ||
                settings.Effects[0].Key != HealHealth.EffectKey) return false;
                effect = GameManager.Instance.EntityEffectBroker.InstantiateEffect(settings.Effects[0]);
                if (effect == null || effect.GetType() != typeof(HealHealth) ||
                    effect.Key != HealHealth.EffectKey || effect.Properties.SupportDuration) return false;
                DaggerfallEntity costCaster = manager.UsePlayerCharacterSkillsForEnemyMagicCost ? null : body.Entity;
                (int _, int calculatedCost) = FormulaHelper.CalculateTotalEffectCosts(settings.Effects,
                    settings.TargetType, costCaster, settings.MinimumCastingCost);
                return calculatedCost == cost && cost > 0 && body.Entity.CurrentMagicka >= cost;
            }
            catch { return false; }
        }

        private static bool SameBundles(LiveEffectBundle[] before, LiveEffectBundle[] after)
        {
            if (before == null || after == null || before.Length != after.Length) return false;
            for (int i = 0; i < before.Length; i++)
                if (!ReferenceEquals(before[i], after[i])) return false;
            return true;
        }

        private static bool OnlyOneImmediateHealAdded(LiveEffectBundle[] before, LiveEffectBundle[] after)
        {
            if (before == null || after == null || after.Length != before.Length + 1 ||
                after[after.Length - 1] == null || after[after.Length - 1].liveEffects == null ||
                after[after.Length - 1].liveEffects.Count != 1 ||
                after[after.Length - 1].liveEffects[0] == null ||
                after[after.Length - 1].liveEffects[0].GetType() != typeof(HealHealth) ||
                after[after.Length - 1].liveEffects[0].Properties.SupportDuration) return false;
            for (int i = 0; i < before.Length; i++)
                if (!ReferenceEquals(before[i], after[i])) return false;
            return true;
        }

        private static void RemoveNewHealBundles(EntityEffectManager manager, LiveEffectBundle[] before)
        {
            var after = manager.EffectBundles;
            if (!OnlyOneImmediateHealAdded(before, after))
                throw new InvalidOperationException("The native effect ledger changed outside the healing spell.");
            manager.RemoveBundle(after[after.Length - 1]);
            if (!SameBundles(before, manager.EffectBundles))
                throw new InvalidOperationException("The native healing bundle was not removed exactly.");
        }

        private static void Rollback(DaggerfallEntity entity, EntityEffectManager manager,
            LiveEffectBundle[] before, int health, int magicka)
        {
            var after = manager.EffectBundles;
            if (after != null && after.Length == before.Length + 1 &&
                OnlyOneImmediateHealAdded(before, after))
                manager.RemoveBundle(after[after.Length - 1]);
            bool quiesced = entity.Quiesce;
            try
            {
                entity.Quiesce = true;
                entity.SetHealth(health);
                entity.SetMagicka(magicka);
            }
            finally { entity.Quiesce = quiesced; }
            if (entity.CurrentHealth != health || entity.CurrentMagicka != magicka ||
                !SameBundles(before, manager.EffectBundles))
                throw new InvalidOperationException("Native resources or effects differ from their pre-cast snapshot.");
        }
    }
}
