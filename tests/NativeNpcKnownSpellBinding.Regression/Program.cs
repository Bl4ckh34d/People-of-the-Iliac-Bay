using System;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using UnityEngine;

internal static class Program
{
    private static void Require(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }

    private static GameObject Body(Resident person)
    {
        var actor = new GameObject();
        actor.AddComponent<ResidentActor>().Resident = person;
        var body = actor.AddComponent<DaggerfallEntityBehaviour>();
        actor.AddComponent<EntityEffectManager>().EntityBehaviour = body;
        return actor;
    }

    private static void RestoredShieldKeepsOwnSkills()
    {
        var store = new WorldStateStore(42);
        var person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
        var first = Body(person);
        first.GetComponent<DaggerfallEntityBehaviour>().Entity.DeserializeSpellbook(new[] {
            new EffectBundleSettings { Name = "Shield", StandardSpellIndex = 17 } });
        // This is the starting-spell path's existing setting before capture.
        first.GetComponent<EntityEffectManager>().UsePlayerCharacterSkillsForEnemyMagicCost = false;
        var binding = NativeNpcKnownSpellBinding.BindTo(first, store, person);
        Require(binding != null && binding.Capture(), "Initial native Shield book was not captured.");
        var saved = store.ReadNpcProgress(person.Id).NativeSpellbook;
        var restoredStore = new WorldStateStore(99);
        restoredStore.Restore(store.Snapshot());
        var rebuilt = Body(person);
        binding = NativeNpcKnownSpellBinding.BindTo(rebuilt, restoredStore, person);
        Require(binding != null && binding.HasRestoredBook, "Rebuilt actor did not restore its native book.");
        var book = rebuilt.GetComponent<DaggerfallEntityBehaviour>().Entity.SerializeSpellbook();
        Require(book.Length == 1 && book[0].StandardSpellIndex == 17 && book[0].Name == "Shield", "Restore changed the exact known Shield.");
        Require(restoredStore.ReadNpcProgress(person.Id).NativeSpellbook == saved, "Restore rewrote the stored spellbook.");
        Require(!rebuilt.GetComponent<EntityEffectManager>().UsePlayerCharacterSkillsForEnemyMagicCost,
            "Rebuilt Shield caster reverted to player skills instead of its own native skills.");
    }

    private static void SuppliedBookUsesResidentSkills()
    {
        var store = new WorldStateStore(42);
        var person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
        var actor = Body(person);
        actor.GetComponent<DaggerfallEntityBehaviour>().Entity.DeserializeSpellbook(new[] {
            new EffectBundleSettings { Name = "Authored protection", StandardSpellIndex = 88 } });
        var binding = NativeNpcKnownSpellBinding.BindTo(actor, store, person);
        Require(binding != null && binding.HasRestoredBook, "Existing native book lost authority.");
        Require(actor.GetComponent<DaggerfallEntityBehaviour>().Entity.SerializeSpellbook()[0].Name == "Authored protection", "Existing book was replaced.");
        Require(!actor.GetComponent<EntityEffectManager>().UsePlayerCharacterSkillsForEnemyMagicCost, "Managed actor uses player skills for supplied spells.");
    }

    private static void ForeignBodiesRemainUnchanged()
    {
        foreach (string kind in new[] { "player", "wrong-manager", "wrong-resident", "quest", "unmanaged" })
        {
            var store = new WorldStateStore(42);
            var person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
            var actor = kind == "unmanaged" ? new GameObject() : Body(person);
            if (kind == "unmanaged")
            { actor.AddComponent<DaggerfallEntityBehaviour>(); actor.AddComponent<EntityEffectManager>().EntityBehaviour = actor.GetComponent<DaggerfallEntityBehaviour>(); }
            var manager = actor.GetComponent<EntityEffectManager>();
            if (kind == "player") manager.IsPlayerEntity = true;
            if (kind == "wrong-manager") manager.EntityBehaviour = Body(person).GetComponent<DaggerfallEntityBehaviour>();
            if (kind == "wrong-resident") actor.GetComponent<ResidentActor>().Resident = new Resident { Id = "other" };
            if (kind == "quest") person.IsQuestProtected = true;
            Require(NativeNpcKnownSpellBinding.BindTo(actor, store, person) == null, kind + " body admitted to resident spell binding.");
            Require(manager.UsePlayerCharacterSkillsForEnemyMagicCost, kind + " body's native cost policy was changed.");
            Require(store.ReadNpcProgress(person.Id) == null, kind + " body changed resident progress.");
        }
    }

    private static void FirstBookAllowsNextObservedMagickaSpend()
    {
        var store = new WorldStateStore(42);
        var person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
        var actor = Body(person);
        var archetype = store.ResolveNpcArchetype(person.Id);
        var before = store.ReadNpcVitals(person.Id, archetype);
        Require(!before.IsPersisted && before.Revision == 0 && before.CurrentMagicka > 0, "Fixture must start seed-only.");
        long samplerRevision = before.Revision;
        actor.AddComponent<NativeNpcVitalsBinding>().Rebaseline = () =>
        {
            var saved = store.ReadNpcVitals(person.Id, archetype);
            Require(saved.CurrentHealth == before.CurrentHealth && saved.CurrentMagicka == before.CurrentMagicka,
                "Book capture must not change actual resources.");
            samplerRevision = saved.Revision;
        };
        var binding = NativeNpcKnownSpellBinding.BindTo(actor, store, person);
        actor.GetComponent<DaggerfallEntityBehaviour>().Entity.DeserializeSpellbook(new[] {
            new EffectBundleSettings { Name = "Shield", StandardSpellIndex = 17 } });
        Require(binding != null && binding.Capture(), "First Shield book was not captured.");
        Require(store.ReadNpcVitals(person.Id, archetype).Revision == 1, "First book must initialize a saved owner.");
        Require(store.TryCaptureObservedNpcVitalsChange(store.CaptureNpcVitalsWriteEpoch(), person.Id, archetype,
            before.CurrentHealth, before.CurrentMagicka, before.CurrentHealth, before.CurrentMagicka - 1, 100, samplerRevision),
            "First native magicka spend was rejected after book initialized the saved vitals revision.");
        Require(store.ReadNpcVitals(person.Id, archetype).CurrentMagicka == before.CurrentMagicka - 1,
            "Observed magicka spend was not persisted exactly.");
    }

    private static int Main()
    {
        int failed = 0;
        foreach (var test in new (string Name, Action Run)[] {
            ("Shield book restore preserves NPC skill cost source", RestoredShieldKeepsOwnSkills),
            ("Supplied managed spellbook retains content and uses NPC skills", SuppliedBookUsesResidentSkills),
            ("First saved spellbook permits next observed magicka spend", FirstBookAllowsNextObservedMagickaSpend),
            ("Player quest mismatched and unmanaged bodies remain unchanged", ForeignBodiesRemainUnchanged) })
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        return failed == 0 ? 0 : 1;
    }
}
