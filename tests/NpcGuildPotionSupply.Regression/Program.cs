using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const int Settlement = 41;
    private const int PatientHome = 12;
    private const int ProviderWork = 88;
    private const NpcArchetype Archetype = NpcArchetype.Citizen;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("potion offers require persisted eligible injury and affordable wallets", OfferEligibility),
            ("native recipe offers require no provider stock or day generation", StocklessOffers),
            ("native recipe IDs preserve signed recipe keys and reject invalid encodings", RecipeIdentity)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void OfferEligibility()
    {
        Fixture f = NewFixture();
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "full health cannot create a potion offer");
        NpcVitalsView initial = f.Store.ReadNpcVitals(f.PatientId, Archetype);
        int aboveThreshold = Math.Max(1, (int)((long)initial.MaximumHealth * 3 / 4) + 1);
        CaptureHealth(f.Store, f.PatientId, aboveThreshold, 90);
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "injury above care threshold cannot create a potion offer");

        int threshold = (int)((long)initial.MaximumHealth * 3 / 4);
        CaptureHealth(f.Store, f.PatientId, Math.Max(1, threshold), 95);
        True(NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "persisted injury at care threshold can create an offer");
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 1000, 100),
            "buyer wallet must cover quoted price");
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 94),
            "future vitals cannot authorize a purchase");

        f.Store.ResolveResident(f.PatientId).IsQuestProtected = true;
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "quest-protected patient cannot create an offer");
        f.Store.ResolveResident(f.PatientId).IsQuestProtected = false;
        f.Store.ResolveResident(f.PatientId).AgeGroup = ResidentAgeGroup.Child;
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "child cannot create this adult care offer");

        Fixture dead = NewFixture();
        CaptureHealth(dead.Store, dead.PatientId, dead.Store.ReadNpcVitals(dead.PatientId, Archetype).MaximumHealth / 2, 90);
        Population.MarkDead(dead.Store.GetOrCreate(Settlement, new[] { PatientHome }, 1), dead.PatientId);
        True(!NpcGuildPotionSupply.CanOffer(dead.Store, dead.PatientId, Archetype, dead.ProviderId, 10, 100),
            "dead patient cannot create an offer");
        True(!NpcGuildPotionSupply.CanOffer(f.Store, "unknown-resident", Archetype, f.ProviderId, 10, 100),
            "unknown patient cannot create an offer");
    }

    private static void StocklessOffers()
    {
        var f=NewFixture();Injure(f);
        for(int index=0;index<20;index++)
        {
            NpcGuildPotionOffer offer;
            True(NpcGuildPotionSupply.TryCreateOffer(f.Store,f.PatientId,Archetype,f.ProviderId,42,10,100+index*86400d,out offer),
                "a native recipe offer cannot exhaust synthetic stock");
            Equal(42UL,offer.RecipeOfferId);Equal(10,offer.Price);
        }
        Equal(0,f.Store.ReadNpcInventory(f.ProviderId).Items.Count);
        Equal(0L,f.Store.ReadNpcInventory(f.ProviderId).Revision);
        Equal(50,f.Store.ReadNpcInventory(f.ProviderId).WalletGold);
        Equal(200,f.Store.ReadNpcInventory(f.PatientId).WalletGold);
        var restored=new WorldStateStore(0);restored.Restore(f.Store.Snapshot());
        NpcGuildPotionOffer loaded;
        True(NpcGuildPotionSupply.TryCreateOffer(restored,f.PatientId,Archetype,f.ProviderId,42,10,100,out loaded));
        Equal(42UL,loaded.RecipeOfferId);
        True(!NpcGuildPotionSupply.TryCreateOffer(f.Store,f.PatientId,Archetype,f.ProviderId,42,1000,100,out loaded));
        True(!NpcGuildPotionSupply.TryCreateOffer(f.Store,f.PatientId,Archetype,"missing",42,10,100,out loaded));
    }

    private static void RecipeIdentity()
    {
        int key;
        True(NpcGuildPotionSupply.TryRecipeKey(NpcGuildPotionSupply.RecipeOfferId(-123),out key));Equal(-123,key);
        True(NpcGuildPotionSupply.TryRecipeKey(NpcGuildPotionSupply.RecipeOfferId(int.MinValue),out key));Equal(int.MinValue,key);
        True(NpcGuildPotionSupply.TryRecipeKey(NpcGuildPotionSupply.RecipeOfferId(int.MaxValue),out key));Equal(int.MaxValue,key);
        True(!NpcGuildPotionSupply.TryRecipeKey(0,out key));
        True(!NpcGuildPotionSupply.TryRecipeKey((ulong)uint.MaxValue+1,out key));
    }

    private static void Injure(Fixture f)
    {
        NpcVitalsView vitals = f.Store.ReadNpcVitals(f.PatientId, Archetype);
        CaptureHealth(f.Store, f.PatientId, Math.Max(1, (int)((long)vitals.MaximumHealth * 3 / 4)), 90);
    }

    private static void CaptureHealth(WorldStateStore store, string personId, int health, double now)
    {
        NpcVitalsView before = store.ReadNpcVitals(personId, Archetype);
        True(store.TryCaptureNpcVitals(personId, Archetype, health, before.CurrentMagicka, now, before.Revision),
            "native vitals capture should accept current fixture sample");
    }

    private static Fixture NewFixture()
    {
        var store = new WorldStateStore(123);
        store.GetOrCreate(Settlement, new[] { PatientHome }, 1);
        string patientId = store.Snapshot().Settlements[0].Households[0].Residents[0].Id;
        NativePerson provider = store.RegisterNativePerson(Settlement, ProviderWork, 600, "Guild Healer",
            ResidentSex.Female, 1, 88, 182, 1);
        True(store.InitializeNpcInventory(patientId, 200, Array.Empty<NpcInventoryItem>()), "buyer inventory should initialize");
        True(store.InitializeNpcInventory(provider.Id, 50, Array.Empty<NpcInventoryItem>()), "provider inventory should initialize");
        return new Fixture { Store = store, PatientId = patientId, Provider = provider, ProviderId = provider.Id };
    }

    private sealed class Fixture
    {
        public WorldStateStore Store;
        public string PatientId, ProviderId;
        public NativePerson Provider;
    }

    private static void True(bool value, string message = "assertion failed")
    { if (!value) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException("expected " + expected + ", got " + actual);
    }
}
