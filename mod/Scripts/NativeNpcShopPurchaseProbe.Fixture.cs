using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
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
    public static partial class NativeNpcShopPurchaseProbe
    {
        private const BindingFlags FixturePrivate=BindingFlags.Instance|BindingFlags.NonPublic;
        private static double FixtureNow()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}

        // Callable by TownSmokeHarness after its general process/character guard.
        // Loads existing disposable saves only. Never writes a save, creates an
        // actor, invents an itinerary, or inserts stock into a shop collection.
        public static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character,bool restartPreparation=false,
            bool taskFailureProbe=false,bool routineRecoveryPhysicalProbe=false,bool routineRecoveryDayProbe=false,bool lootSaleProbe=false)
        {
            Require((Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-probe")>=0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-loot-sale-probe")>=0) &&
                character!=null && character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal),"Shop fixture requires its explicit flag and owned character.");
            Require(!taskFailureProbe || Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-task-failure-probe")>=0,
                "Task failure fixture requires its explicit Player flag.");
            Require(!routineRecoveryPhysicalProbe || Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-routine-recovery-physical-probe")>=0,
                "Physical routine recovery fixture requires its explicit Player flag.");
            Require(!routineRecoveryDayProbe || routineRecoveryPhysicalProbe &&
                Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-routine-recovery-day-probe")>=0,
                "Recovery day fixture requires the physical recovery fixture and its explicit Player flag.");
            Require(!taskFailureProbe || !routineRecoveryPhysicalProbe,"Choose one native shop task fixture.");
            Require(!lootSaleProbe || Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-loot-sale-probe")>=0,
                "Loot-sale fixture requires its explicit Player flag.");
            Require(!lootSaleProbe || !restartPreparation && !taskFailureProbe && !routineRecoveryPhysicalProbe,
                "Loot-sale fixture cannot combine with another shop fixture mode.");
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=-1;string fixture=null;
            foreach(string name in new[]{"People of the Iliac Bay - furnished shop entrance","People - Shop entrance","People - Daggerfall day"})
            {slot=game.SaveLoadManager.FindSaveFolderByNames(character,name);if(slot>=0){fixture=name;break;}}
            Require(slot>=0,"No owned shop/day fixture save exists for "+character+".");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler onLoad=data=>loaded=true;
            SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                float deadline=Time.realtimeSinceStartup+180;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                Require(loaded && streets.Ready && game.PlayerEntity.Name==character,"Owned shop fixture did not load.");
            }
            finally {SaveLoadManager.OnLoad-=onLoad;}
            var motor=game.PlayerMotor;var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller && controller.enabled;
            motor.enabled=false;if(controller) controller.enabled=false;
            game.PlayerEntity.GodMode=true;
            try {yield return RunLoadedFixture(host,streets,character,fixture,restartPreparation,taskFailureProbe,routineRecoveryPhysicalProbe,routineRecoveryDayProbe,lootSaleProbe);}
            finally {if(controller) controller.enabled=controllerEnabled;motor.enabled=motorEnabled;}
        }

        private static IEnumerator RunLoadedFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character,string fixture,
            bool restartPreparation,bool taskFailureProbe,bool routineRecoveryPhysicalProbe,bool routineRecoveryDayProbe,bool lootSaleProbe)
        {
            var game=GameManager.Instance;
            Require(Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale-2f)<0.001f,"Shop commerce physical acceptance requires normal 2x clock.");
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior();
                float until=Time.realtimeSinceStartup+90;
                while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<until) yield return null;
            }
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready,"Shop fixture could not establish its exterior.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var saved=(WorldState)host.GetSaveData();
            var town=saved.Routines.Find(t=>t.SettlementId==directory.MapID);
            var population=saved.Settlements.Find(t=>t.SettlementId==directory.MapID);
            Require(town!=null && population!=null,"Shop fixture has no saved local schedules.");
            var recoveryStore=routineRecoveryPhysicalProbe?
                (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",FixturePrivate).GetValue(streets):null;
            Require(!routineRecoveryPhysicalProbe || recoveryStore!=null,"Physical recovery fixture has no live saved-state store.");
            var saleStore=lootSaleProbe?
                (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",FixturePrivate).GetValue(streets):null;
            Require(!lootSaleProbe || saleStore!=null,"Loot-sale fixture has no live saved-state store.");
            int recoveryDay=(int)Math.Floor(FixtureNow()/86400d)+1;
            Resident selected=null;int building=-1,sourceStopIndex=-1;double visitAt=0,best=double.MinValue;
            foreach(var routine in town.Residents)
            {
                if(routine.Itinerary==null || saved.ResidentTrips.Exists(t=>t.ResidentId==routine.ResidentId)) continue;
                Resident person=null;
                foreach(var home in population.Households) foreach(var member in home.Residents)
                    if(member.Id==routine.ResidentId && member.IsAlive && !member.IsQuestProtected && member.NativeIdentity==null &&
                        member.AgeGroup==ResidentAgeGroup.Adult) person=member;
                if(person==null) continue;
                if(lootSaleProbe)
                {
                    // Select a normal visitor whose first native binding will materialize
                    // existing deterministic belongings. Do not insert any sale fixture item.
                    var plan=NpcStartingBelongings.Generate(saleStore.WorldSeed,person.Id,saleStore.ResolveNpcArchetype(person.Id));
                    if(saleStore.ReadNpcInventory(person.Id)!=null || plan.Count==0) continue;
                }
                foreach(var visit in ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds,routine.Itinerary).ScheduledVisits())
                {
                    BuildingSummary summary;
                    if((restartPreparation && visit.BuildingKey!=131343) || visit.Purpose!=ResidentVisitPurpose.Shop || visit.LeaveAt-visit.EnterAt<600 ||
                        !directory.GetBuildingSummary(visit.BuildingKey,out summary) || !RMBLayout.IsShop(summary.BuildingType)) continue;
                    if(routineRecoveryPhysicalProbe)
                    {
                        double afterExpiry=recoveryDay*86400d+visit.LeaveAt+120d;
                        var ordinary=DailySchedule.ItineraryDay(person.HomeKey,routine.Itinerary,
                            routine.ItineraryDepartureSeconds,0,1439);
                        RoutineRecoveryPlan forecast;string forecastReason;
                        bool feasible=RoutineRecoveryPlanner.TryCreate(directory.MapID,person,routine,ordinary,
                            recoveryStore.AppointmentsFor(person.Id),recoveryDay,afterExpiry,visit.BuildingKey,afterExpiry,
                            0,1439,DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed),
                            out forecast,out forecastReason);
                        if(!feasible || !HasLaterOptionalRecoveryLeg(forecast,visit.VisitIndex))continue;
                    }
                    double score=visit.LeaveAt-visit.EnterAt+(summary.BuildingType==DFLocation.BuildingTypes.GeneralStore?100000:0);
                    if(score<=best) continue;
                    best=score;selected=person;building=visit.BuildingKey;sourceStopIndex=visit.VisitIndex;
                    visitAt=routineRecoveryPhysicalProbe?Math.Max(visit.EnterAt+20,visit.LeaveAt-15):visit.EnterAt+20;
                }
            }
            if(selected==null)
                throw new InvalidOperationException(routineRecoveryPhysicalProbe?
                    "Owned fixture has no existing Shop visit whose post-expiry planner forecast retains a later optional stop and home. No route was manufactured.":
                    "Owned fixture has no existing optional adult Shop visit with ten minutes remaining. No patron was manufactured.");
            if(routineRecoveryPhysicalProbe)
            {
                Require(sourceStopIndex>=0,"Physical recovery fixture has no saved source itinerary index.");
            }
            DaggerfallStaticDoors doorOwner=null;StaticDoor entry=default(StaticDoor);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>()) foreach(var door in doors.Doors)
                if(door.doorType==DoorTypes.Building && door.buildingKey==building){doorOwner=doors;entry=door;break;}
            Require(doorOwner,"Saved shop visit has no actual native exterior door.");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double arrival=(Math.Floor(FixtureNow()/86400d)+1)*86400d+visitAt;
            clock.RaiseTime((float)(arrival-FixtureNow()));yield return null;yield return null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_FIXTURE: save="+fixture+"; resident="+selected.Id+"; building="+building+"; savedVisitOffset="+visitAt);
            var rosy=Type.GetType("PeopleOfTheIliacBay.QuestHooks.RosyInteriorCompatibilityHooks, PeopleOfTheIliacBay.QuestHooks",false);
            bool rosyInstalled=rosy!=null && (bool)rosy.GetProperty("Installed").GetValue(null,null);
            int correctionsBefore=rosyInstalled?(int)rosy.GetProperty("Completed").GetValue(null,null):0;
            TownSmokeHarness.EnterInterior(doorOwner.transform,entry);DaggerfallUI.Instance.PopToHUD();
            float deadline=Time.realtimeSinceStartup+90;
            ResidentActor actor=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(selected.Id);
                if(streets.Ready && game.PlayerEnterExit.Interior && actor && actor.gameObject.activeInHierarchy) break;
                yield return null;
            }
            var room=game.PlayerEnterExit.Interior;
            Require(room && room.EntryDoor.buildingKey==building && actor && actor.gameObject.activeInHierarchy && !actor.IsSleeping,
                "Actual scheduled shop patron did not appear in its native visit.");
            if(rosyInstalled)
            {
                deadline=Time.realtimeSinceStartup+15;
                while((int)rosy.GetProperty("Completed").GetValue(null,null)==correctionsBefore && Time.realtimeSinceStartup<deadline)yield return null;
                Require((int)rosy.GetProperty("Completed").GetValue(null,null)>correctionsBefore,
                    "Rosy's native entry correction did not settle before shop observer placement.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_ENTRY_SETTLED: Rosy's valid correction completed before observer placement.");
            }
            Require(game.PlayerEnterExit.IsPlayerInsideOpenShop && PlayerActivate.IsBuildingOpen(room.BuildingData.BuildingType),"Scheduled shop is closed.");
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",FixturePrivate).GetValue(streets);
            Require(geometry!=null,"Shop floor survey unavailable.");
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",FixturePrivate).GetValue(streets);
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();string reason;
            Require(owner && owner.Binding!=null && NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason),"Native buyer inventory unavailable.");
            var wallet=store.ReadNpcInventory(actor.Resident.Id);
            // Test fixture funds only, using the normal persisted transaction.
            // It creates no item, shelf or actor. Restart mode persists only to
            // its explicitly named disposable commerce slots.
            if(!lootSaleProbe && wallet.WalletGold<100000)
            {
                Require(store.CommitNpcInventoryTransaction("commerce-fixture-funds:"+Guid.NewGuid().ToString("N"),new[]{new NpcInventoryUpdate{
                    PersonId=wallet.PersonId,ExpectedRevision=wallet.Revision,WalletGold=100000,Items=wallet.Items}}),"Fixture wallet provisioning failed.");
                Require(NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason),reason);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_TEST_FUNDS: resident="+wallet.PersonId+"; before="+wallet.WalletGold+"; after=100000; "+
                    (restartPreparation?"disposable commerce restart saves only.":"disposable memory only."));
            }
            int activated=0;
            foreach(var shelf in room.GetComponentsInChildren<DaggerfallLoot>())
            {
                if(shelf.ContainerType!=LootContainerTypes.ShopShelves || shelf.LoadID==0 || shelf.playerOwned || shelf.houseOwned) continue;
                if(!ActivateNativeShelf(game,shelf,geometry,room)) continue;
                activated++;DaggerfallUI.Instance.PopToHUD();yield return null;
            }
            Require(activated>0,"No native shop shelf could be activated through a real collider raycast.");
            if(lootSaleProbe)
            {
                Require(PositionObserver(game,geometry,room,actor,2.5f,5.5f),"No public observer point near the real sale visitor.");
                yield return NativeNpcShopLootSaleProbe.RunScene(streets,character,actor.Resident.Id);
                yield break;
            }
            NpcActionOption choice=null;string itemName=null;
            Require(PositionObserver(game,geometry,room,actor,2.5f,5.5f),"No public observer point near the real patron.");
            foreach(var shelf in room.GetComponentsInChildren<DaggerfallLoot>())
            {
                if(choice!=null) break;
                if(shelf.ContainerType!=LootContainerTypes.ShopShelves) continue;
                for(int i=0;i<shelf.Items.Count && choice==null;i++)
                {
                    var item=shelf.Items.GetItem(i);
                    if(restartPreparation && (item.stackCount!=1 || item.IsStackable())) continue;
                    if(item.IsQuestItem || item.IsEquipped || item.IsSummoned || item.ItemGroup==ItemGroups.Transportation ||
                        !string.IsNullOrEmpty(item.GetSaveData().className)) continue;
                    foreach(var option in streets.ConversationActionOptions(actor.Resident.Id,"Buy one "+item.LongName+"."))
                    {
                        if(option.Kind!=NpcActionKind.Purchase || option.Target.ItemId!=item.UID.ToString()) continue;
                        var target=room.transform.TransformPoint(new Vector3((float)option.Target.X,(float)option.Target.Y,(float)option.Target.Z));
                        if(Vector3.Distance(actor.transform.position-Vector3.up,target)<1.2f) continue;
                        choice=option;itemName=item.LongName;break;
                    }
                }
            }
            Require(choice!=null,"Native stocked shelves have no unambiguous affordable safe item requiring physical approach.");
            if(restartPreparation)
            {
                yield return NpcCommerceRestartProbe.Prepare(streets,character,actor,choice);
                yield break;
            }
            if(taskFailureProbe)
            {
                yield return RunScene(streets,character,itemName,actor.Resident.Id,host,true);
                yield break;
            }
            var before=store.ReadNpcInventory(actor.Resident.Id);var beforeIds=new HashSet<ulong>();foreach(var record in before.Items)beforeIds.Add(record.Uid);
            int sellerBefore=NativeNpcInventoryBridge.InitialWalletGold(store,choice.Target.PersonId);
            int playerGold=game.PlayerEntity.GetGoldAmount();
            yield return RunScene(streets,character,itemName,actor.Resident.Id,host);
            var purchase=store.LatestNpcAction(actor.Resident.Id);
            Require(purchase!=null && purchase.ServiceCode==NpcShopCommerce.ServiceCode && purchase.Phase==NpcActionPhase.Completed,"Scene purchase has no observed completion.");
            Require(store.ReadNpcInventory(purchase.Target.PersonId).WalletGold==sellerBefore+purchase.ServicePrice,"Keeper wallet did not receive exact purchase price.");
            DaggerfallUnityItem bought=null;
            for(int i=0;i<owner.Binding.Items.Count;i++) {var item=owner.Binding.Items.GetItem(i);if(!beforeIds.Contains(item.UID)) {Require(bought==null,"Purchase created more than one inventory item.");bought=item;}}
            Require(bought!=null && bought.LongName==itemName,"Purchased inventory item unavailable for separate delivery.");
            ulong boughtUid=bought.UID;
            DaggerfallLoot stock=null;foreach(var shelf in room.GetComponentsInChildren<DaggerfallLoot>()) if(shelf.LoadID.ToString()==purchase.Target.ContainerId)stock=shelf;
            Require(stock,"Purchased native stock vanished.");
            VerifySceneSerialization(host,store,purchase,stock,boughtUid,true);
            // Hold the observer at a reachable point a few metres away. NPC must
            // physically approach the player before the transfer can commit.
            Require(PositionObserver(game,geometry,room,actor,3f,5.5f),"No nearby handover observer point.");
            var task=store.ActiveNpcTask(actor.Resident.Id);
            Require(task!=null && task.Steps.Count==2 && task.Steps[1].ItemSourceStep==1,"Accepted purchase lost its queued handover.");
            int playerCount=ItemCount(game.PlayerEntity.Items,bought.ItemGroup,bought.TemplateIndex);
            string handoverId=task.Id+":step:1";
            var previous=actor.transform.position;
            float walked=0,maxStep=0;deadline=Time.realtimeSinceStartup+120;
            while(Time.realtimeSinceStartup<deadline)
            {
                float step=Vector3.Distance(previous,actor.transform.position);walked+=step;maxStep=Mathf.Max(maxStep,step);previous=actor.transform.position;
                if(NpcActionExecutor.IsTerminal(store.ReadNpcAction(handoverId))) break;
                yield return null;
            }
            var handed=store.ReadNpcAction(handoverId);
            Require(handed!=null && handed.Phase==NpcActionPhase.Completed,"Queued delivery failed: "+store.ReadNpcTask(task.Id).FailureReason);
            Require(handed.Target.ItemId==boughtUid.ToString(),"Queued delivery selected an item other than the actual purchase result.");
            Require(walked>0.5f && maxStep<1.5f && Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=1.5f,
                "Delivery did not physically approach nearby player.");
            Require(owner.Binding.Items.GetItem(boughtUid)==null && ItemCount(game.PlayerEntity.Items,bought.ItemGroup,bought.TemplateIndex)==playerCount+1 &&
                game.PlayerEntity.GetGoldAmount()==playerGold && store.ReadNpcInventory(actor.Resident.Id).WalletGold==before.WalletGold-purchase.ServicePrice,
                "Delivery ownership/count/wallet conservation failed.");
            Require(!NativeNpcInventoryBridge.TryCompleteActionHandover(store,handed,FixtureNow(),owner.Binding,game.PlayerEntity.Items,()=>true,out reason),"Completed delivery replay succeeded.");
            VerifySceneSerialization(host,store,handed,stock,boughtUid,false);
            for(int frame=0;frame<10 && store.ReadNpcTask(task.Id).Phase!=NpcTaskPhase.Completed;frame++)yield return null;
            Require(store.ReadNpcTask(task.Id).Phase==NpcTaskPhase.Completed,"Physical purchase/handover task did not complete.");
            if(routineRecoveryPhysicalProbe)
                yield return RoutineRecoveryPhysicalProbe.Observe(streets,actor,room,store,store.ReadNpcTask(task.Id),sourceStopIndex);
            if(routineRecoveryDayProbe)
                yield return RoutineRecoveryDayProbe.Observe(streets,task.ResidentId,store,sourceStopIndex);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_FIXTURE_PASS: real-stock native-activation scheduled-patron composed-purchase-handover serializer replay; item="+itemName+
                "; sourceUid="+purchase.Target.ItemId+"; purchasedUid="+boughtUid+"; gold="+purchase.ServicePrice+"; handoverMetres="+walked.ToString("F2"));
        }

        internal static bool PositionObserver(GameManager game,InteriorGeometry geometry,DaggerfallInterior room,ResidentActor actor,float minimum,float maximum)
        {
            foreach(var local in geometry.ApproachPoints(room.transform.InverseTransformPoint(actor.transform.position-Vector3.up),maximum,0.4f))
            {
                Vector3 point=room.transform.TransformPoint(local)+Vector3.up;
                if(Vector3.Distance(point,actor.transform.position)<minimum) continue;
                var route=geometry.RouteBetween(room.transform.InverseTransformPoint(actor.transform.position-Vector3.up),local);
                if(route.Count<2) continue;
                game.PlayerMotor.transform.position=point;Physics.SyncTransforms();return true;
            }
            return false;
        }
        private static bool ActivateNativeShelf(GameManager game,DaggerfallLoot shelf,InteriorGeometry geometry,DaggerfallInterior room)
        {
            var activate=typeof(PlayerActivate).GetMethod("ActivateLootContainer",FixturePrivate);
            Require(activate!=null,"Native shelf activation handler unavailable.");
            foreach(var local in geometry.ApproachPoints(room.transform.InverseTransformPoint(shelf.transform.position),1.8f,2.5f))
            {
                Vector3 origin=room.transform.TransformPoint(local)+Vector3.up;
                foreach(var collider in shelf.GetComponentsInChildren<Collider>())
                {
                    if(!collider.enabled || collider.isTrigger) continue;
                    Vector3 delta=collider.bounds.center-origin;if(delta.magnitude>2.2f) continue;
                    RaycastHit hit;
                    if(!Physics.Raycast(origin,delta.normalized,out hit,delta.magnitude+0.1f,~0,QueryTriggerInteraction.Ignore) ||
                        hit.collider.GetComponentInParent<DaggerfallLoot>()!=shelf) continue;
                    game.PlayerMotor.transform.position=origin;Physics.SyncTransforms();
                    activate.Invoke(game.PlayerActivate,new object[]{hit,shelf});
                    Require(shelf.stockedDate==DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now),"Native activation did not stock shelf.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_NATIVE_STOCK: loadId="+shelf.LoadID+"; count="+shelf.Items.Count+"; nativeStockedDate="+shelf.stockedDate);
                    return true;
                }
            }
            return false;
        }
        private static int ItemCount(ItemCollection items,ItemGroups group,int template)
        {int total=0;for(int i=0;i<items.Count;i++){var item=items.GetItem(i);if(item.ItemGroup==group && item.TemplateIndex==template)total+=item.stackCount;}return total;}
        private static void VerifySceneSerialization(PeopleOfTheIliacBayMod host,WorldStateStore live,NpcActionJob job,DaggerfallLoot stock,ulong boughtUid,bool owns)
        {
            var world=(WorldState)host.GetSaveData();var encoded=SaveLoadManager.Serialize(typeof(WorldState),world);
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded));
            NativeNpcInventoryBinding reopened;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(restored,job.ResidentId,null,0,out reopened,out reason),reason);
            Require((reopened.Items.GetItem(boughtUid)!=null)==owns && restored.ReadNpcInventory(job.ResidentId).WalletGold==live.ReadNpcInventory(job.ResidentId).WalletGold &&
                restored.ReadNpcAction(job.Id).Phase==NpcActionPhase.Completed,"Native mod serialization changed observed ownership/payment/action.");
            var serializer=stock.GetComponent<SerializableLootContainer>();Require(serializer,"Native shelf serializer missing.");
            var data=serializer.GetSaveData();var copied=(LootContainerData_v1)SaveLoadManager.Deserialize(data.GetType(),SaveLoadManager.Serialize(data.GetType(),data));
            var items=new ItemCollection();items.DeserializeItems(copied.items);
            Require(items.Count==stock.Items.Count,"Native shelf serialization changed stock identities.");
            for(int i=0;i<stock.Items.Count;i++){var item=stock.Items.GetItem(i);Require(items.GetItem(item.UID)!=null && items.GetItem(item.UID).stackCount==item.stackCount,"Native shelf serialization changed exact UID/count.");}
            var player=new ItemCollection();player.DeserializeItems(GameManager.Instance.PlayerEntity.Items.SerializeItems());
            Require(player.Count==GameManager.Instance.PlayerEntity.Items.Count,"Native player serialization changed item identities.");
            Require(!restored.TryCompleteNpcActionEffect(job.Id,job.Revision,job.UpdatedAt,()=>{throw new InvalidOperationException("Restored completed effect replayed.");}),"Restored action accepted duplicate effect.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_SERIALIZER_PASS: phase="+job.Kind+"; npcOwns="+owns+"; stockItems="+items.Count+"; playerItems="+player.Count);
        }

        private static bool HasLaterOptionalRecoveryLeg(RoutineRecoveryPlan plan,int sourceStopIndex)
        {
            if(plan==null || !plan.SkippedStopIndices.Contains(sourceStopIndex))return false;
            foreach(var leg in plan.Legs)
                if(!leg.ReturnsHome && leg.OriginalStopIndex>sourceStopIndex)return true;
            return false;
        }
    }
}
