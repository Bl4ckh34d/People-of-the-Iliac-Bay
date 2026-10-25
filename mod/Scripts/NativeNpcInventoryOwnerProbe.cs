using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Isolated native entity binding regression. No live player or resident is used.
    public static class NativeNpcInventoryOwnerProbe
    {
        public static string Run()
        {
            string startingBelongingsProof=VerifyStartingBelongings();
            string mageMagicProof=VerifyMageMagicBelongings();
            var store=new WorldStateStore(725);var town=store.GetOrCreate(15,new[]{10},1);string person=town.Households[0].Residents[0].Id;
            var items=new ItemCollection();
            var first=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);first.stackCount=3;items.AddItem(first,noStack:true);
            var second=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);second.stackCount=4;items.AddItem(second,noStack:true);
            var saber=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Saber);saber.currentCondition=37;items.AddItem(saber,noStack:true);
            HouseholdPropertyKeyItem.Register();var key=new HouseholdPropertyKeyItem(15,10);items.AddItem(key,noStack:true);
            Require(store.InitializeNpcInventory(person,81,NativeNpcInventoryBridge.CaptureItems(items)),"fixture inventory initialization failed");
            string expected=SaveLoadManager.Serialize(typeof(NpcInventoryState),store.ReadNpcInventory(person),false);
            // Exercise real Bind, not the bridge alone. DFU's Items setter clones
            // source items and allocates new UIDs; this regression must catch that.
            for(int branch=0;branch<2;branch++)
            {
                var go=new GameObject("People of the Iliac Bay isolated native owner probe");
                try
                {
                    var entity=new CivilianEntity(null);var native=entity.Items;
                    native.AddItem(ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Dagger));
                    var owner=go.AddComponent<NativeNpcInventoryOwner>();owner.Bind(entity,store,person);
                    Require(ReferenceEquals(native,entity.Items) && ReferenceEquals(owner.Binding.Items,native),"binding replaced the native collection");
                    Require(native.Count==4 && native.GetItem(first.UID)!=null && native.GetItem(first.UID).stackCount==3 &&
                        native.GetItem(second.UID)!=null && native.GetItem(second.UID).stackCount==4 &&
                        native.GetItem(saber.UID)!=null && native.GetItem(saber.UID).currentCondition==37 && native.GetItem(key.UID) is HouseholdPropertyKeyItem,
                        "entity binding changed saved UIDs, split stacks, condition or custom item type");
                    owner.Capture();
                    Require(SaveLoadManager.Serialize(typeof(NpcInventoryState),store.ReadNpcInventory(person),false)==expected,
                        "binding/capture changed saved UID payload, wallet or revision");
                }
                finally {UnityEngine.Object.DestroyImmediate(go);}
                var copy=new WorldStateStore(0);copy.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));store=copy;
            }
            return "PEOPLE_OF_THE_ILIAC_BAY_NPC_INVENTORY_OWNER_PASS native-entity-binding original-uids separate-stacks metadata custom-type capture branch-restore\n"+
                startingBelongingsProof+"\n"+mageMagicProof;
        }
        private static string VerifyStartingBelongings()
        {
            WorldStateStore store=null;Resident resident=null;IReadOnlyList<NpcStartingBelonging> plan=null;
            bool expectsPotion=false;int potionRecipeKey=0;
            for(uint seed=0;seed<500;seed++)
            {
                store=new WorldStateStore(seed);resident=store.GetOrCreate(725,new[]{10},1).Households[0].Residents[0];
                plan=NpcStartingBelongings.Generate(seed,resident.Id,NpcArchetype.Citizen);
                if(plan.Count>0)break;
            }
            Require(plan!=null && plan.Count>0,"Could not select non-empty deterministic starting belongings fixture.");
            expectsPotion=NpcStartingBelongings.ShouldStartWithHealingPotion(store.WorldSeed,resident.Id,NpcArchetype.Citizen) &&
                NativeNpcHealingItem.TryFindHealingPotionRecipeKey(out potionRecipeKey);
            var randomState=UnityEngine.Random.state;uint dfSeed=DFRandom.Seed;
            try
            {
                var entity=new CivilianEntity(null);var go=new GameObject("People of the Iliac Bay starting belongings owner probe");
                try
                {
                    // Probe the constructor path independently; all Unity fixture
                    // setup must precede the RNG checkpoint.
                    VerifyNativeCreationRandomState(store,resident.Id);
                    var owner=go.AddComponent<NativeNpcInventoryOwner>();
                    int expectedRandom=ExpectedNextUnityRandom();uint expectedDfSeed=DFRandom.Seed;
                    owner.Bind(entity,store,resident);
                    Require(entity.Items.Count>=plan.Count+(expectsPotion?1:0)+3,"Initial native inventory must contain utility, optional potion, home key and a complete outfit.");
                    bool wornClothing=false;
                    for(int index=0;index<entity.Items.Count;index++)
                    {
                        var wearable=entity.Items.GetItem(index);
                        if(wearable.ItemGroup!=ItemGroups.MensClothing && wearable.ItemGroup!=ItemGroups.WomensClothing)continue;
                        Require(wearable.IsEquipped && ReferenceEquals(entity.ItemEquipTable.GetItem(wearable.EquipSlot),wearable),
                            "Starting clothing must occupy its native equipment slot and be excluded from trade.");
                        wornClothing=true;
                    }
                    Require(wornClothing,"Starting clothing regression requires an actual wearable.");
                    var uids=new System.Collections.Generic.HashSet<ulong>();
                    for(int i=0;i<plan.Count;i++)
                    {
                        var item=entity.Items.GetItem(i);var expected=plan[i];
                        var expectedGroup=expected.Group==NpcStartingItemGroup.UselessItems1?ItemGroups.UselessItems1:
                            expected.Group==NpcStartingItemGroup.UselessItems2?ItemGroups.UselessItems2:
                            expected.Group==NpcStartingItemGroup.MiscItems?ItemGroups.MiscItems:ItemGroups.Jewellery;
                        Require(item.ItemGroup==expectedGroup && item.TemplateIndex==expected.TemplateIndex && item.stackCount==expected.Count,
                            "Native starting item differs from deterministic plan.");
                        Require(item.UID!=0 && uids.Add(item.UID),"Native starting item UID is empty or duplicated.");
                    }
                    ulong potionUid=0;
                    if(expectsPotion)
                    {
                        var potion=entity.Items.GetItem(plan.Count);
                        Require(potion!=null && potion.PotionRecipeKey==potionRecipeKey && potion.stackCount==1 &&
                            NativeNpcHealingItem.IsSupportedPotion(potion),"Initial native inventory lacks its one broker-verified HealHealth potion.");
                        Require(potion.UID!=0 && uids.Add(potion.UID),"Starting potion UID is empty or duplicated.");
                        potionUid=potion.UID;
                    }
                    AssertStartingPotion(entity.Items,expectsPotion,potionRecipeKey,potionUid);
                    Require(HasHomeKey(entity.Items,725,resident.HomeKey),"First binding did not persist the resident's existing household key.");
                    int actualRandom=UnityEngine.Random.Range(0,1000000);
                    Require(actualRandom==expectedRandom,"Native inventory binding changed Unity random state (expected next="+
                        expectedRandom+", actual="+actualRandom+").");
                    Require(DFRandom.Seed==expectedDfSeed,"Native inventory binding changed Daggerfall random seed (before="+
                        expectedDfSeed+", after="+DFRandom.Seed+").");
                    owner.Capture();
                    for(int i=entity.Items.Count-1;i>=0;i--)entity.Items.RemoveItem(entity.Items.GetItem(i));
                    owner.Capture();
                    Require(store.ReadNpcInventory(resident.Id).Items.Count==0,"Inventory loss was not saved as an empty authoritative ledger.");
                    var saved=store.Snapshot();var restored=new WorldStateStore(0);
                    restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),saved)));
                    var restoredEntity=new CivilianEntity(null);var restoredGo=new GameObject("People of the Iliac Bay starting belongings restore probe");
                    try
                    {
                        restoredGo.AddComponent<NativeNpcInventoryOwner>().Bind(restoredEntity,restored,resident);
                        Require(restoredEntity.Items.Count==0 && restored.ReadNpcInventory(resident.Id).Items.Count==0,
                            "Saved item loss rerolled starting belongings after native save/restore.");
                    }
                    finally {UnityEngine.Object.DestroyImmediate(restoredGo);}
                }
                finally {UnityEngine.Object.DestroyImmediate(go);}
            }
            finally {UnityEngine.Random.state=randomState;DFRandom.Seed=dfSeed;}

            var existingStore=new WorldStateStore(892);var existingResident=existingStore.GetOrCreate(725,new[]{10},1).Households[0].Residents[0];
            var existingItems=new ItemCollection();var owned=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Dagger);existingItems.AddItem(owned,noStack:true);
            Require(existingStore.InitializeNpcInventory(existingResident.Id,0,NativeNpcInventoryBridge.CaptureItems(existingItems)),
                "Existing no-key inventory fixture failed to initialize.");
            var existingEntity=new CivilianEntity(null);
            var existingGo=new GameObject("People of the Iliac Bay existing native inventory probe");
            try
            {
                var existingOwner=existingGo.AddComponent<NativeNpcInventoryOwner>();existingOwner.Bind(existingEntity,existingStore,existingResident);
                Require(existingEntity.Items.Count==1 && existingEntity.Items.GetItem(owned.UID)!=null &&
                    CountHomeKeys(existingEntity.Items,725,existingResident.HomeKey)==0,
                    "Existing saved owner was refilled with a home key.");
                existingOwner.Capture();
                Require(existingStore.ReadNpcInventory(existingResident.Id).Items.Count==1,
                    "Existing saved owner changed during binding.");
            }
            finally {UnityEngine.Object.DestroyImmediate(existingGo);}

            var firstLedgerStore=new WorldStateStore(894);var firstLedgerResident=firstLedgerStore.GetOrCreate(725,new[]{10},1).Households[0].Residents[0];
            var firstLedgerEntity=new CivilianEntity(null);var firstLedgerOwned=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Dagger);firstLedgerEntity.Items.AddItem(firstLedgerOwned,noStack:true);
            var firstLedgerGo=new GameObject("People of the Iliac Bay first native inventory ledger probe");
            try
            {
                var firstLedgerOwner=firstLedgerGo.AddComponent<NativeNpcInventoryOwner>();firstLedgerOwner.Bind(firstLedgerEntity,firstLedgerStore,firstLedgerResident);
                Require(firstLedgerEntity.Items.Count==2 && firstLedgerEntity.Items.GetItem(firstLedgerOwned.UID)!=null &&
                    CountHomeKeys(firstLedgerEntity.Items,725,firstLedgerResident.HomeKey)==1,
                    "First ledger with existing native items lacks its verified home key.");
                firstLedgerOwner.Capture();
                Require(firstLedgerStore.ReadNpcInventory(firstLedgerResident.Id).Items.Count==2,
                    "First ledger did not persist its verified home key.");
            }
            finally {UnityEngine.Object.DestroyImmediate(firstLedgerGo);}

            var ineligibleStore=new WorldStateStore(893);var ineligibleResident=ineligibleStore.GetOrCreate(725,new[]{10},1).Households[0].Residents[0];
            ineligibleResident.AgeGroup=ResidentAgeGroup.Child;
            var ineligibleEntity=new CivilianEntity(null);var ineligibleGo=new GameObject("People of the Iliac Bay ineligible starting belongings probe");
            try
            {
                ineligibleGo.AddComponent<NativeNpcInventoryOwner>().Bind(ineligibleEntity,ineligibleStore,ineligibleResident);
                Require(ineligibleEntity.Items.Count==0,"Child resident received adult starting belongings.");
            }
            finally {UnityEngine.Object.DestroyImmediate(ineligibleGo);}
            return "PEOPLE_OF_THE_ILIAC_BAY_NPC_STARTING_BELONGINGS_PASS native-template-items optional-verified-healing-potion deterministic-profile native-uids rng-isolated existing-items-preserved empty-ledger-save-restore-loss-no-refill adult-only";
        }
        private static string VerifyMageMagicBelongings()
        {
            WorldStateStore store=null;string personId=null;
            for(uint seed=0;seed<100000;seed++)
            {
                var candidateStore=new WorldStateStore(seed);
                string candidate=candidateStore.GetOrCreate(725,new[]{10},1).Households[0].Residents[0].Id;
                if(!NpcStartingBelongings.ShouldStartWithMagicItem(seed,candidate,NpcArchetype.Mage))continue;
                store=candidateStore;personId=candidate;break;
            }
            Require(store!=null,"Could not select a deterministic rare mage-item fixture.");
            var randomState=UnityEngine.Random.state;uint dfSeed=DFRandom.Seed;
            bool createdGame=UnityEngine.Object.FindObjectOfType<GameManager>()==null;
            var game=GameManager.Instance;
            var playerField=typeof(GameManager).GetField("playerEntity",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var priorPlayer=(PlayerEntity)playerField.GetValue(game);
            try
            {
                // Native ApplyWeaponMaterial reads the player's paperdoll gender,
                // even when creating an NPC weapon. Editor verification has no
                // loaded player, so provide that native dependency for this test.
                if(priorPlayer==null)game.PlayerEntity=new PlayerEntity(null);
                int expectedRandom=ExpectedNextUnityRandom();uint expectedDfSeed=DFRandom.Seed;
                var items=NativeStaticNpcStartingBelongings.Create(store,personId,NpcArchetype.Mage,Genders.Male,Races.Breton);
                DaggerfallUnityItem magic=null;
                for(int i=0;i<items.Count;i++)if(items.GetItem(i).IsEnchanted){magic=items.GetItem(i);break;}
                Require(magic!=null && !magic.IsArtifact && !magic.IsQuestItem,
                    "Rare static mage starting belongings lacks its regular non-quest enchanted item.");
                int actualRandom=UnityEngine.Random.Range(0,1000000);
                Require(actualRandom==expectedRandom,"Rare mage item creation changed Unity random state.");
                Require(DFRandom.Seed==expectedDfSeed,"Rare mage item creation changed Daggerfall random seed.");
                NativeNpcInventoryBinding binding;string reason;
                Require(NativeNpcInventoryBridge.TryOpen(store,personId,items,0,out binding,out reason),reason);
                string expected=SaveLoadManager.Serialize(typeof(NpcInventoryState),store.ReadNpcInventory(personId),false);
                var restored=new WorldStateStore(0);
                restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                Require(SaveLoadManager.Serialize(typeof(NpcInventoryState),restored.ReadNpcInventory(personId),false)==expected,
                    "Rare mage item native UID payload changed across WorldState serialization.");
                while(binding.Items.Count>0)binding.Items.RemoveItem(binding.Items.GetItem(0));
                Require(NativeNpcInventoryBridge.TryCapture(store,binding,out reason),reason);
                var lost=new WorldStateStore(0);
                lost.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                NativeNpcInventoryBinding reopened;
                Require(NativeNpcInventoryBridge.TryOpen(lost,personId,items,0,out reopened,out reason) && reopened.Items.Count==0,
                    "Saved rare mage item loss refilled after WorldState restore.");
                return "PEOPLE_OF_THE_ILIAC_BAY_RARE_MAGE_ITEM_PASS static-role enchanted-nonquest rng-isolated uid-serialize loss-no-refill";
            }
            finally
            {
                game.PlayerEntity=priorPlayer;
                if(createdGame)UnityEngine.Object.DestroyImmediate(game.gameObject);
                UnityEngine.Random.state=randomState;DFRandom.Seed=dfSeed;
            }
        }
        private static void AssertStartingPotion(ItemCollection items,bool expected,int recipeKey,ulong expectedUid)
        {
            int found=0;
            for(int i=0;i<items.Count;i++)
            {
                var item=items.GetItem(i);
                if(item==null || !NativeNpcHealingItem.IsSupportedPotion(item))continue;
                found++;
                Require(expected && item.PotionRecipeKey==recipeKey && item.stackCount==1 &&
                    (expectedUid==0 || item.UID==expectedUid),"Native starting inventory has an unexpected or changed healing potion.");
            }
            Require(found==(expected?1:0),"Native starting healing potion count differs from the deterministic, broker-supported choice.");
        }
        private static bool HasHomeKey(ItemCollection items,int settlement,int home)
        {
            for(int i=0;i<items.Count;i++)if(HouseholdPropertyKeyItem.IsMatching(items.GetItem(i),settlement,home))return true;
            return false;
        }
        private static int CountHomeKeys(ItemCollection items,int settlement,int home)
        {
            int count=0;
            for(int i=0;i<items.Count;i++)if(HouseholdPropertyKeyItem.IsMatching(items.GetItem(i),settlement,home))count++;
            return count;
        }
        private static int ExpectedNextUnityRandom()
        {
            var state=UnityEngine.Random.state;
            int expected=UnityEngine.Random.Range(0,1000000);
            UnityEngine.Random.state=state;
            return expected;
        }
        private static void VerifyNativeCreationRandomState(WorldStateStore store,string personId)
        {
            var state=UnityEngine.Random.state;int expected=ExpectedNextUnityRandom();uint dfSeed=DFRandom.Seed;
            var items=NativeNpcStartingBelongings.CreateInitialItems(store,personId);
            var resident=store.ResolveResident(personId);int settlement;
            if(resident!=null && NpcStartingBelongings.TryResolveVerifiedResidentHome(store.Snapshot(),resident,out settlement))
            {
                Require(HasHomeKey(items,settlement,resident.HomeKey),"First offscreen inventory omitted its verified home key.");
                bool clothing=false;
                for(int i=0;i<items.Count;i++)
                    if(items.GetItem(i).ItemGroup==(resident.Sex==ResidentSex.Female?ItemGroups.WomensClothing:ItemGroups.MensClothing))clothing=true;
                Require(clothing,"First offscreen inventory omitted its native gender-appropriate clothing.");
            }
            int actual=UnityEngine.Random.Range(0,1000000);
            Require(actual==expected,"Native starting-item creation changed Unity random state (expected next="+expected+", actual="+actual+").");
            Require(DFRandom.Seed==dfSeed,"Native starting-item creation changed Daggerfall random seed (before="+dfSeed+", after="+DFRandom.Seed+").");
            UnityEngine.Random.state=state;
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
