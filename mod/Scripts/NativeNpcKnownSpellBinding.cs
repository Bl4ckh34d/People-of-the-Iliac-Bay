using System;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Persists only the exact native spellbook already owned by an ordinary
    // managed actor. It never synthesizes or teaches spells; old save rows
    // still use NativeNpcStartingSpells as their deterministic fallback.
    public sealed class NativeNpcKnownSpellBinding : MonoBehaviour
    {
        private const int MaximumSpellbookEntries = 128;
        private WorldStateStore store;
        private Resident resident;
        private DaggerfallEntity entity;
        private object epoch;
        private string lastPacket;

        public bool HasRestoredBook { get; private set; }

        public static NativeNpcKnownSpellBinding BindTo(GameObject actor, WorldStateStore world, Resident person)
        {
            if (!actor || world == null || person == null || !person.IsAlive ||
                person.AgeGroup != ResidentAgeGroup.Adult || person.IsQuestProtected) return null;
            var entity = actor.GetComponent<DaggerfallEntityBehaviour>();
            var resident = actor.GetComponent<ResidentActor>();
            var manager = actor.GetComponent<EntityEffectManager>();
            if (!entity || entity.Entity == null || !resident || resident.Resident == null ||
                resident.Resident.Id != person.Id || !manager || manager.IsPlayerEntity ||
                !ReferenceEquals(manager.EntityBehaviour, entity)) return null;
            // A rebuilt manager defaults to player skill costs. Restored books bypass
            // starting-spell initialization, so establish the resident's cost source here.
            manager.UsePlayerCharacterSkillsForEnemyMagicCost = false;
            var binding = actor.GetComponent<NativeNpcKnownSpellBinding>();
            if (!binding) binding = actor.AddComponent<NativeNpcKnownSpellBinding>();
            binding.Bind(entity.Entity, world, person);
            return binding;
        }

        private void Bind(DaggerfallEntity nativeEntity, WorldStateStore world, Resident person)
        {
            if (entity != null) return;
            store = world;
            resident = person;
            entity = nativeEntity;
            epoch = world.CaptureNpcVitalsWriteEpoch();
            var progress = world.ReadNpcProgress(person.Id);
            lastPacket = progress == null ? null : progress.NativeSpellbook;

            // A supplied/quest-authored book remains native authority. This
            // adapter is only for bodies that would otherwise be rebuilt empty.
            if (entity.SpellbookCount() != 0)
            {
                HasRestoredBook = true;
                Capture();
                return;
            }
            Restore();
        }

        // Kept callable by the bounded native probe after it clears a body to
        // model reconstruction. Normal runtime restore remains bind-time only.
        public bool Restore()
        {
            if (entity == null || entity.SpellbookCount() != 0) return false;
            EffectBundleSettings[] book;
            if (!TryRead(lastPacket, out book)) return false;
            entity.DeserializeSpellbook(book);
            HasRestoredBook = true;
            return true;
        }

        public bool Capture()
        {
            if (store == null || resident == null || entity == null || !resident.IsAlive ||
                resident.AgeGroup != ResidentAgeGroup.Adult || resident.IsQuestProtected ||
                !store.IsNpcVitalsWriteEpochCurrent(epoch)) return false;
            var book = entity.SerializeSpellbook();
            if (book == null || book.Length > MaximumSpellbookEntries) return false;
            // Do not allocate progression rows for ordinary noncasters. A saved
            // empty packet, however, is deliberate and remains authoritative.
            if (book.Length == 0 && string.IsNullOrEmpty(lastPacket)) return true;
            string packet = SaveLoadManager.Serialize(typeof(EffectBundleSettings[]), book, false);
            if (packet == lastPacket) return true;
            if (!store.TryCaptureNpcKnownSpellbook(epoch, resident.Id, packet)) return false;
            lastPacket = packet;
            // A first saved book initializes seed-only vitals at revision one.
            // Adopt that unchanged baseline before the next observed resource spend.
            var vitals = GetComponent<NativeNpcVitalsBinding>();
            if (vitals) vitals.RebaselineAfterExternalCommit();
            return true;
        }

        private static bool TryRead(string packet, out EffectBundleSettings[] book)
        {
            book = null;
            if (string.IsNullOrWhiteSpace(packet)) return false;
            try { book = (EffectBundleSettings[])SaveLoadManager.Deserialize(typeof(EffectBundleSettings[]), packet); }
            catch { return false; }
            return book != null && book.Length <= MaximumSpellbookEntries;
        }
    }
}
