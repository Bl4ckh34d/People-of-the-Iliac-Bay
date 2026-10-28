using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in disposable-fixture acceptance. Outdoor approach is controlled;
    // all room movement, keeper service, wallet changes and seats are native.
    internal static partial class HouseholdInnPhysicalProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            float readyDeadline=Time.realtimeSinceStartup+90;
            while((!streets.Ready || !streets.EntranceSurveyReady) && Time.realtimeSinceStartup<readyDeadline)yield return null;
            if(!streets.Ready || !streets.EntranceSurveyReady)
                throw new InvalidOperationException("Family Inn fixture has no ready native town geometry.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var townGeometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
            var initialSnapshot=(WorldState)host.GetSaveData();
            HouseholdDayPlan plan;
            HouseholdDayPersistenceProbe.CreateRequest(initialSnapshot,directory,townGeometry,6*60,22*60,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),out plan);
            DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())foreach(var door in candidate.Doors)
                if(door.doorType==DoorTypes.Building && door.buildingKey==plan.JointInn.InnKey){owner=candidate;entry=door;break;}
            if(!owner)throw new InvalidOperationException("Family Inn native entrance is missing.");
            var originalPlayer=game.PlayerMotor.transform.position;int playerGold=game.PlayerEntity.GoldPieces;
            bool hostEnabled=host.enabled,streetEnabled=streets.enabled,motorEnabled=game.PlayerMotor.enabled;ResidentActor first=null,second=null;HouseholdJointInnMotion visit=null;
            WorldStateStore catalogStore=null;
            try
            {
            try
            {
                // This owned process never saves. Match the service hour so a
                // dawn keeper's legitimate sleep does not fake a refusal.
                double serviceTime=plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
                double advance=serviceTime-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                if(advance>0)DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)advance);
                var unloadedCheck=VerifyUnloadedNativeCatalog(initialSnapshot,owner.transform,entry,directory.LocationData,serviceTime);
                try{while(unloadedCheck.MoveNext())yield return unloadedCheck.Current;}
                finally{(unloadedCheck as IDisposable)?.Dispose();}
                var rosy=Type.GetType("PeopleOfTheIliacBay.QuestHooks.RosyInteriorCompatibilityHooks, PeopleOfTheIliacBay.QuestHooks",false);
                bool rosyInstalled=rosy!=null && (bool)rosy.GetProperty("Installed").GetValue(null,null);
                int correctionsBefore=rosyInstalled?(int)rosy.GetProperty("Completed").GetValue(null,null):0;
                TownSmokeHarness.EnterInterior(owner.transform,entry);yield return new WaitForSecondsRealtime(1);DaggerfallUI.Instance.PopToHUD();
                var room=game.PlayerEnterExit.Interior;if(!room || room.EntryDoor.buildingKey!=plan.JointInn.InnKey)
                    throw new InvalidOperationException("Wrong family Inn loaded.");
                float ready=Time.realtimeSinceStartup+30;
                while((DaggerfallInterior)typeof(StreetPopulation).GetField("interior",Private).GetValue(streets)!=room && Time.realtimeSinceStartup<ready)yield return null;
                if((DaggerfallInterior)typeof(StreetPopulation).GetField("interior",Private).GetValue(streets)!=room)
                    throw new InvalidOperationException("Family Inn live bindings did not prepare.");
                if(rosyInstalled)
                {
                    float settleBy=Time.realtimeSinceStartup+15;
                    while((int)rosy.GetProperty("Completed").GetValue(null,null)==correctionsBefore && Time.realtimeSinceStartup<settleBy)yield return null;
                    if((int)rosy.GetProperty("Completed").GetValue(null,null)<=correctionsBefore)
                        throw new InvalidOperationException("Rosy's native entry correction did not settle before family Inn observer placement.");
                }
                var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(initialSnapshot);
                // Loading the real room may publish a new ordinary household
                // day. Keep this fixture's earlier source snapshot, importing
                // only the observed keeper's real immutable native identity.
                var liveActivities=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",Private).GetValue(streets);
                var liveKeeper=liveActivities==null?null:liveActivities.TavernKeeper;
                var keeperIdentity=liveKeeper?((WorldState)host.GetSaveData()).NativePeople.Find(p=>p.WorkKey==room.EntryDoor.buildingKey &&
                    p.NativeHash==liveKeeper.Data.hash && p.SettlementId==plan.SettlementId):null;
                if(keeperIdentity==null || !keeperIdentity.IsAlive)throw new InvalidOperationException("The actual family Inn keeper identity is unavailable.");
                var liveStore=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
                float catalogBy=Time.realtimeSinceStartup+15;
                while(liveStore.ReadNpcInnServiceCatalog(plan.SettlementId,room.EntryDoor.buildingKey)==null && Time.realtimeSinceStartup<catalogBy)yield return null;
                if(liveStore.ReadNpcInnServiceCatalog(plan.SettlementId,room.EntryDoor.buildingKey)==null)
                    throw new InvalidOperationException("Production scheduler did not capture the actual loaded Inn service catalog.");
                store.RegisterNativePerson(keeperIdentity.SettlementId,keeperIdentity.WorkKey,keeperIdentity.NativeHash,keeperIdentity.Name,
                    keeperIdentity.Sex,keeperIdentity.Race,keeperIdentity.FactionId,keeperIdentity.TextureArchive,keeperIdentity.TextureRecord);
                string reason;if(!store.TryPublishHouseholdDay(plan,plan.PlannedAt,out reason))throw new InvalidOperationException(reason);
                var geometry=new InteriorGeometry(room,CrowdMotion.BodyRadius);
                if(!geometry.EntryGround.HasValue)throw new InvalidOperationException("Family Inn has no actual entry floor.");
                var activities=new ShopVisitGeometry(room,geometry,npc=>store.NativePeopleFor(plan.SettlementId).Exists(p=>
                    p.WorkKey==room.EntryDoor.buildingKey && p.NativeHash==npc.Data.hash && p.IsAlive));
                var keeperNpc=activities.TavernKeeper;
                var keeper=keeperNpc?store.NativePeopleFor(plan.SettlementId).Find(p=>p.WorkKey==room.EntryDoor.buildingKey && p.NativeHash==keeperNpc.Data.hash):null;
                if(keeper==null)throw new InvalidOperationException("Family Inn lacks a real living native keeper.");
                host.enabled=false;streets.enabled=false;
                // Keep all existing patrons/keepers in place. Move only the
                // fixture player away from the entry so two bodies can enter.
                Vector3 observer=geometry.EntryGround.Value;bool observerFound=false;
                foreach(var anchor in geometry.LocalAnchors)
                {
                    var point=room.transform.TransformPoint(anchor);
                    if(FurnitureSeatCatalog.Blocks(point,point,CrowdMotion.BodyRadius) || geometry.RouteToExit(point).Count<2)continue;
                    bool occupied=false;foreach(var nearby in StreetPopulation.ShopDepartureNeighbours(point,null))
                        if(Vector3.Distance(nearby,point)<1.2f){occupied=true;break;}
                    foreach(var existing in room.GetComponentsInChildren<ResidentActor>())
                        if(existing.gameObject.activeInHierarchy && Vector3.Distance(existing.transform.position-Vector3.up,point)<1.2f)occupied=true;
                    if(!occupied && (point-geometry.EntryGround.Value).sqrMagnitude>(observer-geometry.EntryGround.Value).sqrMagnitude)
                    {observer=point;observerFound=true;}
                }
                if(!observerFound)throw new InvalidOperationException("No safe unoccupied observer floor in the family Inn fixture.");
                // Freeze only fixture input/gravity. Its real collider remains
                // enabled at this clear observer floor throughout the run.
                game.PlayerMotor.enabled=false;game.PlayerMotor.transform.position=observer+Vector3.up;
                // Collision queries must observe the player's new position,
                // not its previous doorway collider from this same frame.
                yield return null;Physics.SyncTransforms();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_OBSERVER: position="+game.PlayerMotor.transform.position+"; nativeEntrySettled=true; colliderRetained=true");
                var catalogCheck=VerifyNativeCatalog(store,room,activities,keeper,serviceTime);
                try{while(catalogCheck.MoveNext())yield return catalogCheck.Current;}
                finally{(catalogCheck as IDisposable)?.Dispose();}
                catalogStore=store;
                var lifecycleCheck=VerifyOffscreenLifecycle(store,plan,streets,room);
                try{while(lifecycleCheck.MoveNext())yield return lifecycleCheck.Current;}
                finally{(lifecycleCheck as IDisposable)?.Dispose();}
                var motion=ControlledDoor(store,plan);double entered=motion.Snapshot().ObservedAt+1;
                Fund(store,plan.First.ResidentId);Fund(store,plan.Second.ResidentId);
                first=Create(store,plan.First.ResidentId,room,game);second=Create(store,plan.Second.ResidentId,room,game);
                first.Place(geometry.EntryGround.Value,Vector3.forward,true);second.Place(geometry.EntryGround.Value,Vector3.forward,true);
                if(Vector3.Distance(first.transform.position-Vector3.up,geometry.EntryGround.Value)>1.2f ||
                    Vector3.Distance(second.transform.position-Vector3.up,geometry.EntryGround.Value)>1.2f)
                    throw new InvalidOperationException("The controlled doorway transition cannot place both actual room bodies: entry="+geometry.EntryGround.Value+
                        "; first="+(first.transform.position-Vector3.up)+"/"+first.GetComponent<CrowdMotion>().HasPlacement+
                        "; second="+(second.transform.position-Vector3.up)+"/"+second.GetComponent<CrowdMotion>().HasPlacement+"; player="+game.PlayerMotor.transform.position);
                motion.EnterInn(entered);store.SaveHouseholdJointProgress(motion.Snapshot());
                int firstGold=store.ReadNpcInventory(first.Resident.Id).WalletGold,secondGold=store.ReadNpcInventory(second.Resident.Id).WalletGold;
                Func<ResidentActor,PhysicalShopVisit,double,bool> buy=(actor,physical,at)=>StreetPopulation.TryCompleteInnPurchaseAtCounter(store,actor,physical,
                    room,geometry,activities,plan.SettlementId,at,npc=>npc==keeperNpc?store.NativePeopleFor(plan.SettlementId).Find(p=>p.Id==keeper.Id):null,
                    npc=>streets.KeeperServiceBlocked(npc));
                if(!HouseholdJointInnMotion.TryCreate(store,plan,first,second,room,geometry,activities,entered,buy,out visit))
                    throw new InvalidOperationException("Actual family Inn cannot provide two compatible free seats/routes.");
                float started=Time.realtimeSinceStartup,deadline=started+300;double offset=0;bool paused=false,reloaded=false,seated=false,egressReloaded=false,sequentialDoor=false;
                while(Time.realtimeSinceStartup<deadline && !visit.Complete)
                {
                    double at=entered+(Time.realtimeSinceStartup-started)*DaggerfallUnity.Instance.WorldTime.TimeScale+offset;
                    var a=first.transform.position;var b=second.transform.position;visit.Tick(at,true,true);
                    bool firstExited=visit.FirstVisit.Snapshot.Phase==ShopVisitPhase.Complete,secondExited=visit.SecondVisit.Snapshot.Phase==ShopVisitPhase.Complete;
                    if(!first.gameObject.activeSelf && !firstExited || !second.gameObject.activeSelf && !secondExited)
                        throw new InvalidOperationException("A shared Inn body vanished before actual doorway arrival.");
                    if(firstExited!=secondExited && (firstExited?!first.gameObject.activeSelf:!second.gameObject.activeSelf))sequentialDoor=true;
                    if(Vector3.Distance(a,first.transform.position)>0.551f || Vector3.Distance(b,second.transform.position)>0.551f)
                        throw new InvalidOperationException("Shared Inn movement exceeded one body step: first="+Vector3.Distance(a,first.transform.position)+
                            "/"+visit.FirstVisit.Snapshot.Phase+"; second="+Vector3.Distance(b,second.transform.position)+
                            "/"+visit.SecondVisit.Snapshot.Phase+"; seated="+seated+"; egressReload="+egressReloaded);
                    var firstReceipt=store.ReadNpcInnConsumption(first.Resident.Id);var secondReceipt=store.ReadNpcInnConsumption(second.Resident.Id);
                    bool paid=firstReceipt!=null && secondReceipt!=null && firstReceipt.Outcome==NpcInnConsumptionOutcome.Purchased && secondReceipt.Outcome==NpcInnConsumptionOutcome.Purchased;
                    if((first.IsSeated || second.IsSeated) && !paid)throw new InvalidOperationException("A family patron sat before both native purchases.");
                    if(!paused && Time.realtimeSinceStartup-started>1)
                    {
                        paused=true;first.SetConversationFacing(true);a=first.transform.position;b=second.transform.position;
                        for(int i=0;i<5;i++)
                        {
                            visit.Tick(at+i*0.01,true,true);
                            if(first.transform.position!=a || second.transform.position!=b)throw new InvalidOperationException("Conversation did not hold both Inn patrons.");
                            yield return null;
                        }
                        first.SetConversationFacing(false);offset+=0.1;
                        at=entered+(Time.realtimeSinceStartup-started)*DaggerfallUnity.Instance.WorldTime.TimeScale+offset;
                    }
                    bool reloadEgress=seated && !egressReloaded && (InChairEgress(first,visit.FirstVisit,activities) || InChairEgress(second,visit.SecondVisit,activities));
                    if(paid && (!reloaded || reloadEgress))
                    {
                        visit.Capture(at);string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                        var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
                        a=first.transform.position;b=second.transform.position;
                        double originalFirstEnd=visit.FirstVisit.OriginalEndAt,originalSecondEnd=visit.SecondVisit.OriginalEndAt;
                        visit.Release();store=restored;
                        if(!HouseholdJointInnMotion.TryCreate(store,plan,first,second,room,geometry,activities,at,buy,out visit) ||
                            Vector3.Distance(a,first.transform.position)>0.001f || Vector3.Distance(b,second.transform.position)>0.001f ||
                            Math.Floor(originalFirstEnd)!=Math.Floor(visit.FirstVisit.OriginalEndAt) || Math.Floor(originalSecondEnd)!=Math.Floor(visit.SecondVisit.OriginalEndAt))
                            throw new InvalidOperationException("Native shared Inn reload changed actual body positions or lost the pair.");
                        reloaded=true;
                        if(reloadEgress)egressReloaded=true;
                    }
                    if(first.IsSeated && second.IsSeated && !seated)
                    {
                        seated=true;
                        if(!activities.Seating.CanShareTable(visit.FirstVisit.SelectedSeatId,visit.SecondVisit.SelectedSeatId))
                            throw new InvalidOperationException("The two actual patrons did not share a compatible real table.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_SEATED: first="+visit.FirstVisit.SelectedSeatId+"; second="+visit.SecondVisit.SelectedSeatId);
                        VerifySharedConversationContext(host,store,plan,first,second,room,at);
                        // Skip only the already observed social dwell. Actual
                        // egress still requires each body's continuous route.
                        offset+=Math.Max(0,Math.Min(visit.FirstVisit.OriginalEndAt,visit.SecondVisit.OriginalEndAt)+1-at);
                    }
                    if(visit.RequiresRecovery)throw new InvalidOperationException("Actual shared Inn requested unexpected recovery.");
                    yield return null;
                }
                if(!visit.Complete || !paused || !reloaded || !egressReloaded || !sequentialDoor || !seated || first.IsSeated || second.IsSeated)
                    throw new InvalidOperationException("Shared Inn physical flow incomplete: complete="+visit.Complete+"; seated="+seated+"; reload="+reloaded+
                        "; egressReload="+egressReloaded+"; sequentialDoor="+sequentialDoor+"; first="+visit.FirstVisit.Snapshot.Phase+"/"+visit.FirstVisit.LastFailure+"; second="+visit.SecondVisit.Snapshot.Phase+"/"+visit.SecondVisit.LastFailure);
                var firstPaid=store.ReadNpcInnConsumption(first.Resident.Id);var secondPaid=store.ReadNpcInnConsumption(second.Resident.Id);
                if(firstPaid==null || secondPaid==null)throw new InvalidOperationException("Family purchase receipts missing.");
                string payer=firstPaid.PayerId??firstPaid.BuyerId;
                int total=firstPaid.ChargedGold+secondPaid.ChargedGold;
                if(firstPaid.VisitId==secondPaid.VisitId || payer!=(secondPaid.PayerId??secondPaid.BuyerId) ||
                    payer!=first.Resident.Id && payer!=second.Resident.Id ||
                    store.ReadNpcInventory(first.Resident.Id).WalletGold!=firstGold-(payer==first.Resident.Id?total:0) ||
                    store.ReadNpcInventory(second.Resident.Id).WalletGold!=secondGold-(payer==second.Resident.Id?total:0) ||
                    game.PlayerEntity.GoldPieces!=playerGold || store.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey).Phase!=HouseholdJointPhase.Returning)
                    throw new InvalidOperationException("Native shared purchases did not charge the exact payer wallet or preserve the nonpayer and player.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_PAYER_NATIVE_PASS: onePayer=true; total="+total+"; nativeServiceGoldSink=true; playerUnchanged=true");
                if(Vector3.Distance(first.transform.position-Vector3.up,geometry.EntryGround.Value)>0.65f ||
                    Vector3.Distance(second.transform.position-Vector3.up,geometry.EntryGround.Value)>0.65f)
                    throw new InvalidOperationException("Both actual parents did not reach the Inn's interior doorway endpoint.");
            }
            finally
            {
                if(visit!=null)visit.Release();
                if(first){first.gameObject.SetActive(false);UnityEngine.Object.Destroy(first.gameObject);}
                if(second){second.gameObject.SetActive(false);UnityEngine.Object.Destroy(second.gameObject);}
                if(game.PlayerEnterExit.IsPlayerInsideBuilding)game.PlayerEnterExit.TransitionExterior();
            }
            yield return new WaitForSecondsRealtime(1);DaggerfallUI.Instance.PopToHUD();game.PlayerMotor.transform.position=originalPlayer;
            var exteriorCheck=VerifyOutdoorFamilyRuntime(initialSnapshot,catalogStore,plan,streets,directory,townGeometry);
            try{while(exteriorCheck.MoveNext())yield return exteriorCheck.Current;}
            finally{(exteriorCheck as IDisposable)?.Dispose();}
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_PHYSICAL_PASS: actualInterior=true; realKeeper=true; separatePurchases=true; sameTable=true; conversationHoldsBoth=true; nativeReload=true; egressReload=true; sequentialDoor=true; actualDoorEndpoint=true; exteriorReturn=false; controlledOutdoor=true; liveFamily=false");
            }
            finally
            {
                game.PlayerMotor.transform.position=originalPlayer;game.PlayerMotor.enabled=motorEnabled;
                host.enabled=hostEnabled;streets.enabled=streetEnabled;
            }
        }
        private static void VerifySharedConversationContext(PeopleOfTheIliacBayMod host,WorldStateStore store,HouseholdDayPlan plan,ResidentActor first,ResidentActor second,DaggerfallInterior room,double now)
        {
            var before=store.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey);
            if(before==null)throw new InvalidOperationException("Actual shared Inn has no group progress for conversation capture.");
            var archive=store.CaptureConversationArchiveCheckpoint();
            var capture=typeof(PeopleOfTheIliacBayMod).GetMethod("CaptureConversationHouseholdOuting",Private);
            if(capture==null)throw new InvalidOperationException("Native shared outing conversation adapter is missing.");
            foreach(var actor in new[]{first,second})
            {
                var partner=actor==first?second:first;
                var group=(HouseholdOutingConversation)capture.Invoke(host,new object[]{store,actor.Resident.Id,actor,now});
                if(group==null || group.PartnerId!=partner.Resident.Id || group.Phase!=HouseholdJointPhase.Visiting)
                    throw new InvalidOperationException("Actual shared Inn lost the speaker-bound NPC companion context.");
                string building=GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey(room.EntryDoor.buildingKey);
                if(!actor.transform.IsChildOf(room.transform) || !partner.transform.IsChildOf(room.transform))
                    throw new InvalidOperationException("Shared Inn context fixture bodies are not in the actual room.");
                // These controlled bodies are not in the live population map.
                // The adapter must leave their partner presence unknown; this
                // fixture separately supplies its observed native-room proof.
                group=group.WithObservation(building,Vector3.Distance(actor.transform.position,partner.transform.position)<=6,true);
                var binding=new ConversationContextBinding(ConversationActorCategory.Mobile,actor.Resident.Id,null,"native-pair-context",1,1,
                    archive.ArchiveId,archive.Head,actor.gameObject.scene.name,1);
                var unknown=ConversationContextFact<string>.Unknown();
                var facts=new ConversationContextSnapshot(binding,now,unknown,unknown,ConversationContextFact<ConversationDialogueRole>.Unknown(),
                    unknown,unknown,ConversationContextFact<ConversationNpcPosition>.Unknown(),ConversationContextFact<ConversationNpcActivity>.Unknown(),
                    ConversationContextFact<bool>.Unknown(),ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),unknown,0,1,null,
                    new ConversationContextCommitment[0],new ConversationContextSourceHandle[0]).WithHouseholdOuting(group);
                foreach(var projection in new Func<ConversationContextSnapshot,int,string>[] {ConversationContextFormatter.ForRouting,
                    ConversationContextFormatter.ForSemanticClassifier,ConversationContextFormatter.ForSocialReply,ConversationContextFormatter.ForActionVoice})
                {
                    string text=projection(facts,2048);
                    if(!text.Contains(partner.Resident.FullName) || !text.Contains("visiting") || !text.Contains("player is not a group member"))
                        throw new InvalidOperationException("Native shared Inn context omitted partner, phase or player distinction.");
                }
            }
            var after=store.ReadHouseholdJointProgress(before.SettlementId,before.HomeKey);
            if(before.Sequence!=after.Sequence)throw new InvalidOperationException("Conversation projection advanced family progress.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_CONVERSATION_CONTEXT_PASS: actualPair=true; reciprocal=true; projections=4; playerNotMember=true; nativeAdapter=true; nativeTalk=false; movementUnchanged=true");
        }
        private static bool InChairEgress(ResidentActor actor,PhysicalShopVisit visit,ShopVisitGeometry activities)
        {
            if(actor.IsSeated)return false;
            var slot=activities.Seating.Find(visit.HouseholdSeatId);var actual=actor.transform.position-Vector3.up;
            return slot!=null && slot.Furniture.Blocks(actual,actual,CrowdMotion.BodyRadius);
        }
        private static HouseholdJointMovement ControlledDoor(WorldStateStore store,HouseholdDayPlan plan)
        {
            var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());
            double at=plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            motion.Depart(at,plan.JointInn.First.Itinerary.Stops[0].Route[0],plan.JointInn.Second.Itinerary.Stops[0].Route[0]);store.SaveHouseholdJointProgress(motion.Snapshot());
            for(int i=0;motion.Snapshot().Phase==HouseholdJointPhase.Outbound;i++)
            {
                if(i>100000)throw new InvalidOperationException("Controlled exterior Inn fixture did not arrive.");
                var p=motion.Snapshot();var targets=motion.Targets(true,true);motion.Observe(at+=0.125,Step(p.First.Position,targets.First),Step(p.Second.Position,targets.Second));
                store.SaveHouseholdJointProgress(motion.Snapshot());
            }
            return motion;
        }
        private static IEnumerator VerifyUnloadedNativeCatalog(WorldState snapshot,Transform owner,StaticDoor door,DFLocation location,double now)
        {
            var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(snapshot);
            var game=GameManager.Instance;var player=game.PlayerMotor.transform.position;
            int people=UnityEngine.Object.FindObjectsOfType<StaticNPC>().Length;
            string wallets=SaveLoadManager.Serialize(typeof(List<NpcInventoryState>),store.Snapshot().NpcInventories);
            string observations=SaveLoadManager.Serialize(typeof(List<ObservedVisit>),store.Snapshot().ObservedVisits);
            var result=new TavernBedSurvey.Result();
            var steps=TavernBedSurvey.ReadServiceSteps(owner,door,location.Climate.ClimateType,result,()=>true);
            int frames=0;double maxSliceMs=0;
            try
            {
                while(true)
                {
                    var actorsBefore=NativeActorIds();
                    var timer=System.Diagnostics.Stopwatch.StartNew();bool more=steps.MoveNext();timer.Stop();
                    if(!actorsBefore.SetEquals(NativeActorIds()))
                        throw new InvalidOperationException("Unloaded survey slice created or removed an actor, including outside its temporary room.");
                    maxSliceMs=Math.Max(maxSliceMs,timer.Elapsed.TotalMilliseconds);if(!more)break;
                    frames++;
                    var loaded=(IEnumerable)typeof(FurnitureSeatCatalog).GetField("loaded",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                    foreach(FurnitureSeatCatalog catalog in loaded)if(catalog && catalog.name.Contains("isolated tavern survey"))
                        throw new InvalidOperationException("Unloaded Inn furniture entered the live collision registry across a yield.");
                    foreach(var room in UnityEngine.Object.FindObjectsOfType<DaggerfallInterior>())
                        if(room.name=="People of the Iliac Bay isolated tavern survey" && room.GetComponentsInChildren<StaticNPC>(true).Length!=0)
                            throw new InvalidOperationException("Model-only Inn survey created a native NPC in its temporary room.");
                    yield return steps.Current;
                }
            }
            finally{(steps as IDisposable)?.Dispose();}
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_INN_CATALOG_TIMING: frames="+frames+"; maxSliceMs="+
                maxSliceMs.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture));
            var top=DaggerfallUI.UIManager.TopWindow;
            // Other world owners can populate the exterior during yielded work.
            // Check the synchronous identity/menu registration separately.
            int registrationPeople=UnityEngine.Object.FindObjectsOfType<StaticNPC>().Length;
            NativeTavernRentals.RegisterInventory(store,location.MapTableData.MapId,location,door,result,null,now);
            var source=store.ReadNpcInnServiceCatalog(location.MapTableData.MapId,door.buildingKey);
            if(source==null || frames<2 || !result.KeeperHash.HasValue || source.KeeperHash!=result.KeeperHash.Value ||
                source.BlockIndex!=door.blockIndex || source.RecordIndex!=door.recordIndex || source.RegionIndex!=location.RegionIndex ||
                source.Climate!=(int)DaggerfallWorkshop.Utility.ClimateSwaps.FromAPIClimateBase(location.Climate.ClimateType) ||
                game.PlayerEnterExit.IsPlayerInsideBuilding || Vector3.Distance(game.PlayerMotor.transform.position,player)>0.01f || DaggerfallUI.UIManager.TopWindow!=top ||
                UnityEngine.Object.FindObjectsOfType<StaticNPC>().Length!=registrationPeople ||
                observations!=SaveLoadManager.Serialize(typeof(List<ObservedVisit>),store.Snapshot().ObservedVisits) ||
                wallets!=SaveLoadManager.Serialize(typeof(List<NpcInventoryState>),store.Snapshot().NpcInventories))
                throw new InvalidOperationException("Unloaded Inn source, native identity or actor/wallet isolation failed: catalog="+(source!=null)+
                    "; frames="+frames+"; keeper="+result.KeeperHash+"; draftPairs="+(result.ServiceCatalog==null?0:result.ServiceCatalog.Pairs.Count)+
                    "; playerDelta="+Vector3.Distance(game.PlayerMotor.transform.position,player)+"; people="+people+"/"+UnityEngine.Object.FindObjectsOfType<StaticNPC>().Length);
            var roundtrip=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            roundtrip.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            if(roundtrip.ReadNpcInnServiceCatalog(source.SettlementId,source.BuildingKey).Fingerprint!=source.Fingerprint)
                throw new InvalidOperationException("Unloaded native Inn catalog failed actual serializer/store restoration.");
            var cancelled=new TavernBedSurvey.Result();bool valid=true;
            steps=TavernBedSurvey.ReadServiceSteps(owner,door,location.Climate.ClimateType,cancelled,()=>valid);
            try{if(!steps.MoveNext())throw new InvalidOperationException("Unloaded cancellation fixture never began.");valid=false;while(steps.MoveNext())yield return steps.Current;}
            finally{(steps as IDisposable)?.Dispose();}
            if(cancelled.Beds!=null || typeof(TavernBedSurvey.Result).GetField("ServiceCatalog").GetValue(cancelled)!=null)
                throw new InvalidOperationException("Cancelled unloaded Inn service survey published partial data.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_INN_CATALOG_PASS: actualRecords=true; nativeKeeper=true; nativeMenu=true; pairs="+
                source.Pairs.Count+"; noSurveyNpc=true; registrationNoNpcSpawn=true; noLiveFurniture=true; walletsUnchanged=true; cancelled=true; nativeReload=true; purchase=false");
        }
        private static HashSet<int> NativeActorIds()
        {
            var ids=new HashSet<int>();
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<StaticNPC>())ids.Add(npc.GetInstanceID());
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<MobilePersonNPC>())ids.Add(npc.GetInstanceID());
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<ResidentActor>())ids.Add(npc.GetInstanceID());
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<GuardActor>())ids.Add(npc.GetInstanceID());
            return ids;
        }
        private static IEnumerator VerifyNativeCatalog(WorldStateStore store,DaggerfallInterior room,
            ShopVisitGeometry activities,NativePerson keeper,double now)
        {
            string wallets=SaveLoadManager.Serialize(typeof(List<NpcInventoryState>),store.Snapshot().NpcInventories);
            string observations=SaveLoadManager.Serialize(typeof(List<ObservedVisit>),store.Snapshot().ObservedVisits);
            string CatalogContents()
            {
                var existing=store.ReadNpcInnServiceCatalog(keeper.SettlementId,keeper.WorkKey);
                return existing==null?string.Empty:SaveLoadManager.Serialize(typeof(NpcInnServiceCatalog),existing);
            }
            string priorCatalog=CatalogContents();
            bool current=true;
            var cancelled=StreetPopulation.CaptureInnServiceCatalog(store,room,activities,keeper,now,()=>current);
            try
            {
                if(!cancelled.MoveNext())throw new InvalidOperationException("Native catalog capture must yield before publication.");
                // Menu/keeper facts publish immediately. Cancellation only
                // prevents the subsequent scene-dependent seating survey.
                priorCatalog=CatalogContents();
                current=false;while(cancelled.MoveNext())yield return cancelled.Current;
            }
            finally{(cancelled as IDisposable)?.Dispose();}
            if(CatalogContents()!=priorCatalog)
                throw new InvalidOperationException("Cancelled native catalog capture published into a stale scene.");
            var stale=StreetPopulation.CaptureInnServiceCatalog(store,room,activities,keeper,now,()=>true);
            try
            {
                if(!stale.MoveNext())throw new InvalidOperationException("Native catalog capture did not start before restore.");
                store.Restore(store.Snapshot());
                while(stale.MoveNext())yield return stale.Current;
            }
            finally{(stale as IDisposable)?.Dispose();}
            if(CatalogContents()!=priorCatalog)
                throw new InvalidOperationException("Native catalog capture survived a same-store restore epoch change.");
            var steps=StreetPopulation.CaptureInnServiceCatalog(store,room,activities,keeper,now,()=>true);
            int frames=0;
            try{while(steps.MoveNext()){frames++;yield return steps.Current;}}
            finally{(steps as IDisposable)?.Dispose();}
            var catalog=store.ReadNpcInnServiceCatalog(keeper.SettlementId,keeper.WorkKey);
            if(catalog==null || frames<2 || catalog.KeeperId!=keeper.Id || catalog.KeeperHash!=keeper.NativeHash ||
                catalog.BuildingKey!=room.EntryDoor.buildingKey || catalog.BlockIndex!=room.EntryDoor.blockIndex ||
                catalog.RecordIndex!=room.EntryDoor.recordIndex || catalog.RegionIndex!=GameManager.Instance.PlayerGPS.CurrentRegionIndex)
                throw new InvalidOperationException("Native catalog source/identity or cooperative capture is missing.");
            var menuReader=typeof(StreetPopulation).GetMethod("TryReadNativeTavernMenu",BindingFlags.Static|BindingFlags.NonPublic);
            object[] menuArgs={activities.TavernKeeper,null,null};
            if(!(bool)menuReader.Invoke(null,menuArgs))throw new InvalidOperationException("Native catalog menu oracle is unavailable.");
            var names=(string[])menuArgs[1];var prices=(byte[])menuArgs[2];
            string[] template;byte[] templatePrices;var top=DaggerfallUI.UIManager.TopWindow;
            if(!StreetPopulation.TryReadNativeTavernMenuTemplate(out template,out templatePrices) || DaggerfallUI.UIManager.TopWindow!=top)
                throw new InvalidOperationException("Read-only native menu template changed the visible UI or was unavailable.");
            for(int i=0;i<11;i++)if(catalog.Menu[i].ProductName!=names[i] || catalog.Menu[i].BasePriceGold!=prices[i])
                throw new InvalidOperationException("Native catalog changed an actual menu label/base price.");
            for(int i=0;i<11;i++)if(template[i]!=names[i] || templatePrices[i]!=prices[i])
                throw new InvalidOperationException("Unloaded menu template differs from the actual loaded native merchant menu.");
            foreach(var pair in catalog.Pairs)if(!activities.Seating.CanShareTable(pair.First.Id,pair.Second.Id))
                throw new InvalidOperationException("Native catalog invented a compatible table pair.");
            VerifyNativeServiceCalendar(catalog);
            string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
            var copy=restored.ReadNpcInnServiceCatalog(keeper.SettlementId,keeper.WorkKey);
            if(copy==null || copy.Fingerprint!=catalog.Fingerprint || NpcInnServiceCatalog.Copy(copy).Fingerprint!=catalog.Fingerprint ||
                wallets!=SaveLoadManager.Serialize(typeof(List<NpcInventoryState>),store.Snapshot().NpcInventories) ||
                observations!=SaveLoadManager.Serialize(typeof(List<ObservedVisit>),store.Snapshot().ObservedVisits))
                throw new InvalidOperationException("Native catalog capture changed wallets/observations or failed native reload.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_SERVICE_CATALOG_PASS: actualInterior=true; nativeMenu=true; compatiblePairs="+
                catalog.Pairs.Count+"; cooperativeFrames="+frames+"; productionScheduler=true; cancelledSource=true; restoreEpoch=true; walletsUnchanged=true; nativeReload=true; simulatedPurchase=false");
        }
        private static void VerifyNativeServiceCalendar(NpcInnServiceCatalog catalog)
        {
            double worldBefore=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var epochDate=new DaggerfallWorkshop.Utility.DaggerfallDateTime();epochDate.FromClassicDaggerfallTime(0);
            double epoch=epochDate.ToSeconds(),harvest=epoch+236*86400d+3600;
            var tariff=NativeInnCalendar.ForServiceAt(catalog.RegionIndex,harvest);
            if(tariff.HolidayId!=37 || catalog.OpenMinute!=0 || catalog.CloseMinute!=1440)
                throw new InvalidOperationException("Native Inn calendar lost its historical holiday or all-day service hour.");
            for(int i=0;i<11;i++)
            {
                var quote=NpcInnServiceCatalog.Quote(catalog,i,harvest,tariff);
                var newLife=NpcInnServiceCatalog.Quote(catalog,i,epoch+3600,NativeInnCalendar.ForServiceAt(catalog.RegionIndex,epoch+3600));
                if(quote.PriceGold!=Math.Max(1,catalog.Menu[i].BasePriceGold/2) || newLife.PriceGold!=catalog.Menu[i].BasePriceGold)
                    throw new InvalidOperationException("Historical NPC Inn quote differs from native menu pricing.");
            }
            double lastMinute=epoch+86399.5;
            NpcInnServiceCatalog.Quote(catalog,0,lastMinute,NativeInnCalendar.ForServiceAt(catalog.RegionIndex,lastMinute));
            NpcInnServiceCatalog.Quote(catalog,0,epoch+86400,NativeInnCalendar.ForServiceAt(catalog.RegionIndex,epoch+86400));
            foreach(double invalid in new[]{epoch-1,epoch+((double)uint.MaxValue+1)*60})
            {
                bool rejected=false;try{NativeInnCalendar.ForServiceAt(catalog.RegionIndex,invalid);}catch(ArgumentException){rejected=true;}
                if(!rejected)throw new InvalidOperationException("Native Inn calendar accepted an underflow/wrapped timestamp.");
            }
            if(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()!=worldBefore)
                throw new InvalidOperationException("Historical service quote changed the live world clock.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_INN_CALENDAR_PASS: nativeCatalog=true; historicalHoliday=true; finalMinute=true; epochBoundaries=true; worldClockUnchanged=true");
        }
        private static SavedRoutePoint Step(SavedRoutePoint from,SavedRoutePoint to)
        {double x=to.X-from.X,z=to.Z-from.Z,length=Math.Sqrt(x*x+z*z),scale=length>0?Math.Min(1,0.25/length):0;return new SavedRoutePoint{X=from.X+x*scale,Z=from.Z+z*scale};}
        private static ResidentActor Create(WorldStateStore store,string id,DaggerfallInterior room,GameManager game)
        {
            var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
            var actor=body.AddComponent<ResidentActor>();actor.Initialize(store.ResolveResident(id),1,game.StreamingWorld.GetCurrentCityNavigation(),inventoryStore:store);body.SetActive(true);return actor;
        }
        private static void Fund(WorldStateStore store,string id)
        {
            if(store.InitializeNpcInventory(id,100,Array.Empty<NpcInventoryItem>()))return;
            var inventory=store.ReadNpcInventory(id);
            if(!store.CommitNpcInventoryTransaction("household-inn-fixture-capital:"+id,new[]{new NpcInventoryUpdate{PersonId=id,ExpectedRevision=inventory.Revision,
                WalletGold=100,Items=inventory.Items}}))throw new InvalidOperationException("Detached family fixture capital failed.");
        }
    }
}
