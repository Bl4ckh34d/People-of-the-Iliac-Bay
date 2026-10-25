using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Queues receipt-bound history for newly active poison on one resident.</summary>
    public sealed class NativeNpcPoisonOnsetBinding : MonoBehaviour
    {
        private WorldStateStore store;
        private Resident resident;
        private EntityEffectManager effectManager;
        private object writeEpoch;
        private bool bound;
        private bool subscribed;
        private readonly List<LiveEffectBundle> observedBundles = new List<LiveEffectBundle>();

        public static void BindTo(GameObject owner, WorldStateStore world, Resident person)
        {
            if (!owner || world == null || person == null ||
                !NpcVitalsBindingPolicy.Eligible(person.IsAlive, person.AgeGroup == ResidentAgeGroup.Adult, person.IsQuestProtected))
                return;

            EntityEffectManager manager = owner.GetComponent<EntityEffectManager>();
            if (!manager || manager.gameObject != owner)
                return;

            NativeNpcPoisonOnsetBinding binding = owner.GetComponent<NativeNpcPoisonOnsetBinding>();
            if (!binding) binding = owner.AddComponent<NativeNpcPoisonOnsetBinding>();
            binding.Bind(world, person, manager);
        }

        private void Bind(WorldStateStore world, Resident person, EntityEffectManager manager)
        {
            if (world == null || person == null || !manager)
                return;
            if (bound)
            {
                if (ReferenceEquals(store, world) && ReferenceEquals(resident, person) &&
                    ReferenceEquals(effectManager, manager) && world.IsNpcVitalsWriteEpochCurrent(writeEpoch))
                    return;
                // A save reload can reuse this component while replacing Core's
                // branch. Revoke old tickets and baseline the new native binding.
                Unsubscribe();
                bound = false;
            }

            store = world;
            resident = person;
            effectManager = manager;
            // Same opaque generation contract as resident vitals writes. Restore
            // invalidates callbacks retained by actors from the previous branch.
            writeEpoch = world.CaptureNpcVitalsWriteEpoch();
            bound = true;
            if (isActiveAndEnabled)
                SubscribeWithBaseline();
        }

        private void OnEnable()
        {
            if (bound)
                SubscribeWithBaseline();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            observedBundles.Clear();
            store = null;
            resident = null;
            effectManager = null;
            writeEpoch = null;
            bound = false;
        }

        private void SubscribeWithBaseline()
        {
            if (!bound || subscribed || !effectManager)
                return;

            observedBundles.Clear();
            LiveEffectBundle[] bundles = effectManager.EffectBundles;
            if (bundles != null)
            {
                foreach (LiveEffectBundle bundle in bundles)
                {
                    if (IsActivePoisonBundle(bundle))
                        observedBundles.Add(bundle);
                }
            }

            effectManager.OnAssignBundle -= OnAssignBundle;
            effectManager.OnRemoveBundle -= OnRemoveBundle;
            effectManager.OnAssignBundle += OnAssignBundle;
            effectManager.OnRemoveBundle += OnRemoveBundle;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (subscribed && effectManager)
            {
                effectManager.OnAssignBundle -= OnAssignBundle;
                effectManager.OnRemoveBundle -= OnRemoveBundle;
            }
            subscribed = false;
            if (store != null && resident != null)
                store.ReleaseAllNpcPoisonAssignments(writeEpoch, resident.Id, this);
            observedBundles.Clear();
        }

        private void OnAssignBundle(LiveEffectBundle bundle)
        {
            if (!CanObserve() || bundle == null || bundle.bundleType != BundleTypes.Poison ||
                !IsExactActiveBundle(bundle) || ContainsReference(observedBundles, bundle))
                return;

            PoisonEffect poison = FindLivePoison(bundle);
            if (poison == null || string.IsNullOrWhiteSpace(poison.Key))
                return;

            DaggerfallUnity dfu = DaggerfallUnity.Instance;
            if (!dfu || dfu.WorldTime == null)
                return;
            double observedGameTime = dfu.WorldTime.Now.ToSeconds();
            if (double.IsNaN(observedGameTime) || double.IsInfinity(observedGameTime) || observedGameTime < 0)
                return;

            NpcPoisonAssignmentTicket ticket = store.TryIssueNpcPoisonAssignment(
                writeEpoch, resident.Id, this, bundle);
            if (ticket == null) return;
            NpcExperienceEvent captured;
            NpcPoisonCaptureDisposition disposition = store.TryRecordNpcPoisonAssignment(
                ticket, poison.Key, observedGameTime, out captured);
            if (disposition == NpcPoisonCaptureDisposition.Queued ||
                disposition == NpcPoisonCaptureDisposition.Pending ||
                disposition == NpcPoisonCaptureDisposition.Degraded)
            {
                // All three states durably account for this exact native assignment.
                // A pending receipt is drained from the save, even if this actor streams out.
                observedBundles.Add(bundle);
            }
            else
                store.ReleaseNpcPoisonAssignment(writeEpoch, resident.Id, this, bundle);
        }

        private void OnRemoveBundle(LiveEffectBundle bundle)
        {
            // DFU's WipeAllBundles() reports a null bundle after clearing the
            // manager; this is a reset boundary, not a single-bundle removal.
            if (bundle == null)
            {
                if (store != null && resident != null)
                    store.ReleaseAllNpcPoisonAssignments(writeEpoch, resident.Id, this);
                observedBundles.Clear();
                return;
            }
            if (store != null && resident != null)
                store.ReleaseNpcPoisonAssignment(writeEpoch, resident.Id, this, bundle);
            RemoveReference(observedBundles, bundle);
        }

        private bool CanObserve()
        {
            DaggerfallEntityBehaviour entityBehaviour = effectManager ? effectManager.EntityBehaviour : null;
            DaggerfallEntity nativeEntity = entityBehaviour ? entityBehaviour.Entity : null;
            return bound && subscribed && isActiveAndEnabled && gameObject.activeInHierarchy &&
                store != null && store.IsNpcVitalsWriteEpochCurrent(writeEpoch) && effectManager &&
                entityBehaviour && nativeEntity != null && nativeEntity.CurrentHealth > 0 &&
                resident != null && NpcVitalsBindingPolicy.Eligible(resident.IsAlive,
                    resident.AgeGroup == ResidentAgeGroup.Adult, resident.IsQuestProtected) &&
                !string.IsNullOrWhiteSpace(resident.Id);
        }

        private bool IsExactActiveBundle(LiveEffectBundle candidate)
        {
            LiveEffectBundle[] bundles = effectManager.EffectBundles;
            if (bundles == null)
                return false;
            foreach (LiveEffectBundle active in bundles)
            {
                if (ReferenceEquals(active, candidate))
                    return true;
            }
            return false;
        }

        private static bool IsActivePoisonBundle(LiveEffectBundle bundle)
        {
            return bundle != null && bundle.bundleType == BundleTypes.Poison && FindLivePoison(bundle) != null;
        }

        private static PoisonEffect FindLivePoison(LiveEffectBundle bundle)
        {
            if (bundle == null || bundle.liveEffects == null)
                return null;
            foreach (IEntityEffect effect in bundle.liveEffects)
            {
                PoisonEffect poison = effect as PoisonEffect;
                if (poison != null && !poison.HasEnded)
                    return poison;
            }
            return null;
        }

        private static bool ContainsReference(List<LiveEffectBundle> bundles, LiveEffectBundle candidate)
        {
            for (int i = 0; i < bundles.Count; i++)
            {
                if (ReferenceEquals(bundles[i], candidate))
                    return true;
            }
            return false;
        }

        private static void RemoveReference(List<LiveEffectBundle> bundles, LiveEffectBundle candidate)
        {
            for (int i = bundles.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(bundles[i], candidate))
                    bundles.RemoveAt(i);
            }
        }
    }
}
