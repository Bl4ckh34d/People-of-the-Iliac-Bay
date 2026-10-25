using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native scene acceptance for first inventory creation. The hook is
    // armed only by the fixture runner and observes before ordinary actor spawn.
    public static class NativeNpcStartingBelongingsSceneProbe
    {
        private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string SaveName="People of the Iliac Bay - starting belongings scene";
        private sealed class Selection
        {
            public string ResidentId;
            public int BuildingKey;
            public double At;
            public Resident Resident;
            public List<NpcStartingBelonging> Plan;
            public bool HasHealingPotion;
            public int HealingPotionRecipeKey;
            public ulong HealingPotionUid;
        }
        private sealed class Capture
        {
            public List<NpcInventoryItem> Items;
            public string ResidentId;
            public int BuildingKey;
            public bool BeforeSpawn,AfterInitialize;
            public string Failure;
        }
        private static Selection selection;
        private static Capture capture;
        private static bool armed;

        public static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-starting-belongings-scene-probe")>=0 &&
                character!=null && character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal),
                "Starting-belongings scene probe requires its explicit flag and owned character.");
            Require(host && streets,"Starting-belongings scene probe has no live adapter or town population.");
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            string source=null;WorldStateStore store=null;int fixturesFound=0;
            selection=null;capture=null;armed=true;
            foreach(string name in new[]{"People - Shop entrance","People of the Iliac Bay - furnished shop entrance","People - Daggerfall day"})
            {
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,name);
                if(slot<0)continue;
                fixturesFound++;
                bool loaded=false;SaveLoadManager.OnLoadEventHandler onLoad=data=>loaded=true;
                SaveLoadManager.OnLoad+=onLoad;
                try
                {
                    game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                    float deadline=Time.realtimeSinceStartup+180f;
                    while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline)yield return null;
                    if(!loaded || !streets.Ready || game.PlayerEntity.Name!=character)continue;
                }
                finally {SaveLoadManager.OnLoad-=onLoad;}
                store=LiveStore(streets);
                if(store==null)continue;
                selection=SelectExistingFutureShop(host,streets,store);
                if(selection!=null){source=name;break;}
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STARTING_BELONGINGS_SCENE_PREFLIGHT: save="+name+
                    "; eligibleExistingAdultShopVisits=0; reason=no absent-ledger unmaterialized adult with planned belongings and a real shop door");
            }
            Require(fixturesFound>0,"No owned existing shop/day fixture saves found for "+character+".");
            Require(selection!=null && store!=null,"Fixture preflight found no existing adult shop visit with an absent inventory ledger, no materialized actor, and non-empty deterministic belongings across the owned shop/day saves. No actor, route, or ledger was created.");
            Require(store.ReadNpcInventory(selection.ResidentId)==null && !streets.FindActor(selection.ResidentId),
                "Selected first-spawn resident already has an inventory ledger or materialized actor.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STARTING_BELONGINGS_SCENE_SELECTED: source="+source+"; resident="+selection.ResidentId+
                "; building="+selection.BuildingKey+"; plannedItems="+selection.Plan.Count+"; plannedHealingPotion="+
                selection.HasHealingPotion+"; ledger=absent; actor=absent");

            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors doorOwner=null;StaticDoor door=default(StaticDoor);int doorIndex=-1;
            Require(directory && FindDoor(directory,selection.BuildingKey,out doorOwner,out door,out doorIndex),
                "Selected scheduled shop has no existing native exterior door.");
            armed=true;
            double before=Now();double delta=selection.At-before;
            Require(delta>1d && delta<8d*86400d,"Selected saved shop visit is outside the bounded fixture clock window.");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)delta);
            yield return null;yield return null;

            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior();
                float exitDeadline=Time.realtimeSinceStartup+90f;
                while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<exitDeadline)yield return null;
            }
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready,"Could not reach the exterior before entering the saved shop visit.");
            // Use the saved visit's existing exterior door and DFU's ordinary entry
            // transition. The target resident is never created by the probe.
            TownSmokeHarness.EnterInterior(doorOwner.transform,door);DaggerfallUI.Instance.PopToHUD();
            float deadlineSpawn=Time.realtimeSinceStartup+120f;
            while(Time.realtimeSinceStartup<deadlineSpawn && (capture==null || (!capture.AfterInitialize && capture.Failure==null)))yield return null;
            Require(capture!=null && capture.AfterInitialize && capture.BeforeSpawn && capture.Failure==null,
                capture!=null && capture.Failure!=null ? capture.Failure : "Natural first actor initialization did not occur for selected resident.");
            float sceneDeadline=Time.realtimeSinceStartup+75f;ResidentActor actor=null;DaggerfallInterior room=null;
            while(Time.realtimeSinceStartup<sceneDeadline)
            {
                actor=streets.FindActor(selection.ResidentId);room=game.PlayerEnterExit.Interior;
                if(streets.Ready && room && room.EntryDoor.buildingKey==selection.BuildingKey && actor && actor.gameObject.activeInHierarchy)break;
                yield return null;
            }
            Require(room && room.EntryDoor.buildingKey==selection.BuildingKey && actor && actor.gameObject.activeInHierarchy,
                "Naturally spawned resident did not enter the selected shop's actual native interior.");
            Require(game.PlayerEnterExit.IsPlayerInsideOpenShop && PlayerActivate.IsBuildingOpen(room.BuildingData.BuildingType),
                "Selected saved shop visit did not resolve to an actually open native shop.");
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            Require(owner && owner.Binding!=null,"Naturally spawned resident has no native inventory binding.");
            var beforeDisk=CaptureNativeItems(owner.Binding.Items);
            AssertSameInventory(capture.Items,beforeDisk,"pre-save resident binding");
            var currentStore=LiveStore(streets);
            AssertSameInventory(capture.Items,currentStore.ReadNpcInventory(selection.ResidentId).Items,"pre-save world ledger");

            bool saved=false;SaveLoadManager.OnSaveEventHandler onSave=data=>saved=true;
            SaveLoadManager.OnSave+=onSave;
            try
            {
                game.SaveLoadManager.Save(character,SaveName);
                float saveDeadline=Time.realtimeSinceStartup+45f;
                while(!saved && Time.realtimeSinceStartup<saveDeadline)yield return null;
                Require(saved,"Disk save timed out for first-spawn inventory scene.");
            }
            finally {SaveLoadManager.OnSave-=onSave;}
            game.SaveLoadManager.EnumerateSaves();
            int saveSlot=game.SaveLoadManager.FindSaveFolderByNames(character,SaveName);
            Require(saveSlot>=0,"First-spawn disk save was not enumerated.");
            bool reloaded=false;SaveLoadManager.OnLoadEventHandler onReload=data=>reloaded=true;
            SaveLoadManager.OnLoad+=onReload;
            try
            {
                game.SaveLoadManager.Load(saveSlot);DaggerfallUI.Instance.PopToHUD();
                float reloadDeadline=Time.realtimeSinceStartup+120f;
                while((!reloaded || !streets.Ready) && Time.realtimeSinceStartup<reloadDeadline)yield return null;
                Require(reloaded && streets.Ready,"First-spawn disk reload timed out.");
            }
            finally {SaveLoadManager.OnLoad-=onReload;armed=false;}

            var restoredStore=LiveStore(streets);
            Require(restoredStore!=null,"No restored world store after first-spawn disk reload.");
            ResidentActor restoredActor=null;float actorDeadline=Time.realtimeSinceStartup+90f;
            while(Time.realtimeSinceStartup<actorDeadline)
            {
                restoredActor=streets.FindActor(selection.ResidentId);
                if(restoredActor && restoredActor.gameObject.activeInHierarchy)break;
                yield return null;
            }
            Require(restoredActor && restoredActor.gameObject.activeInHierarchy,"Saved shop visitor did not re-materialize after disk reload.");
            var restoredOwner=restoredActor.GetComponent<NativeNpcInventoryOwner>();
            Require(restoredOwner && restoredOwner.Binding!=null,"Reloaded visitor has no native inventory owner binding.");
            var afterDisk=CaptureNativeItems(restoredOwner.Binding.Items);
            AssertSameInventory(beforeDisk,afterDisk,"reloaded native binding");
            AssertSameInventory(beforeDisk,restoredStore.ReadNpcInventory(selection.ResidentId).Items,"reloaded world ledger");
            AssertHealingPotionState(restoredOwner.Binding.Items,selection,requireSameUid:true);
            armed=false;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STARTING_BELONGINGS_SCENE_PASS: resident="+selection.ResidentId+"; building="+selection.BuildingKey+
                "; sourceLedgerAbsentBeforeSpawn=true; naturalInitialize=true; plannedItems="+selection.Plan.Count+
                "; plannedHealingPotion="+selection.HasHealingPotion+"; healingPotionUid="+selection.HealingPotionUid+
                "; exactNativeUidPayloadDiskSaveLoad=true; noReloadRefill=true; fixture="+source+"; save="+SaveName);
        }

        // Called before the visitor prefab is instantiated. This is the evidence
        // point that the person's first inventory ledger is still absent.
        public static void BeforeNaturalSpawn(StreetPopulation streets,Resident resident,WorldStateStore store)
        {
            if(!armed || Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-starting-belongings-scene-probe")<0 ||
                selection==null || capture!=null || resident==null || resident.Id!=selection.ResidentId)return;
            var result=new Capture{ResidentId=resident.Id,BuildingKey=selection.BuildingKey};capture=result;
            if(!resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult || resident.IsQuestProtected || resident.NativeIdentity!=null)
            {result.Failure="Selected first-spawn resident lost ordinary adult eligibility before actor materialization.";return;}
            if(store==null || store.ReadNpcInventory(resident.Id)!=null)
            {result.Failure="Selected resident acquired an inventory ledger before its first native actor binding.";return;}
            if(streets.FindActor(resident.Id))
            {result.Failure="Selected resident materialized before the first-spawn observer ran.";return;}
            result.BeforeSpawn=true;
        }

        // Called immediately after the naturally scheduled actor's Initialize.
        public static void AfterNaturalInitialize(ResidentActor actor,WorldStateStore store)
        {
            if(!armed || Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-starting-belongings-scene-probe")<0 ||
                selection==null || capture==null || capture.Failure!=null || actor==null || actor.Resident==null ||
                actor.Resident.Id!=selection.ResidentId || capture.AfterInitialize)return;
            try
            {
                var owner=actor.GetComponent<NativeNpcInventoryOwner>();
                Require(owner && owner.Binding!=null,"Natural first bind did not attach a native inventory owner.");
                var items=CaptureNativeItems(owner.Binding.Items);
                int expectedCount=selection.Plan.Count+(selection.HasHealingPotion?1:0);
                Require(items.Count>=expectedCount,"Natural first bind did not materialize the deterministic planned items.");
                for(int i=0;i<selection.Plan.Count;i++)
                {
                    var planned=selection.Plan[i];var native=owner.Binding.Items.GetItem(i);
                    ItemGroups expectedGroup=planned.Group==NpcStartingItemGroup.UselessItems1?ItemGroups.UselessItems1:
                        planned.Group==NpcStartingItemGroup.UselessItems2?ItemGroups.UselessItems2:
                        planned.Group==NpcStartingItemGroup.MiscItems?ItemGroups.MiscItems:ItemGroups.Jewellery;
                    Require(native.ItemGroup==expectedGroup && native.TemplateIndex==planned.TemplateIndex && native.stackCount==planned.Count,
                        "Natural first bind differs from the pure deterministic item plan at item "+i+".");
                }
                if(selection.HasHealingPotion)
                {
                    var native=owner.Binding.Items.GetItem(selection.Plan.Count);
                    Require(native!=null && native.PotionRecipeKey==selection.HealingPotionRecipeKey && native.stackCount==1 &&
                        NativeNpcHealingItem.IsSupportedPotion(native),
                        "Natural first bind did not materialize exactly one broker-verified HealHealth potion.");
                    selection.HealingPotionUid=native.UID;
                }
                AssertHealingPotionState(owner.Binding.Items,selection,requireSameUid:false);
                Require(store!=null && store.ReadNpcInventory(selection.ResidentId)!=null,"Natural first bind did not persist the inventory ledger.");
                AssertSameInventory(items,store.ReadNpcInventory(selection.ResidentId).Items,"natural first-bind ledger");
                capture.Items=items;capture.AfterInitialize=true;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STARTING_BELONGINGS_FIRST_BIND: resident="+selection.ResidentId+"; beforeSpawnLedgerAbsent=true; items="+items.Count);
            }
            catch(Exception ex){capture.Failure=ex.Message;}
        }

        private static Selection SelectExistingFutureShop(PeopleOfTheIliacBayMod host,StreetPopulation streets,WorldStateStore store)
        {
            var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory)return null;
            var state=(WorldState)host.GetSaveData();
            var town=state.Routines.Find(x=>x.SettlementId==directory.MapID);
            var population=state.Settlements.Find(x=>x.SettlementId==directory.MapID);
            if(town==null || population==null)return null;
            double now=Now();double day=Math.Floor(now/86400d)+1;Selection best=null;float bestDistance=float.PositiveInfinity;
            foreach(var routine in town.Residents)
            {
                if(routine.Itinerary==null || state.ResidentTrips.Exists(t=>t.ResidentId==routine.ResidentId))continue;
                Resident person=null;
                foreach(var household in population.Households)foreach(var member in household.Residents)
                    if(member.Id==routine.ResidentId)person=member;
                if(person==null || !person.IsAlive || person.IsQuestProtected || person.NativeIdentity!=null ||
                    person.AgeGroup!=ResidentAgeGroup.Adult || store.ReadNpcInventory(person.Id)!=null || streets.FindActor(person.Id))continue;
                var progress=store.ReadNpcProgress(person.Id);
                var archetype=progress==null?NpcArchetype.Citizen:progress.Archetype;
                var plan=NpcStartingBelongings.Generate(store.WorldSeed,person.Id,archetype);
                bool hasHealingPotion=NpcStartingBelongings.ShouldStartWithHealingPotion(store.WorldSeed,person.Id,archetype);
                int recipeKey=0;
                hasHealingPotion=hasHealingPotion && NativeNpcHealingItem.TryFindHealingPotionRecipeKey(out recipeKey);
                if(plan.Count==0 && !hasHealingPotion)continue;
                IEnumerable<ResidentVisitSnapshot> visits;
                try {visits=ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds,routine.Itinerary).ScheduledVisits();}
                catch(ArgumentException){continue;}
                foreach(var visit in visits)
                {
                    BuildingSummary summary;
                    if(visit.Purpose!=ResidentVisitPurpose.Shop || visit.LeaveAt-visit.EnterAt<120 ||
                        !directory.GetBuildingSummary(visit.BuildingKey,out summary) || !RMBLayout.IsShop(summary.BuildingType))continue;
                    DaggerfallStaticDoors owner;StaticDoor door;int index;
                    if(!FindDoor(directory,visit.BuildingKey,out owner,out door,out index))continue;
                    var doorWorld=owner.GetDoorPosition(index);float distance=Vector3.Distance(GameManager.Instance.PlayerMotor.transform.position,doorWorld);
                    double at=day*86400d+visit.EnterAt+2d;
                    if(at<=now+30d || at-now>7*86400d)continue;
                    if(best!=null && distance>=bestDistance)continue;
                    bestDistance=distance;best=new Selection{ResidentId=person.Id,BuildingKey=visit.BuildingKey,At=at,Resident=person,
                        Plan=new List<NpcStartingBelonging>(plan),HasHealingPotion=hasHealingPotion,
                        HealingPotionRecipeKey=hasHealingPotion?recipeKey:0};
                }
            }
            return best;
        }

        private static WorldStateStore LiveStore(StreetPopulation streets)
        {return typeof(StreetPopulation).GetField("worldStore",PrivateInstance).GetValue(streets) as WorldStateStore;}
        private static bool FindDoor(BuildingDirectory directory,int building,out DaggerfallStaticDoors owner,out StaticDoor door,out int index)
        {
            owner=null;door=default(StaticDoor);index=-1;
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if(doors.Doors==null)continue;
                for(int i=0;i<doors.Doors.Length;i++)if(doors.Doors[i].doorType==DoorTypes.Building && doors.Doors[i].buildingKey==building)
                {owner=doors;door=doors.Doors[i];index=i;return true;}
            }
            return false;
        }
        private static List<NpcInventoryItem> CaptureNativeItems(ItemCollection items)
        {return NativeNpcInventoryBridge.CaptureItems(items);}
        private static void AssertHealingPotionState(ItemCollection items,Selection expected,bool requireSameUid)
        {
            int count=0;DaggerfallUnityItem found=null;
            for(int i=0;i<items.Count;i++)
            {
                var item=items.GetItem(i);
                if(item!=null && NativeNpcHealingItem.IsSupportedPotion(item)) {count++;found=item;}
            }
            int expectedCount=expected.HasHealingPotion?1:0;
            Require(count==expectedCount,"Native starting inventory has "+count+" supported healing potion(s), expected "+expectedCount+".");
            if(expected.HasHealingPotion)
            {
                Require(found!=null && found.PotionRecipeKey==expected.HealingPotionRecipeKey && found.stackCount==1,
                    "Native starting healing potion recipe or quantity changed.");
                if(requireSameUid)
                    Require(expected.HealingPotionUid!=0 && found.UID==expected.HealingPotionUid,
                        "Reload changed the exact starting healing potion UID.");
            }
        }
        private static void AssertSameInventory(IList<NpcInventoryItem> expected,IList<NpcInventoryItem> actual,string label)
        {
            Require(expected!=null && actual!=null && expected.Count==actual.Count,label+" item count changed.");
            var byUid=new Dictionary<ulong,string>();foreach(var item in expected)byUid.Add(item.Uid,item.NativePayload);
            foreach(var item in actual)
            {string payload;Require(byUid.TryGetValue(item.Uid,out payload) && payload==item.NativePayload,label+" native UID or exact payload changed: "+item.Uid);byUid.Remove(item.Uid);}
            Require(byUid.Count==0,label+" lost saved native items.");
        }
        private static double Now()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
