using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit acceptance only. Optional setup discovers and hires an actual
    // adventurer before transport. Measured acceptance starts at the real entry.
    internal static partial class NativeDungeonFollowProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] private sealed class Fixture
        {public int mapId=-1;public string residentId=null;public bool verifyDungeonStop=true;public bool verifyRecovery=false;public bool verifyLoot=false;public bool requireFastTravelOwner=false;public bool requireUnpaidFastTravelOwner=false;public float stagedDoorDistance=2.2f;}
        private sealed class Evidence
        {
            public WorldStateStore Store;
            public ResidentActor Actor;
            public RoadTravelActors Roads;
            public string Id,ActionId,Service,Inventory,Contract,Stage;
            public double Accepted,Finish,Deadline;
            public int Body,Person;
            public readonly Dictionary<ulong,DaggerfallUnityItem> Items=new Dictionary<ulong,DaggerfallUnityItem>();
            public Vector3 Previous;
            public float Metres,MaximumStep,NativeInitial;
            public bool Tracking;
            public bool MultipleThreatsVerified;
            public bool ObserverHeldForThreat=true;
            public bool DangerObserved,NativePayloadChanged,NativeCombatMutationObserved;
            public int OriginalWallet,LastHealth,LastCounterattacks;
            public string LastThreat,LastContact;
            private void ObserveNativeCombatMutation()
            {
                var entity=Actor.GetComponent<DaggerfallEntityBehaviour>();
                int health=entity&&entity.Entity!=null?entity.Entity.CurrentHealth:LastHealth;
                if(Actor.Counterattacks>LastCounterattacks || health<LastHealth &&
                    (LastThreat!=null || RoadTravelActors.CurrentDungeonThreat(Actor)))NativeCombatMutationObserved=true;
                LastHealth=health;LastCounterattacks=Actor.Counterattacks;
            }
            public void ResetMotion(){Previous=Actor.transform.position-Vector3.up;Metres=MaximumStep=0;Tracking=true;
                var native=Actor.GetComponent<NativeResidentNavigation>();NativeInitial=native?native.DistanceWalked:0;}
            public void CheckIdentity()
            {
                Need(Actor && Actor.Person && Actor.GetInstanceID()==Body && Actor.Person.GetInstanceID()==Person,
                    "Dungeon Follow replaced the native body or person.");
                var job=Store.ReadNpcAction(ActionId);
                if(GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon && job!=null && job.FollowTransition==null)
                    Need(Roads.FindDungeonCompanion(Id)==Actor && !Roads.OwnsPaidCompanion(Id),
                        "A visible dungeon follower still has a competing paid/fast-travel exterior owner.");
                Need(job!=null && !NpcActionExecutor.IsTerminal(job) && job.ResidentId==Id && job.ServiceCode==Service &&
                    job.AcceptedAt==Accepted && job.FinishAt==Finish && job.DeadlineAt==Deadline,
                    "Dungeon Follow changed or ended its accepted action: "+(job==null?"missing":job.Phase+"; "+job.FailureReason));
                int count=0;foreach(var body in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(body && body.gameObject.activeInHierarchy && body.Resident!=null && body.Resident.Id==Id)count++;
                Need(count==(Actor.gameObject.activeInHierarchy?1:0),"Dungeon Follow created duplicate visible bodies.");
                ObserveNativeCombatMutation();Actor.CaptureState();var inventory=Store.ReadNpcInventory(Id);
                NativePayloadChanged=InventorySignature(inventory)!=Inventory;
                Need(!NativePayloadChanged || NativeCombatMutationObserved && inventory.WalletGold==OriginalWallet,
                    "Dungeon Follow changed native inventory payload/wallet without an observed hostile encounter.");
                var items=Actor.GetComponent<NativeNpcInventoryOwner>().Binding.Items;
                Need(items.Count==Items.Count,"Native item collection size changed.");
                for(int index=0;index<items.Count;index++)
                {var item=items.GetItem(index);DaggerfallUnityItem expected;Need(Items.TryGetValue(item.UID,out expected) && ReferenceEquals(item,expected),"Native item object or UID changed.");}
                Need(ContractSignature(Store,job)==Contract,"Dungeon Follow changed the paid contract or payment receipt state.");
            }
            public void Sample()
            {
                if(!Tracking || !Actor || !Actor.gameObject.activeInHierarchy)return;
                var current=Actor.transform.position-Vector3.up;float step=Vector3.Distance(Previous,current);
                ObserveNativeCombatMutation();
                Need(step<=0.55f,"Dungeon Follow moved more than the existing bounded physical step: "+step);
                Need(StaticFloor(current,GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon?
                    GameManager.Instance.PlayerEnterExit.Dungeon.transform:null,out _),
                    "Dungeon Follow lost static floor support; stage="+Stage+"; from="+Previous+"; at="+current);
                var threat=RoadTravelActors.CurrentDungeonThreat(Actor);
                string threatId=threat?NativeNpcCombatAssist.Identity(threat):null;
                if(threatId!=LastThreat)
                {
                    if(threat)
                    {DangerObserved=true;var senses=threat.GetComponent<EnemySenses>();var native=Actor.GetComponent<NativeResidentNavigation>();
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_HOSTILE_ENCOUNTER: stage="+Stage+"; target="+threatId+
                            "; npc="+current+"; hostile="+threat.transform.position+"; nativeTarget="+(senses&&senses.Target?senses.Target.name:"none")+
                            "; nativeReason="+(native?native.FailureReason:"none")+"; observerHeld="+(ObserverHeldForThreat?"true":"false")+"; enemySuppressed=false");}
                    else Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_HOSTILE_CLEAR: stage="+Stage+"; target="+LastThreat+"; originalFollowRequired=true");
                    LastThreat=threatId;
                }
                // Native hostile movement can enter the sampled segment after
                // the NPC's own check. Classify contact, retaining every static
                // and other body collision check and actual enemy physics.
                foreach(var blocker in CollisionCandidates(Previous,current,CrowdMotion.BodyRadius))
                {
                    if(!Blocks(blocker,Actor.transform,false))continue;
                    var hostile=blocker.GetComponentInParent<DaggerfallEntityBehaviour>();
                    bool nativeThreat=hostile && NativeNpcCombatAssist.Identity(hostile)!=null &&
                        NativeNpcCombatAssist.ThreatensPlayerOrActor(hostile,Actor,false) &&
                        NativeNpcCombatAssist.SameVisibleScene(Actor,hostile,6);
                    // A native enemy can contact a stationary NPC while fighting
                    // another enemy. With no NPC displacement this is external
                    // contact, not evidence that the follower crossed a body.
                    bool stationaryContact=hostile && NativeNpcCombatAssist.Identity(hostile)!=null && step<=0.000001f;
                    if(nativeThreat || stationaryContact)
                    {
                        if(nativeThreat)DangerObserved=true;string contact=NativeNpcCombatAssist.Identity(hostile);
                        if(LastContact!=contact)
                        {LastContact=contact;var native=Actor.GetComponent<NativeResidentNavigation>();
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_HOSTILE_CONTACT: stage="+Stage+"; target="+contact+
                                "; npcFrom="+Previous+"; npcAt="+current+"; npcStep="+step+"; hostileAt="+hostile.transform.position+
                                "; collider="+blocker.name+"; nativeReason="+(native?native.FailureReason:"none")+
                                "; stationaryNpcContact="+stationaryContact+"; dynamicContact=true; staticWallCrossingProven=false; enemyPhysicsRetained=true");}
                        continue;
                    }
                    var motor=hostile?hostile.GetComponent<EnemyMotor>():null;
                    var senses=hostile?hostile.GetComponent<EnemySenses>():null;
                    var navigation=Actor.GetComponent<NativeResidentNavigation>();
                    Need(false,"Dungeon Follow crossed collision; stage="+Stage+"; from="+Previous.ToString("F6")+"; at="+current.ToString("F6")+
                        "; step="+step.ToString("F6")+"; nativeReason="+(navigation?navigation.FailureReason:"none")+
                        "; hostile="+(motor && motor.IsHostile)+"; nativeTarget="+(senses&&senses.Target?senses.Target.name:"none")+
                        "; visibleThreat="+nativeThreat+"; colliderPosition="+blocker.transform.position.ToString("F6")+
                        "; collider="+blocker.name+"; type="+blocker.GetType().Name+"; trigger="+blocker.isTrigger+
                        "; player="+(bool)blocker.GetComponentInParent<PlayerMotor>()+"; owner="+blocker.transform.root.name);
                }
                Metres+=step;MaximumStep=Mathf.Max(MaximumStep,step);Previous=current;
            }
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;string json=Environment.GetEnvironmentVariable("POTIB_DUNGEON_FOLLOW_FIXTURE");
            Need(host && streets && game && game.PlayerEntity!=null && NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) &&
                !string.IsNullOrWhiteSpace(json) && json.Length<=4096,
                "Dungeon Follow probe requires an owned exterior fixture and POTIB_DUNGEON_FOLLOW_FIXTURE JSON.");
            yield return RunFixture(host,streets,JsonUtility.FromJson<Fixture>(json));
        }

        internal static IEnumerator RunExistingFastTravelFollower(PeopleOfTheIliacBayMod host,StreetPopulation streets,int map,string id)
        {
            Need(NativeProbeText.IsTestCharacter(GameManager.Instance.PlayerEntity.Name) &&
                Environment.GetEnvironmentVariable("POTIB_DUNGEON_FOLLOW_PREPARE")!="1" &&
                Environment.GetEnvironmentVariable("POTIB_DUNGEON_RECOVERY_RESUME")!="1",
                "Actual fast-travel acceptance must not invoke fixture transport or recovery staging.");
            yield return RunFixture(host,streets,new Fixture {mapId=map,residentId=id,requireFastTravelOwner=true,verifyDungeonStop=false});
        }

        private static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,Fixture fixture)
        {
            var game=GameManager.Instance;var roads=host.GetComponent<RoadTravelActors>();
            var store=typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host) as WorldStateStore;
            if(Environment.GetEnvironmentVariable("POTIB_DUNGEON_RECOVERY_RESUME")=="1")
            {
                Need(fixture!=null && !string.IsNullOrWhiteSpace(fixture.residentId) && roads && store!=null,"Recovery resume needs its actual saved resident.");
                yield return VerifyRecoveryOriginArrival(store,streets,fixture.residentId,true);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: recoveryOriginArrival=true; checkpointResume=true");
                yield break;
            }
            if(Environment.GetEnvironmentVariable("POTIB_DUNGEON_FOLLOW_PREPARE")=="1")
                yield return PrepareFixture(host,streets,roads,store,fixture);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            Need(fixture!=null && fixture.mapId>=0 && !string.IsNullOrWhiteSpace(fixture.residentId) && roads && store!=null &&
                !game.PlayerEnterExit.IsPlayerInside &&
                directory && directory.MapID==fixture.mapId && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.MapTableData.MapId==fixture.mapId,"Dungeon Follow fixture does not match its actual exterior map.");
            var actor=streets.FindActor(fixture.residentId);if(!actor)actor=roads.FindPaidCompanion(fixture.residentId);
            var job=store.ActiveNpcAction(fixture.residentId);
            Need(actor && actor.gameObject.activeInHierarchy && actor.Person && job!=null && job.Kind==NpcActionKind.Follow &&
                job.Target.PersonId=="player" && job.FollowTransition==null && !NpcActionExecutor.IsTerminal(job) &&
                job.FinishAt-Now()>=600 && job.DeadlineAt-Now()>=600,
                "Probe requires the real accepted visible follower with at least 600 game seconds of existing consent remaining; it does not extend consent.");
            Need(!fixture.requireFastTravelOwner || job.FastTravelFollowOwner,
                "This dungeon case requires a genuine existing fast-travel owner; the probe does not invent that handoff.");
            Need(!fixture.requireUnpaidFastTravelOwner || job.FastTravelFollowOwner && !NpcPaidCompanionAction.IsPaidFollowJob(job),
                "The unpaid fast-travel case needs actual unexpired unpaid consent; no contract or deadline is changed by the probe.");
            DaggerfallStaticDoors entranceOwner;StaticDoor entrance;
            Need(FindEntrance(fixture.mapId,out entranceOwner,out entrance),"Player must already stand within four metres of an actual native dungeon entrance.");
            actor.CaptureState();var nativeItems=actor.GetComponent<NativeNpcInventoryOwner>();
            Need(nativeItems && nativeItems.Binding!=null && nativeItems.Binding.Items.Count>0,"Probe requires the follower's real bound native item inventory.");
            var evidence=new Evidence {Store=store,Actor=actor,Roads=roads,Id=fixture.residentId,ActionId=job.Id,Service=job.ServiceCode,
                Accepted=job.AcceptedAt,Finish=job.FinishAt,Deadline=job.DeadlineAt,Body=actor.GetInstanceID(),Person=actor.Person.GetInstanceID(),
                Inventory=InventorySignature(store.ReadNpcInventory(fixture.residentId)),OriginalWallet=store.ReadNpcInventory(fixture.residentId).WalletGold,
                LastHealth=actor.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth,LastCounterattacks=actor.Counterattacks,
                Contract=ContractSignature(store,job)};
            for(int index=0;index<nativeItems.Binding.Items.Count;index++)
            {var item=nativeItems.Binding.Items.GetItem(index);evidence.Items.Add(item.UID,item);}
            var controller=game.PlayerMotor.GetComponent<CharacterController>();Need(controller && controller.enabled,"Probe requires the native enabled player controller.");
            bool oldMotor=game.PlayerMotor.enabled;
            var dialogueField=typeof(PeopleOfTheIliacBayMod).GetField("localDialogueEnabled",Private);
            bool oldDialogue=(bool)dialogueField.GetValue(host);dialogueField.SetValue(host,false);
            game.PlayerMotor.enabled=false;
            float outward=0,returned=0,exterior=0,maximumStep=0;
            try
            {
                DaggerfallUI.Instance.PopToHUD();
                yield return Enter(entranceOwner,entrance,fixture.mapId,evidence);
                NavMeshPath path=null;yield return FindDungeonPath(value=>path=value);
                Need(path!=null,"No actual clear eight-metre walking path exists from this native entry.");
                if(Environment.GetEnvironmentVariable("POTIB_DUNGEON_AUTOMATIC_OBSTACLE")=="1")
                {
                    yield return VerifyAutomaticObstacle(evidence,controller,path);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: automaticObstacle=true; roundTrip=false; stop=false");
                    yield break;
                }
                evidence.Stage="outward";yield return WalkObserver(path.corners,controller,evidence,false);
                if(!fixture.verifyRecovery)yield return WaitFollower(evidence,20);
                evidence.CheckIdentity();Need(roads.IsLoadedDungeonCompanion(actor),"Loaded dungeon actor discovery failed.");
                // Recovery has its own measured physical return criterion.
                // Native flight during setup must not turn this focused case
                // into another acceptance run for five metres of NavMesh Follow.
                if(fixture.verifyRecovery)
                {
                    evidence.Stage="recovery";yield return VerifyRecovery(evidence,controller);
                    if(Environment.GetEnvironmentVariable("POTIB_DUNGEON_RECOVERY_ARRIVAL")=="1")
                        yield return VerifyRecoveryOriginArrival(store,streets,evidence.Id,false);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: recovery=true; automaticObstacle=false; roundTrip=false; stop=false");
                    yield break;
                }
                var navigation=actor.GetComponent<NativeResidentNavigation>();
                Need(navigation && navigation.DistanceWalked-evidence.NativeInitial>5 && evidence.Metres>5,"Follower did not physically walk more than five metres through native navigation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_OUTWARD_PASS: resident="+evidence.Id+"; measuredMetres="+evidence.Metres+
                    "; nativeMetres="+(navigation.DistanceWalked-evidence.NativeInitial)+"; sameBody=true; roundTripNotYetVerified=true");
                outward=evidence.Metres;maximumStep=evidence.MaximumStep;
                yield return VerifyTalkContext(host,actor,evidence);
                if(Environment.GetEnvironmentVariable("POTIB_DUNGEON_ENTERPRISE_DIALOGUE")=="1")
                {yield return VerifyEnterpriseDialogue(host,evidence);yield break;}
                if(Environment.GetEnvironmentVariable("POTIB_DUNGEON_CONTAINER_LOOT")=="1" ||
                    Environment.GetEnvironmentVariable("POTIB_DUNGEON_MULTITHREAT")=="1")
                {yield return VerifyContainerLoot(evidence);yield break;}
                if(fixture.verifyLoot)
                {yield return VerifyLoot(evidence);yield break;}

                var reverse=(Vector3[])path.corners.Clone();Array.Reverse(reverse);evidence.ResetMotion();evidence.Stage="return-inside";
                yield return WalkObserver(reverse,controller,evidence,true);
                bool dangerBoundary=Environment.GetEnvironmentVariable("POTIB_DUNGEON_DANGER_BOUNDARY")=="1";
                bool nativeExit=Environment.GetEnvironmentVariable("POTIB_DUNGEON_DANGER_EXIT")=="1";
                if(dangerBoundary || nativeExit)
                {
                    if(dangerBoundary)yield return VerifyDangerBoundary(evidence);
                    if(nativeExit)yield return VerifyDangerExit(fixture.mapId,evidence,controller);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: dangerBoundary="+dangerBoundary+"; nativeExit="+nativeExit+"; roundTrip=false; stop=false");
                    yield break;
                }
                yield return WaitFollower(evidence,20);
                returned=evidence.Metres;maximumStep=Mathf.Max(maximumStep,evidence.MaximumStep);
                Need(returned>4,"Follower did not physically return along the measured dungeon path.");evidence.CheckIdentity();
                yield return Exit(fixture.mapId,evidence);
                Need(!roads.OwnsDungeonFollow(evidence.Id) && (streets.FindActor(evidence.Id)==actor || roads.FindPaidCompanion(evidence.Id)==actor),
                    "Native return failed to restore the existing exterior owner.");
                var outsideStart=PlayerGround();var normal=DaggerfallStaticDoors.GetDoorNormal(entrance);normal.y=0;normal.Normalize();
                Vector3 outsideEnd;Need(StaticFloor(outsideStart+normal*5,null,out outsideEnd) &&
                    ClearSupportedRoute(new[]{outsideStart,outsideEnd},null,actor.transform),"Actual exterior doorway has no clear supported five-metre continuation.");
                evidence.ResetMotion();evidence.Stage="exterior";yield return WalkObserver(new[]{outsideStart,outsideEnd},controller,evidence,true);
                yield return WaitFollower(evidence,20);exterior=evidence.Metres;maximumStep=Mathf.Max(maximumStep,evidence.MaximumStep);
                Need(exterior>2.5f,"Returned follower did not continue physically outside.");evidence.CheckIdentity();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_ROUNDTRIP_PASS: map="+fixture.mapId+"; resident="+evidence.Id+
                    "; action="+evidence.ActionId+"; body="+evidence.Body+"; nativeItemObjects="+evidence.Items.Count+
                    "; outwardMetres="+outward+"; returnMetres="+returned+"; exteriorMetres="+exterior+"; maximumStep="+maximumStep+
                    "; nativeEntry=true; nativeExit=true; sameBody=true; contractUnchanged=true; nativeItemObjectsUnchanged=true; inventoryPayloadUnchanged="+
                    !evidence.NativePayloadChanged+"; hostileEncounterObserved="+evidence.DangerObserved+"; nativeTalkContext=true");
                if(fixture.verifyDungeonStop)
                {
                    evidence.Stage="return-to-entry";yield return WalkObserver(new[]{outsideEnd,outsideStart},controller,evidence,true);yield return WaitFollower(evidence,20);
                    Need(FindEntrance(fixture.mapId,out entranceOwner,out entrance),"Returned player did not reach a current native dungeon entrance for the Stop check.");
                    yield return Enter(entranceOwner,entrance,fixture.mapId,evidence);
                    path=null;yield return FindDungeonPath(value=>path=value);Need(path!=null,"Second native entry has no verified path.");
                    evidence.Stage="stop-reentry";yield return WalkObserver(path.corners,controller,evidence,false);yield return WaitFollower(evidence,20);
                    yield return VerifyTalkContext(host,actor,evidence);
                    Vector3 stopped=actor.transform.position;string reason;
                    Need(streets.TryStopConversationAction(evidence.Id,out reason),"Dungeon Stop was unavailable without town ownership: "+reason);
                    var cancelled=store.ReadNpcAction(evidence.ActionId);Need(cancelled.Phase==NpcActionPhase.Cancelled,"Dungeon Stop did not cancel the same Follow action.");
                    float end=Time.realtimeSinceStartup+1.5f;while(Time.realtimeSinceStartup<end){Need(Vector3.Distance(stopped,actor.transform.position)<=0.4f,"Stopped dungeon follower kept moving.");yield return null;}
                    var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                    Need(restored.ActiveNpcAction(evidence.Id)==null && restored.ReadNpcAction(evidence.ActionId).Phase==NpcActionPhase.Cancelled,
                        "Cancelled dungeon Follow resurrected after a Core snapshot restore.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_STOP_PASS: resident="+evidence.Id+"; nativeBodyHeld=true; townReady="+streets.Ready+
                        "; actionCancelled=true; snapshotNoResurrection=true; diskReload=false; semanticStop=false");
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: roundTrip=true; stop="+fixture.verifyDungeonStop+
                    "; requiredFastTravelOwner="+fixture.requireFastTravelOwner+"; requiredUnpaidFastTravelOwner="+fixture.requireUnpaidFastTravelOwner);
            }
            finally
            {DaggerfallUI.Instance.PopToHUD();game.PlayerMotor.enabled=oldMotor;dialogueField.SetValue(host,oldDialogue);}
        }

        private static bool FindEntrance(int map,out DaggerfallStaticDoors owner,out StaticDoor entrance)
        {
            owner=null;entrance=default(StaticDoor);var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();if(!directory || directory.MapID!=map)return false;
            float nearest=4;
            foreach(var collection in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var raw in collection.Doors??new StaticDoor[0])
                {
                    var door=raw;door.ownerPosition=collection.transform.position;door.ownerRotation=collection.transform.rotation;
                    float distance=Vector3.Distance(DaggerfallStaticDoors.GetDoorPosition(door),game.PlayerMotor.transform.position);
                    if(door.doorType==DoorTypes.DungeonEntrance && distance<nearest){nearest=distance;entrance=door;owner=collection;}
                }
            return owner;
        }

        private static IEnumerator Enter(DaggerfallStaticDoors owner,StaticDoor door,int map,Evidence evidence)
        {
            evidence.Tracking=false;var game=GameManager.Instance;
            game.PlayerEnterExit.TransitionDungeonInterior(owner.transform,door,game.PlayerGPS.CurrentLocation);
            var pending=evidence.Store.ReadNpcAction(evidence.ActionId);
            Need(pending!=null && pending.FollowTransition!=null && NpcFollowTransition.IsDungeonSpace(pending.FollowTransition.Destination),
                "Actual native entry did not prepare the accepted follower: "+(pending==null?"missing":pending.FailureReason));
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline && (!game.PlayerEnterExit.IsPlayerInsideDungeon || !game.PlayerEnterExit.Dungeon ||
                game.PlayerEnterExit.Dungeon.Summary.ID!=map || !NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready ||
                NativeNavigationWorld.Current.Scene!="dungeon-"+map))yield return null;
            Need(game.PlayerEnterExit.IsPlayerInsideDungeon && NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready &&
                NativeNavigationWorld.Current.Scene=="dungeon-"+map,"Actual native dungeon/navmesh did not become ready.");
            // The observer still occupies the native marker. The first physical
            // path leg vacates it; no NPC warp or alternate landing is requested.
        }
        private static IEnumerator Exit(int map,Evidence evidence)
        {
            var game=GameManager.Instance;var dungeon=game.PlayerEnterExit.Dungeon;StaticDoor[] exits=
                DaggerfallStaticDoors.FindDoorsInCollections(dungeon.StaticDoorCollections,DoorTypes.DungeonExit);
            Vector3 position;int index;
            Need(exits!=null && exits.Length>0 && DaggerfallStaticDoors.FindClosestDoorToPlayer(game.PlayerMotor.transform.position,exits,out position,out index),
                "Native dungeon has no actual exit door.");
            DaggerfallStaticDoors.FindClosestDoorToPlayer(game.PlayerMotor.transform.position,exits,out position,out index);
            Need(Vector3.Distance(position,game.PlayerMotor.transform.position)<=4,"Verified return path did not reach the native exit.");
            evidence.Tracking=false;game.PlayerEnterExit.TransitionDungeonExterior();
            var pending=evidence.Store.ReadNpcAction(evidence.ActionId);
            Need(pending.FollowTransition!=null && !NpcFollowTransition.IsDungeonSpace(pending.FollowTransition.Destination),
                "Actual native exit did not prepare follower return: "+pending.FailureReason);
            yield return WaitForExteriorFloor(map);
            // Walk observer away from the exact native landing before awaiting
            // the follower. Placement must wait for a real clear doorway.
            DaggerfallStaticDoors exteriorOwner;StaticDoor exteriorDoor;
            Need(FindEntrance(map,out exteriorOwner,out exteriorDoor),"Native dungeon return is not beside its actual exterior door.");
            var start=PlayerGround();var forward=DaggerfallStaticDoors.GetDoorNormal(exteriorDoor);forward.y=0;
            Vector3 clear;Need(StaticFloor(start+forward.normalized*3,null,out clear) &&
                ClearSupportedRoute(new[]{start,clear},null,evidence.Actor.transform),"Native exterior landing has no clear observer departure.");
            yield return WalkObserver(new[]{start,clear},game.PlayerMotor.GetComponent<CharacterController>(),evidence,false);
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline && (evidence.Store.ReadNpcAction(evidence.ActionId).FollowTransition!=null ||
                !evidence.Actor.gameObject.activeInHierarchy || evidence.Roads.OwnsDungeonFollow(evidence.Id)))yield return null;
            evidence.CheckIdentity();
            if(!evidence.Actor.gameObject.activeInHierarchy || evidence.Roads.OwnsDungeonFollow(evidence.Id))DescribeExteriorLanding(evidence);
            Need(evidence.Actor.gameObject.activeInHierarchy && !evidence.Roads.OwnsDungeonFollow(evidence.Id),"Exterior ownership/landing timed out.");
        }

        private static IEnumerator WaitForExteriorFloor(int map)
        {
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+35;
            while(Time.realtimeSinceStartup<deadline)
            {
                // Native placement is deferred; the old ready flag can still
                // describe the source frame. Require the actual exterior floor.
                yield return null;
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();Vector3 floor;
                if(!game.IsPlayerInside && game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer && directory && directory.MapID==map &&
                    StaticFloor(game.PlayerMotor.transform.position-Vector3.up,null,out floor))yield break;
            }
            Need(false,"Native dungeon exit did not reach its actual supported exterior floor.");
        }
        private static IEnumerator FindDungeonPath(Action<NavMeshPath> capture)
        {
            var dungeon=GameManager.Instance.PlayerEnterExit.Dungeon;var start=PlayerGround();NavMeshHit sampled;
            Need(NavMesh.SamplePosition(start,out sampled,0.4f,NavMesh.AllAreas),"Native player entry is not on loaded navigation.");
            var mesh=NavMesh.CalculateTriangulation();int attempts=0;
            for(int index=0;index+2<mesh.indices.Length && attempts<384;index+=3)
            {
                if(index%192==0)yield return null;
                var point=(mesh.vertices[mesh.indices[index]]+mesh.vertices[mesh.indices[index+1]]+mesh.vertices[mesh.indices[index+2]])/3;
                float direct=Vector3.Distance(sampled.position,point);if(direct<8 || direct>14 || Mathf.Abs(sampled.position.y-point.y)>0.5f)continue;
                attempts++;var path=new NavMeshPath();
                if(!NavMesh.CalculatePath(sampled.position,point,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete || path.corners.Length<2)continue;
                float length=0;for(int corner=1;corner<path.corners.Length;corner++)length+=Vector3.Distance(path.corners[corner-1],path.corners[corner]);
                if(length<8 || length>18 || !ClearSupportedRoute(path.corners,dungeon.transform,null))continue;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PATH: nativeStart="+sampled.position+"; nativeEnd="+point+"; metres="+length+"; corners="+path.corners.Length);
                capture(path);yield break;
            }
        }
        private static IEnumerator WalkObserver(Vector3[] points,CharacterController controller,Evidence evidence,bool tracking,float seconds=45,bool openOrdinaryDoors=false,bool holdForThreat=true)
        {
            evidence.ObserverHeldForThreat=holdForThreat;
            float deadline=Time.realtimeSinceStartup+seconds;
            var observerProgress=PlayerGround();float observerMovedAt=Time.realtimeSinceStartup;
            for(int index=1;index<points.Length;index++)
            {
                var waypoint=points[index];Vector3 measured;
                if(openOrdinaryDoors && StaticFloor(waypoint,GameManager.Instance.PlayerEnterExit.Dungeon.transform,out measured))waypoint=measured;
                while(!ObserverReachedWaypoint(waypoint,openOrdinaryDoors))
                {
                    if(evidence.Stage=="container-approach" && Environment.GetEnvironmentVariable("POTIB_DUNGEON_MULTITHREAT")=="1" &&
                        CurrentImmediateThreat(evidence) && ObservedDungeonThreats(evidence.Actor,12).Count>=2)
                    {yield return VerifyMultipleThreats(evidence);yield break;}
                    var activeThreat=RoadTravelActors.CurrentDungeonThreat(evidence.Actor);
                    Need(Time.realtimeSinceStartup<deadline,"Native observer movement timed out; stage="+evidence.Stage+
                        "; actualHostile="+(activeThreat?activeThreat.name:"none")+"; enemySuppressed=false; movementWindowSeconds="+seconds+
                        "; observer="+PlayerGround()+"; corner="+waypoint);
                    if(activeThreat && holdForThreat)
                    {observerMovedAt=Time.realtimeSinceStartup;evidence.Sample();yield return null;continue;}
                    if(openOrdinaryDoors)NativeNpcNavigationDoors.OpenAhead(PlayerGround(),waypoint);
                    MoveObserverToward(waypoint,controller,openOrdinaryDoors);
                    if(openOrdinaryDoors)
                    {
                        if(Vector3.Distance(observerProgress,PlayerGround())>=0.15f)
                        {observerProgress=PlayerGround();observerMovedAt=Time.realtimeSinceStartup;}
                        if(Time.realtimeSinceStartup-observerMovedAt>=6)
                        {
                            string details=ObserverBlockDetails(waypoint,controller);
                            SleepProbe.AimCamera(waypoint+Vector3.up*0.6f);
                            yield return new WaitForEndOfFrame();
                            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-container-observer-block.png"));
                            yield return new WaitForSecondsRealtime(0.5f);
                            Need(false,"Native observer approach blocked; "+details);
                        }
                    }
                    if(evidence.Actor.gameObject.activeInHierarchy && evidence.Store.ReadNpcAction(evidence.ActionId).FollowTransition==null)
                    {
                        if(!evidence.Tracking)evidence.ResetMotion();
                        evidence.Sample();
                    }
                    else Need(!tracking,"Previously visible follower disappeared during physical movement.");
                    yield return null;
                }
            }
        }
        private static bool ObserverReachedWaypoint(Vector3 waypoint,bool measuredFloor)
        {
            var delta=PlayerGround()-waypoint;
            // A capsule can rest on the higher edge of a step while the point
            // under a nearby mesh corner hits the lower tread. Preserve 0.4 m
            // horizontal arrival and the existing 0.6 m verified-floor bound.
            return measuredFloor?new Vector2(delta.x,delta.z).magnitude<=0.4f && Mathf.Abs(delta.y)<=0.6f:delta.magnitude<=0.4f;
        }
        private static string lastObserverMove;
        private static string ObserverBlockDetails(Vector3 target,CharacterController controller)
        {
            var ground=PlayerGround();var next=ground+(target-ground).normalized*0.1f;Vector3 floor;
            bool supported=StaticFloor(next,GameManager.Instance.PlayerEnterExit.Dungeon.transform,out floor);
            var blocker=BodyBlocker(ground,supported?floor:next,controller.transform,true,controller.radius+0.02f);
            var door=blocker?blocker.GetComponentInParent<DaggerfallActionDoor>():null;
            return "observer="+ground+"; corner="+target+"; floor="+supported+"; blocker="+(blocker?blocker.name:"none")+
                "; door="+(door?door.LoadID.ToString():"none")+"; locked="+(door && door.IsLocked)+
                "; closed="+(door && door.IsClosed)+"; actionOwned="+(door && door.GetComponent<DaggerfallAction>())+
                "; controllerHeight="+controller.height+"; radius="+controller.radius+
                "; nativeBodyClear="+ObserverBodiesClear(controller,(supported?floor:next)-ground)+
                "; nativeCollisions="+controller.collisionFlags+"; paused="+GameManager.IsGamePaused+
                "; controllerCenter="+controller.center+"; skin="+controller.skinWidth+"; step="+controller.stepOffset+
                "; lastMoves="+lastObserverMove;
        }
        private static void MoveObserverToward(Vector3 target,CharacterController controller,bool nativeControllerGeometry=false)
        {
            var ground=PlayerGround();var delta=target-ground;
            var side=Vector3.Cross(Vector3.up,delta).normalized;
            float stride=1.8f*Mathf.Min(Time.unscaledDeltaTime,0.05f);
            var root=GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon?
                GameManager.Instance.PlayerEnterExit.Dungeon.transform:null;
            // The observer must pass actual bodies, never drive through them.
            if(nativeControllerGeometry)lastObserverMove=string.Empty;
            foreach(var direction in new[]{delta.normalized,side,-side})
            {
                Vector3 floor;
                if(!StaticFloor(ground+direction*Mathf.Min(stride,delta.magnitude),root,out floor) ||
                    !(nativeControllerGeometry?ObserverBodiesClear(controller,floor-ground):
                    BodyClear(ground,floor,controller.transform,true,controller.radius+0.02f)))continue;
                var before=controller.transform.position;controller.Move(floor-ground);
                if(nativeControllerGeometry)lastObserverMove+="[requested="+(floor-ground).ToString("F5")+
                    "; actual="+(controller.transform.position-before).ToString("F5")+
                    "; toward="+Vector3.Dot(controller.transform.position-before,delta.normalized)+"]";
                // Native collision depenetration can move the capsule sideways
                // or backwards. Only progress in this attempted direction should
                // suppress the remaining physical avoidance attempts.
                if(!nativeControllerGeometry || Vector3.Dot(controller.transform.position-before,direction)>0.001f)break;
            }
        }
        private static bool ObserverBodiesClear(CharacterController controller,Vector3 delta)
        {
            // The native controller resolves static walls/steps using its real
            // 1.8 m capsule. A hard-coded NPC capsule falsely blocks low passages.
            // Retain explicit body checks even where native collision pairs are ignored.
            var bounds=controller.bounds;float radius=Mathf.Min(bounds.extents.x,bounds.extents.z);
            var offset=Vector3.up*Mathf.Max(0,bounds.extents.y-radius);
            foreach(var collider in Physics.OverlapCapsule(bounds.center-offset+delta,bounds.center+offset+delta,radius,~0,QueryTriggerInteraction.Collide))
                if(Blocks(collider,controller.transform,true) && NativeNavigationGeometry.IsDynamicNavigationBody(collider))return false;
            if(delta.sqrMagnitude>0.000001f)
                foreach(var hit in Physics.CapsuleCastAll(bounds.center-offset,bounds.center+offset,radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Collide))
                    if(Blocks(hit.collider,controller.transform,true) && NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider))return false;
            return true;
        }
        private static IEnumerator WaitFollower(Evidence evidence,float seconds)
        {
            evidence.ObserverHeldForThreat=true;
            float deadline=Time.realtimeSinceStartup+seconds,checkAt=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(Time.realtimeSinceStartup>=checkAt){checkAt=Time.realtimeSinceStartup+0.5f;evidence.CheckIdentity();}
                if(evidence.Actor.gameObject.activeInHierarchy && evidence.Store.ReadNpcAction(evidence.ActionId).FollowTransition==null)
                {
                    if(!evidence.Tracking)evidence.ResetMotion();evidence.Sample();
                    if(Vector3.Distance(evidence.Actor.transform.position-Vector3.up,PlayerGround())<=2.2f &&
                        !RoadTravelActors.CurrentDungeonThreat(evidence.Actor))yield break;
                }
                yield return null;
            }
            var job=evidence.Store.ReadNpcAction(evidence.ActionId);var native=evidence.Actor.GetComponent<NativeResidentNavigation>();
            throw new InvalidOperationException("Accepted native follower did not catch the observer; stage="+evidence.Stage+"; action="+job.FailureReason+
                "; actor="+(evidence.Actor.transform.position-Vector3.up)+"; observer="+PlayerGround()+"; measuredMetres="+evidence.Metres+
                "; recovery="+(job.CompanionRecovery==null?"none":job.CompanionRecovery.Phase.ToString())+
                "; actualHostile="+(RoadTravelActors.CurrentDungeonThreat(evidence.Actor)?RoadTravelActors.CurrentDungeonThreat(evidence.Actor).name:"none")+
                "; nativeReason="+(native?native.FailureReason:"no-native-navigation")+"; nativePath="+(native?native.PathState:"none")+
                "; suppressedExteriorReady="+GameManager.Instance.StreamingWorld.IsReady);
        }
        private static IEnumerator VerifyTalkContext(PeopleOfTheIliacBayMod host,ResidentActor actor,Evidence evidence)
        {
            var game=GameManager.Instance;game.TalkManager.TalkToMobileNPC(actor.Person);float deadline=Time.realtimeSinceStartup+10;
            DaggerfallTalkWindow talk=null;
            while(Time.realtimeSinceStartup<deadline)
            {talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;if(talk!=null && talk.IsSetup)break;yield return null;}
            Need(talk!=null && talk.IsSetup && game.TalkManager.MobileNPC==actor.Person,"Native dungeon Talk did not select the same person.");
            var opened=(bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen",Private).GetValue(host);
            if(!opened)typeof(PeopleOfTheIliacBayMod).GetMethod("BeginGroundedConversation",Private).Invoke(host,null);
            long revision=(long)typeof(PeopleOfTheIliacBayMod).GetField("conversationContextRequestRevision",Private).GetValue(host)+1;
            var snapshot=host.CaptureConversationContext(talk,"Native dungeon context verification; no utterance is submitted.",revision);bool outside;
            Need(snapshot.Binding.PersonId==evidence.Id && snapshot.NpcOutdoors.TryGetVerified(out outside) && !outside,
                "Dungeon Talk speaker/location snapshot is not grounded in the owned loaded native body.");
            bool commitment=false;foreach(var entry in snapshot.Commitments)if(entry.Id==evidence.ActionId && entry.ActionKind==NpcActionKind.Follow)commitment=true;
            Need(commitment,"Dungeon Talk lost its exact accepted Follow commitment.");
            string place=(string)typeof(PeopleOfTheIliacBayMod).GetMethod("ConversationPlaceContext",Private).Invoke(host,null);
            Need(place=="You are physically inside the currently loaded dungeon with the player. This establishes your current location, not ownership or knowledge of unexplored rooms.",
                "Dungeon Talk emitted the wrong verified place context.");
            // Invoke the normal full life builder too: it must not query absent
            // town schedules merely because the companion's dungeon is observed.
            typeof(PeopleOfTheIliacBayMod).GetMethod("CurrentConversationLifeContext",Private).Invoke(host,new object[]{actor.Person.NameNPC,50});
            DaggerfallUI.Instance.PopToHUD();yield return null;evidence.CheckIdentity();
        }

        private static Vector3 PlayerGround()
        {Vector3 ground;Need(StaticFloor(GameManager.Instance.PlayerMotor.transform.position-Vector3.up,null,out ground),"Native observer has no measured floor.");return ground;}
        private static bool StaticFloor(Vector3 point,Transform root,out Vector3 floor)
        {
            floor=Vector3.zero;float distance=float.MaxValue;
            foreach(var hit in Physics.RaycastAll(point+Vector3.up*0.6f,Vector3.down,1.2f,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider && (!root || hit.collider.transform.IsChildOf(root)) && !NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider) &&
                    hit.normal.y>=0.7f && Mathf.Abs(hit.point.y-point.y)<=0.6f && hit.distance<distance){floor=hit.point;distance=hit.distance;}
            return distance<float.MaxValue;
        }
        private static bool ClearSupportedRoute(Vector3[] points,Transform root,Transform actor)
        {
            for(int index=1;index<points.Length;index++)
            {
                if(!BodyClear(points[index-1],points[index],actor,true))return false;
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(points[index-1],points[index])/0.4f));
                for(int sample=0;sample<=count;sample++)if(!StaticFloor(Vector3.Lerp(points[index-1],points[index],sample/(float)count),root,out _))return false;
            }
            return true;
        }
        private static bool BodyClear(Vector3 from,Vector3 to,Transform own,bool ignorePlayer,float radius=CrowdMotion.BodyRadius)
        {return !BodyBlocker(from,to,own,ignorePlayer,radius);}
        private static Collider BodyBlocker(Vector3 from,Vector3 to,Transform own,bool ignorePlayer,float radius)
        {
            foreach(var collider in CollisionCandidates(from,to,radius))
                if(Blocks(collider,own,ignorePlayer))return collider;
            return null;
        }
        private static IEnumerable<Collider> CollisionCandidates(Vector3 from,Vector3 to,float radius)
        {
            foreach(var collider in Physics.OverlapCapsule(to+Vector3.up*0.78f,to+Vector3.up*1.6f,radius,~0,QueryTriggerInteraction.Collide))yield return collider;
            var delta=to-from;if(delta.sqrMagnitude<0.000001f)yield break;
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.78f,from+Vector3.up*1.6f,radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Collide))
                yield return hit.collider;
        }
        private static bool Blocks(Collider collider,Transform own,bool ignorePlayer)
        {return collider && (!own || !collider.transform.IsChildOf(own)) && !(ignorePlayer && collider.GetComponentInParent<PlayerMotor>()) &&
            (!collider.isTrigger || collider.GetComponentInParent<DaggerfallEntityBehaviour>() || collider.GetComponentInParent<PlayerMotor>());}
        private static string InventorySignature(NpcInventoryState inventory)
        {if(inventory==null)return "<missing>";var items=new List<string>();foreach(var item in inventory.Items)items.Add(item.Uid+":"+item.NativePayload);items.Sort(StringComparer.Ordinal);return inventory.WalletGold+"|"+string.Join("|",items.ToArray());}
        private static string ContractSignature(WorldStateStore store,NpcActionJob job)
        {
            string id=NpcPaidCompanionAction.ContractId(job);if(id==null)return "<ordinary>";
            var contract=store.ReadNpcServiceContracts().Read(id);Need(contract!=null,"Paid Follow contract missing.");
            return contract.ContractId+"|"+contract.NpcId+"|"+contract.PayerId+"|"+contract.Coverage+"|"+contract.Status+"|"+
                contract.BlockStartAt+"|"+contract.BlockEndAt+"|"+contract.FundedGold+"|"+contract.ReleasedGold+"|"+contract.RefundedGold+"|"+contract.Revision;
        }
        private static double Now(){var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
