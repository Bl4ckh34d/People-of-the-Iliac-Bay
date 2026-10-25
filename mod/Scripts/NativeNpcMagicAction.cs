using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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
    /// <summary>
    /// Exact, synchronous self-cast adapter. A trusted action owner supplies Core
    /// authorization and a live duty check. No conversation text enters this path.
    /// </summary>
    public static class NativeNpcMagicAction
    {
        private const int MaxSpellbookEntries = 128;

        public static NpcMagicKnownEffect[] DiscoverSelfEffects(GameObject actor)
        {
            try
            {
                DaggerfallEntityBehaviour body;
                EntityEffectManager manager;
                if (!TryGetNativeActor(actor, out body, out manager)) return new NpcMagicKnownEffect[0];
                int count = body.Entity.SpellbookCount();
                if (count < 0 || count > MaxSpellbookEntries) return new NpcMagicKnownEffect[0];
                var found = new List<NpcMagicKnownEffect>();
                for (int index = 0; index < count; index++)
                {
                    EffectBundleSettings settings;
                    NpcMagicKnownEffect known;
                    if (body.Entity.GetSpell(index, out settings) &&
                        TryDescribeSpell(body, manager, settings, index, out known)) found.Add(known);
                }
                return found.ToArray();
            }
            catch { return new NpcMagicKnownEffect[0]; }
        }

        /// <summary>
        /// Read-only bridge for a future care task owner. It never casts, reserves,
        /// ranks against potions/rest, or marks a treatment complete.
        /// </summary>
        public static NpcMagicCareOffer[] DiscoverCareOptions(GameObject actor, WorldStateStore store,
            Func<bool> dutyAvailable)
        {
            try
            {
                if (store == null || dutyAvailable == null) return new NpcMagicCareOffer[0];
                DaggerfallEntityBehaviour body;
                EntityEffectManager manager;
                if (!TryGetNativeActor(actor, out body, out manager) || manager.HasReadySpell)
                    return new NpcMagicCareOffer[0];
                ResidentActor residentActor = actor.GetComponent<ResidentActor>();
                NativeNpcVitalsBinding binding = actor.GetComponent<NativeNpcVitalsBinding>();
                if (!residentActor || residentActor.Resident == null || !binding ||
                    !NpcVitalsBindingPolicy.Eligible(residentActor.Resident.IsAlive,
                        residentActor.Resident.AgeGroup == ResidentAgeGroup.Adult,
                        residentActor.Resident.IsQuestProtected) || !dutyAvailable())
                    return new NpcMagicCareOffer[0];
                int health, maximumHealth, magicka, maximumMagicka;
                if (!binding.TryReadCurrent(out health, out maximumHealth, out magicka, out maximumMagicka) ||
                    health != body.Entity.CurrentHealth || maximumHealth != body.Entity.MaxHealth ||
                    magicka != body.Entity.CurrentMagicka || maximumMagicka != body.Entity.MaxMagicka)
                    return new NpcMagicCareOffer[0];
                string id = residentActor.Resident.Id;
                NpcArchetype archetype = store.ResolveNpcArchetype(id);
                NpcVitalsView saved = store.ReadNpcVitals(id, archetype);
                if (saved == null || !saved.IsAlive || !saved.IsPersisted ||
                    saved.CurrentHealth != health || saved.MaximumHealth != maximumHealth ||
                    saved.CurrentMagicka != magicka || saved.MaximumMagicka != maximumMagicka)
                    return new NpcMagicCareOffer[0];
                return NpcMagicCarePort.Discover(new NpcMagicCareFacts {
                    ActorId = id, KnownEffects = DiscoverSelfEffects(actor),
                    CurrentHealth = health, MaximumHealth = maximumHealth,
                    CurrentMagicka = magicka, PoisonCount = manager.PoisonCount,
                    DiseaseCount = manager.DiseaseCount, NativeAdapterAvailable = true,
                    DutyAvailable = true, QuestProtected = residentActor.Resident.IsQuestProtected
                });
            }
            catch { return new NpcMagicCareOffer[0]; }
        }

        public static bool TryExecuteSelf(GameObject actor, WorldStateStore store,
            NpcMagicAuthorization authorization, Func<bool> dutyStillAvailable,
            out NpcMagicObservedOutcome outcome)
        {
            outcome = null;
            try
            {
            if (authorization == null || store == null || dutyStillAvailable == null ||
                authorization.ActorId != authorization.TargetId || authorization.Hostile) return false;

            DaggerfallEntityBehaviour body;
            EntityEffectManager manager;
            if (!TryGetNativeActor(actor, out body, out manager) || manager.HasReadySpell) return false;
            ResidentActor residentActor = actor.GetComponent<ResidentActor>();
            NativeNpcVitalsBinding vitals = actor.GetComponent<NativeNpcVitalsBinding>();
            if (!residentActor || residentActor.Resident == null || !vitals ||
                residentActor.Resident.Id != authorization.ActorId ||
                !NpcVitalsBindingPolicy.Eligible(residentActor.Resident.IsAlive,
                    residentActor.Resident.AgeGroup == ResidentAgeGroup.Adult,
                    residentActor.Resident.IsQuestProtected)) return false;

            int health, maximumHealth, magicka, maximumMagicka;
            if (!vitals.TryReadCurrent(out health, out maximumHealth, out magicka, out maximumMagicka) ||
                magicka != authorization.MagickaBefore || health != body.Entity.CurrentHealth ||
                maximumHealth != body.Entity.MaxHealth || maximumMagicka != body.Entity.MaxMagicka)
                return false;
            NpcArchetype archetype = store.ResolveNpcArchetype(authorization.ActorId);
            NpcVitalsView saved = store.ReadNpcVitals(authorization.ActorId, archetype);
            if (saved == null || !saved.IsAlive || !saved.IsPersisted ||
                saved.CurrentHealth != health || saved.MaximumHealth != maximumHealth ||
                saved.CurrentMagicka != magicka || saved.MaximumMagicka != maximumMagicka)
                return false;

            int count = body.Entity.SpellbookCount();
            if (count < 0 || count > MaxSpellbookEntries) return false;
            EffectBundleSettings selected = new EffectBundleSettings();
            int selectedIndex = -1;
            NpcMagicKnownEffect exact = null;
            for (int index = 0; index < count; index++)
            {
                EffectBundleSettings settings;
                NpcMagicKnownEffect known;
                if (!body.Entity.GetSpell(index, out settings) ||
                    !TryDescribeSpell(body, manager, settings, index, out known) ||
                    known.SpellId != authorization.SpellId) continue;
                if (exact != null) return false;
                selected = settings;
                selectedIndex = index;
                exact = known;
            }
            if (exact == null || exact.EffectId != authorization.EffectId ||
                exact.EffectKind != authorization.EffectKind ||
                exact.MagickaCost != authorization.MagickaCost) return false;

            int before = ReadEffectValue(body, manager, exact.EffectId, exact.EffectKind);
            var request = new NpcMagicRequest {
                ActorId = authorization.ActorId, TargetId = authorization.TargetId,
                SpellId = authorization.SpellId, EffectId = authorization.EffectId,
                Intent = authorization.Intent, EffectKind = authorization.EffectKind,
                MagickaCost = authorization.MagickaCost, Hostile = false
            };
            NpcMagicAuthorization fresh;
            bool duty;
            try { duty = dutyStillAvailable(); }
            catch { return false; }
            if (!NativeNpcMagicSelfCastGate.TryAuthorize(request, exact, magicka, before,
                    true, duty, residentActor.Resident.IsQuestProtected, out fresh) ||
                fresh.MagickaBefore != authorization.MagickaBefore ||
                exact.EffectKind == NpcMagicEffectKind.Health && health >= maximumHealth)
                return false;

            // The duty callback may have run game code. Recheck native ownership,
            // the exact spellbook entry, resources, and saved state before casting.
            EffectBundleSettings stillSelected;
            NpcMagicKnownEffect stillKnown;
            int currentHealth, currentMaximumHealth, currentMagicka, currentMaximumMagicka;
            NpcVitalsView currentSaved = store.ReadNpcVitals(authorization.ActorId, archetype);
            if (!TryGetNativeActor(actor, out DaggerfallEntityBehaviour currentBody,
                    out EntityEffectManager currentManager) ||
                !ReferenceEquals(currentBody, body) || !ReferenceEquals(currentManager, manager) ||
                manager.HasReadySpell || selectedIndex < 0 ||
                !body.Entity.GetSpell(selectedIndex, out stillSelected) ||
                !TryDescribeSpell(body, manager, stillSelected, selectedIndex, out stillKnown) ||
                stillKnown.SpellId != exact.SpellId || stillKnown.EffectId != exact.EffectId ||
                stillKnown.EffectKind != exact.EffectKind || stillKnown.MagickaCost != exact.MagickaCost ||
                !vitals.TryReadCurrent(out currentHealth, out currentMaximumHealth,
                    out currentMagicka, out currentMaximumMagicka) ||
                currentHealth != health || currentMaximumHealth != maximumHealth ||
                currentMagicka != magicka || currentMaximumMagicka != maximumMagicka ||
                currentSaved == null || !currentSaved.IsAlive || !currentSaved.IsPersisted ||
                currentSaved.Revision != saved.Revision || currentSaved.CurrentHealth != health ||
                currentSaved.MaximumHealth != maximumHealth ||
                currentSaved.CurrentMagicka != magicka || currentSaved.MaximumMagicka != maximumMagicka ||
                residentActor.Resident == null || residentActor.Resident.Id != authorization.ActorId ||
                residentActor.Resident.IsQuestProtected ||
                ReadEffectValue(body, manager, exact.EffectId, exact.EffectKind) != before)
                return false;

            EntityEffectBundle bundle = new EntityEffectBundle(selected, body);
            bool started = false;
            try
            {
                // SetReadySpell uses this manager's exact cost formula. CastNoAnimSpell
                // immediately assigns CasterOnly effects and debits native magicka.
                if (manager.HasReadySpell || !manager.SetReadySpell(bundle)) return false;
                started = true;
                manager.CastNoAnimSpell();
                if (!actor || !actor.activeInHierarchy || manager.HasReadySpell ||
                    body.Entity.CurrentHealth <= 0 || residentActor.Resident == null ||
                    residentActor.Resident.Id != authorization.ActorId) return false;
                var observed = new NpcMagicObservedOutcome {
                    ActorId = authorization.ActorId, TargetId = authorization.TargetId,
                    SpellId = authorization.SpellId, EffectId = authorization.EffectId,
                    EffectKind = authorization.EffectKind, NativeAdapterExecuted = true,
                    MagickaBefore = magicka, MagickaAfter = body.Entity.CurrentMagicka,
                    EffectValueBefore = before,
                    EffectValueAfter = ReadEffectValue(body, manager, exact.EffectId, exact.EffectKind)
                };
                if (!NpcMagicActionContract.MatchesObservedOutcome(fresh, observed)) return false;
                outcome = observed;
                return true;
            }
            catch { return false; }
            finally
            {
                if (started && manager && ReferenceEquals(manager.ReadySpell, bundle))
                    manager.AbortReadySpell();
            }
            }
            catch { outcome = null; return false; }
        }

        private static bool TryGetNativeActor(GameObject actor, out DaggerfallEntityBehaviour body,
            out EntityEffectManager manager)
        {
            body = null;
            manager = null;
            if (!actor || !actor.activeInHierarchy) return false;
            body = actor.GetComponent<DaggerfallEntityBehaviour>();
            manager = actor.GetComponent<EntityEffectManager>();
            return body && body.Entity != null && manager &&
                ReferenceEquals(manager.EntityBehaviour, body) && !manager.IsPlayerEntity;
        }

        private static bool TryDescribeSpell(DaggerfallEntityBehaviour body, EntityEffectManager manager,
            EffectBundleSettings settings, int index, out NpcMagicKnownEffect known)
        {
            known = null;
            if (settings.BundleType != BundleTypes.Spell || settings.TargetType != TargetTypes.CasterOnly ||
                settings.Effects == null || settings.Effects.Length != 1 ||
                string.IsNullOrEmpty(settings.Effects[0].Key)) return false;
            string key = settings.Effects[0].Key;
            NpcMagicEffectKind kind;
            if (key == HealHealth.EffectKey) kind = NpcMagicEffectKind.Health;
            else if (key == CurePoison.EffectKey || key == CureDisease.EffectKey)
                kind = NpcMagicEffectKind.Condition;
            else if (key == Shield.EffectKey) kind = NpcMagicEffectKind.TimedEffect;
            else return false;
            try
            {
                IEntityEffect effect = GameManager.Instance.EntityEffectBroker.InstantiateEffect(settings.Effects[0]);
                if (effect == null || effect.Key != key ||
                    kind == NpcMagicEffectKind.Health && effect.GetType() != typeof(HealHealth) ||
                    kind == NpcMagicEffectKind.Condition &&
                        effect.GetType() != typeof(CurePoison) && effect.GetType() != typeof(CureDisease) ||
                    kind == NpcMagicEffectKind.TimedEffect &&
                        (effect.GetType() != typeof(Shield) || !effect.Properties.SupportDuration)) return false;
                DaggerfallEntity costCaster = manager.UsePlayerCharacterSkillsForEnemyMagicCost ? null : body.Entity;
                (int _, int cost) = FormulaHelper.CalculateTotalEffectCosts(settings.Effects,
                    settings.TargetType, costCaster, settings.MinimumCastingCost);
                if (cost <= 0 || cost > body.Entity.MaxMagicka) return false;
                string payload = SaveLoadManager.Serialize(typeof(EffectBundleSettings), settings, false);
                if (string.IsNullOrEmpty(payload)) return false;
                byte[] digest;
                using (SHA256 hash = SHA256.Create())
                    digest = hash.ComputeHash(Encoding.UTF8.GetBytes(payload));
                known = new NpcMagicKnownEffect {
                    SpellId = "native:spellbook:" + index + ":" + BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant(),
                    EffectId = key, EffectKind = kind, MagickaCost = cost, Hostile = false
                };
                return true;
            }
            catch { return false; }
        }

        private static int ReadEffectValue(DaggerfallEntityBehaviour body, EntityEffectManager manager,
            string key, NpcMagicEffectKind kind)
        {
            if (kind == NpcMagicEffectKind.Health) return body.Entity.CurrentHealth;
            if (kind == NpcMagicEffectKind.Condition)
                return key == CurePoison.EffectKey ? manager.PoisonCount :
                    key == CureDisease.EffectKey ? manager.DiseaseCount : -1;
            if (kind != NpcMagicEffectKind.TimedEffect || key != Shield.EffectKey) return -1;
            int rounds = 0;
            foreach (LiveEffectBundle bundle in manager.EffectBundles)
            {
                if (bundle == null || bundle.liveEffects == null) continue;
                foreach (IEntityEffect effect in bundle.liveEffects)
                {
                    if (effect != null && effect.GetType() == typeof(Shield) && !effect.HasEnded &&
                        effect.RoundsRemaining > rounds) rounds = effect.RoundsRemaining;
                }
            }
            return rounds;
        }
    }
}
