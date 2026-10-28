using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors
    {
        private sealed class DungeonFollower
        {
            public string ActionId;
            public ResidentActor Actor;
            public int MapId;
            public StaticDoor Entrance;
            public bool AwaitingNativeEntry;
            public int ResumeAfterFrame;
            public float SaveAt;
            public double StalledSeconds;
            public Vector3 LastProgress;
            public List<NpcActionTarget> RecruitmentRoute;
            public bool OriginTravelPreparationStarted;
            public string DangerId;
            public DaggerfallEntityBehaviour DangerTarget;
            public float NextDangerSwingAt;
            public bool DangerRetreating;
            public string DangerMode;
        }
        private readonly Dictionary<string,DungeonFollower> dungeonFollowers=new Dictionary<string,DungeonFollower>();

        public bool OwnsDungeonFollow(string id)
        {
            // An interior reload has no dungeon body/binding yet. The saved
            // recovery still owns the person until its measured return ends.
            return id!=null && (dungeonFollowers.ContainsKey(id) ||
                source!=null && IsCompanionRecovering(source().ActiveNpcAction(id)));
        }

        public ResidentActor FindDungeonCompanion(string id)
        {DungeonFollower binding;return id!=null && dungeonFollowers.TryGetValue(id,out binding)?binding.Actor:null;}

        public bool IsLoadedDungeonCompanion(ResidentActor actor)
        {
            if(!actor || actor.Resident==null || !actor.gameObject.activeInHierarchy || source==null ||
                FindDungeonCompanion(actor.Resident.Id)!=actor)return false;
            var game=GameManager.Instance;var dungeon=game.PlayerEnterExit.Dungeon;
            var job=source().ReadNpcAction(dungeonFollowers[actor.Resident.Id].ActionId);
            return game.PlayerEnterExit.IsPlayerInsideDungeon && dungeon && actor.transform.parent==dungeon.transform &&
                job!=null && job.FollowTransition==null && NpcFollowTransition.IsDungeonSpace(job.LastPosition) &&
                job.LastPosition.SettlementId==dungeon.Summary.ID;
        }

        private static double DungeonNow()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}

        private static NpcActionTarget DungeonPoint(int map,Transform root,Vector3 world,bool inside)
        {
            var p=root.InverseTransformPoint(world);
            return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=map,
                WorldSpaceId=(inside?"dungeon:":"settlement:")+map.ToString(CultureInfo.InvariantCulture),X=p.x,Y=p.y,Z=p.z};
        }

        // Called before the town adapter: a genuine native dungeon door owns this
        // transition. Neither dialogue wording nor a requested coordinate can invoke it.
        public bool PrepareDungeonFollow(PlayerEnterExit.TransitionEventArgs args)
        {
            bool entering=args.TransitionType==PlayerEnterExit.TransitionType.ToDungeonInterior;
            bool leaving=args.TransitionType==PlayerEnterExit.TransitionType.ToDungeonExterior;
            if(!entering && !leaving)return false;
            if(source==null || !enabledForWorld())return true;
            var store=source();var game=GameManager.Instance;double now=DungeonNow();
            if(!ReferenceEquals(current,store)){Clear();current=store;}
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var dungeon=game.PlayerEnterExit.Dungeon;
            if(!directory || entering && (game.PlayerEnterExit.IsPlayerInside || args.StaticDoor.doorType!=DoorTypes.DungeonEntrance) ||
                leaving && (!dungeon || !game.PlayerEnterExit.IsPlayerInsideDungeon))return true;
            int map=entering?directory.MapID:dungeon.Summary.ID;
            if(directory.MapID!=map)return true;
            Transform root=entering?directory.transform:dungeon.transform;
            if(leaving)CaptureRecoveringDungeonDeparture(store,now,map,root);
            StaticDoor door=args.StaticDoor;
            if(leaving)
            {
                var exits=DaggerfallStaticDoors.FindDoorsInCollections(dungeon.StaticDoorCollections,DoorTypes.DungeonExit);
                if(exits==null || exits.Length==0)return true;
                Vector3 position;int index;
                if(!DaggerfallStaticDoors.FindClosestDoorToPlayer(game.PlayerMotor.transform.position,exits,out position,out index) ||
                    Vector3.Distance(position,game.PlayerMotor.transform.position)>4)return true;
                door=exits[index];
            }
            int budget=4;
            foreach(var candidate in store.PendingNpcActions())
            {
                if(candidate.Kind!=NpcActionKind.Follow || candidate.Target.PersonId!="player" ||
                    NpcActionExecutor.IsTerminal(candidate) || candidate.FollowTransition!=null || IsCompanionRecovering(candidate))continue;
                NpcServiceContract paidContract;
                if(NpcPaidCompanionAction.IsPaidFollowJob(candidate) && !NpcPaidCompanionTravel.TryReadActiveTravel(store,candidate,now,out paidContract))continue;
                DungeonFollower binding;dungeonFollowers.TryGetValue(candidate.ResidentId,out binding);
                var actor=entering?(streets?streets.FindActor(candidate.ResidentId):null):binding==null?null:binding.Actor;
                if(entering && !actor)actor=FindPaidCompanion(candidate.ResidentId);
                if(!actor || !actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                    streets && streets.QuestOwnsResident!=null && streets.QuestOwnsResident(actor.Resident.Id) ||
                    actor.IsConversing || actor.IsSeated || actor.IsSleeping || budget--<=0)continue;
                var actual=actor.transform.position-Vector3.up;
                var endpoint=StreetPopulation.FollowExteriorDoorGround(door);
                var route=VerifiedDungeonDoorRoute(actor,actual,endpoint,root,map,!entering);
                if(route==null)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_DOOR_ROUTE_REFUSED: person="+candidate.ResidentId+
                        "; entering="+entering+"; distance="+Vector3.Distance(actual,endpoint)+"; height="+Mathf.Abs(actual.y-endpoint.y)+
                        "; directApproachClear="+ClearNativeDoorApproach(actor,actual,endpoint)+
                        "; navigationReady="+(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready));
                    EndDungeonFollow(store,candidate,now,NpcActionObservation.Interrupted("No verified walking route reaches the native dungeon door."));continue;
                }
                var job=candidate;
                if(!store.SaveNpcActionPosition(job.Id,job.Revision,DungeonPoint(map,root,actual,!entering),false,now))continue;
                job=store.ReadNpcAction(job.Id);
                var target=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=map,
                    WorldSpaceId=(entering?"dungeon:":"settlement:")+map.ToString(CultureInfo.InvariantCulture)};
                if(!store.TryBeginNpcFollowTransition(job.Id,job.Revision,now,target,route,
                    DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed))continue;
                job=store.ReadNpcAction(job.Id);
                // The live body now has the exact doorway owner. Retire only the
                // exterior deferral marker; retain paid action and contract unchanged.
                if(job.PaidTravelHandoff!=null && !store.TryResumePaidTravel(job.Id,job.Revision,now))continue;
                actor.CaptureState();
                List<NpcActionTarget> originRoute=null;
                if(entering && job.CompanionRecovery!=null && NpcFollowTransition.SameSpace(job.LastPosition,job.CompanionRecovery.RecruitmentOrigin))
                {
                    var origin=job.CompanionRecovery.RecruitmentOrigin;
                    originRoute=MeasuredCompanionPath(actor,endpoint,root.TransformPoint(new Vector3((float)origin.X,(float)origin.Y,(float)origin.Z)),root,map,false);
                }
                dungeonFollowers[job.ResidentId]=new DungeonFollower {ActionId=job.Id,Actor=actor,MapId=map,
                    Entrance=door,AwaitingNativeEntry=entering,RecruitmentRoute=originRoute};
            }
            return true;
        }

        private static List<NpcActionTarget> VerifiedDungeonDoorRoute(ResidentActor actor,Vector3 actual,Vector3 door,Transform root,int map,bool inside)
        {
            const float maximumRouteMetres=32;
            var world=NativeNavigationWorld.Current;NavMeshHit start,end;
            // Dungeon exteriors can have no city NavMesh. The only fallback is
            // a fully measured, supported, collision-clear approach to this door.
            // Use the same bounded reach as the mesh route: a follower normally
            // trails a player who can activate the door from several metres away.
            if(!inside && Vector3.Distance(actual,door)<=maximumRouteMetres && Mathf.Abs(actual.y-door.y)<=0.5f &&
                ClearNativeDoorApproach(actor,actual,door))
                return new List<NpcActionTarget> {DungeonPoint(map,root,actual,false),DungeonPoint(map,root,door,false)};
            if(!world || !world.Ready || world.Scene!=(inside?"dungeon-":"city-")+map || Vector3.Distance(actual,door)>maximumRouteMetres ||
                !NavMesh.SamplePosition(actual,out start,0.4f,NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(door,out end,0.4f,NavMesh.AllAreas))return null;
            var path=new NavMeshPath();
            if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete ||
                path.corners.Length<2 || path.corners.Length>128)return null;
            var route=new List<NpcActionTarget>();route.Add(DungeonPoint(map,root,actual,inside));
            for(int i=1;i<path.corners.Length;i++)route.Add(DungeonPoint(map,root,path.corners[i],inside));
            return route;
        }

        private static bool ClearNativeDoorApproach(ResidentActor actor,Vector3 from,Vector3 to)
        {
            int count=Math.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/0.25f));var previous=from;
            for(int i=1;i<=count;i++)
            {
                var point=Vector3.Lerp(from,to,i/(float)count);RaycastHit floor;
                if(!Physics.Raycast(point+Vector3.up*0.5f,Vector3.down,out floor,1,~0,QueryTriggerInteraction.Ignore) ||
                    floor.normal.y<0.7f || NativeNavigationGeometry.IsDynamicNavigationBody(floor.collider))return false;
                foreach(var collider in Physics.OverlapCapsule(point+Vector3.up*0.78f,point+Vector3.up*1.6f,
                    CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Collide))
                    if(BlocksDoorApproach(collider,actor))return false;
                var delta=point-previous;
                foreach(var hit in Physics.CapsuleCastAll(previous+Vector3.up*0.78f,previous+Vector3.up*1.6f,
                    CrowdMotion.BodyRadius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Collide))
                    if(BlocksDoorApproach(hit.collider,actor))return false;
                previous=point;
            }
            return true;
        }
        private static bool BlocksDoorApproach(Collider collider,ResidentActor actor)
        {
            // The observer leaves in this very native transition. Other bodies
            // still block the bounded continuation; the source actor ignores itself.
            return collider && !collider.transform.IsChildOf(actor.transform) && !collider.GetComponentInParent<PlayerMotor>() &&
                (!collider.isTrigger || collider.GetComponentInParent<DaggerfallEntityBehaviour>());
        }

        private void CompleteNativeDungeonEntry(PlayerEnterExit.TransitionEventArgs args)
        {
            var dungeon=args.DaggerfallDungeon;
            if(!dungeon || !dungeon.StartMarker)return;
            foreach(var pair in new List<KeyValuePair<string,DungeonFollower>>(dungeonFollowers))
            {
                var binding=pair.Value;
                if(!binding.AwaitingNativeEntry)continue;
                if(binding.MapId!=dungeon.Summary.ID || !binding.Entrance.Equals(args.StaticDoor))
                {
                    var job=source().ReadNpcAction(binding.ActionId);
                    if(job!=null && !NpcActionExecutor.IsTerminal(job))EndDungeonFollow(source(),job,DungeonNow(),
                        NpcActionObservation.Interrupted("The native dungeon entry resolved to a different door or map."));
                    if(streets && binding.Actor)streets.ReleaseTransferredActor(binding.Actor);
                    RemoveDungeonFollower(pair.Key);continue;
                }
                binding.AwaitingNativeEntry=false;
                if(!binding.Actor)continue;
                // Native entry succeeded. Preserve the same person and native item
                // objects while the bounded source route is temporarily unobserved.
                if(streets)streets.ReleaseTransferredActor(binding.Actor);
                paidCompanions.Remove(pair.Key);paidActions.Remove(pair.Key);retiredPaidCompanions.Remove(pair.Key);
                fastTravelFollowers.Remove(pair.Key);
                binding.Actor.TransferOwner(transform,navigation);binding.Actor.gameObject.SetActive(false);
            }
        }

        private void AbortNativeDungeonFollow(PlayerEnterExit.TransitionEventArgs args)
        {
            if(args.TransitionType!=PlayerEnterExit.TransitionType.ToDungeonInterior &&
                args.TransitionType!=PlayerEnterExit.TransitionType.ToDungeonExterior)return;
            if(source==null)return;var store=source();double now=DungeonNow();
            foreach(var pair in new List<KeyValuePair<string,DungeonFollower>>(dungeonFollowers))
            {
                var binding=pair.Value;var job=store.ReadNpcAction(binding.ActionId);
                if(job==null || job.FollowTransition==null)continue;
                if(store.TryResumeNpcFollowSource(job.Id,job.Revision,now) && binding.Actor)
                {
                    var game=GameManager.Instance;var root=NpcFollowTransition.IsDungeonSpace(job.LastPosition)?
                        game.PlayerEnterExit.Dungeon.transform:game.StreamingWorld.GetCurrentBuildingDirectory().transform;
                    job=store.ReadNpcAction(job.Id);
                    store.SaveNpcActionPosition(job.Id,job.Revision,DungeonPoint(binding.MapId,root,
                        binding.Actor.transform.position-Vector3.up,NpcFollowTransition.IsDungeonSpace(job.LastPosition)),false,now);
                    binding.Actor.gameObject.SetActive(true);
                }
                if(binding.AwaitingNativeEntry)dungeonFollowers.Remove(pair.Key);
            }
        }

        private void CompleteNativeDungeonExit(PlayerEnterExit.TransitionEventArgs args)
        {
            if(source==null)return;var store=source();double now=DungeonNow();
            foreach(var binding in dungeonFollowers.Values)
            {
                binding.ResumeAfterFrame=Time.frameCount+1;
                if(binding.Actor){binding.Actor.TransferOwner(transform,navigation);binding.Actor.gameObject.SetActive(false);}
                var job=store.ReadNpcAction(binding.ActionId);
                if(job!=null && job.CompanionEnterprise!=null && job.CompanionEnterprise.Active &&
                    job.CompanionEnterprise.Kind==NpcCompanionEnterpriseKind.DungeonCrawl)
                {store.TryEndCompanionEnterprise(job.ResidentId,job.CompanionEnterprise.Id,now);job=store.ReadNpcAction(binding.ActionId);}
                if(IsCompanionRecovering(job))continue;
                if(job!=null && !NpcActionExecutor.IsTerminal(job) && job.FollowTransition==null)
                    EndDungeonFollow(store,job,now,NpcActionObservation.Interrupted("No verified source route reaches the native dungeon exit."));
            }
        }

        private void UpdateDungeonFollowers(WorldStateStore store,double now)
        {
            AdvanceUnobservedDungeonRecoveries(store,now);
            var game=GameManager.Instance;bool inside=game.PlayerEnterExit.IsPlayerInsideDungeon;
            var dungeon=game.PlayerEnterExit.Dungeon;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(inside && !dungeon || !inside && (game.IsPlayerInside || !directory))return;
            int map=inside?dungeon.Summary.ID:directory.MapID;
            Transform root=inside?dungeon.transform:directory.transform;
            foreach(var job in store.PendingNpcActions())
            {
                if(job.Kind!=NpcActionKind.Follow || NpcActionExecutor.IsTerminal(job) && !IsCompanionRecovering(job) || dungeonFollowers.ContainsKey(job.ResidentId))continue;
                bool transit=job.FollowTransition!=null && (NpcFollowTransition.IsDungeonSpace(job.LastPosition) ||
                    NpcFollowTransition.IsDungeonSpace(job.FollowTransition.Destination));
                if(IsCompanionRecovering(job) || (transit || inside && NpcFollowTransition.IsDungeonSpace(job.LastPosition)) && job.LastPosition.SettlementId==map)
                    dungeonFollowers[job.ResidentId]=new DungeonFollower {ActionId=job.Id,MapId=job.LastPosition.SettlementId,ResumeAfterFrame=Time.frameCount+1};
            }
            foreach(var pair in new List<KeyValuePair<string,DungeonFollower>>(dungeonFollowers))
            {
                var binding=pair.Value;var job=store.ReadNpcAction(binding.ActionId);var actor=binding.Actor;
                if(binding.AwaitingNativeEntry || Time.frameCount<=binding.ResumeAfterFrame)continue;
                if(IsCompanionRecovering(job))
                {UpdateCompanionRecovery(store,binding,job,now,inside,map,root);continue;}
                if(job!=null && job.CompanionRecovery!=null && job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.ResumedRoutine)
                {RemoveDungeonFollower(pair.Key);continue;}
                if(binding.MapId!=map || job==null)
                {
                    if(job!=null && !NpcActionExecutor.IsTerminal(job))EndDungeonFollow(store,job,now,
                        NpcActionObservation.Interrupted("The player left the native dungeon and its exterior map."));
                    RemoveDungeonFollower(pair.Key);continue;
                }
                var resident=store.ResolveResident(job.ResidentId);
                if(!NpcActionExecutor.IsTerminal(job) && (now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt ||
                    resident==null || !resident.IsAlive || resident.IsQuestProtected ||
                    streets && streets.QuestOwnsResident!=null && streets.QuestOwnsResident(job.ResidentId)))
                {EndDungeonFollow(store,job,now,NpcActionObservation.Interrupted("Dungeon following ended at its agreed limit or native ownership interruption."));job=store.ReadNpcAction(job.Id);}
                if(NpcActionExecutor.IsTerminal(job))
                {
                    string ignored;StreetPopulation.SettlePaidCompanionAction(store,job,
                        job.Phase==NpcActionPhase.Cancelled?NpcServiceEndReason.PlayerDismissal:NpcServiceEndReason.NpcResigned,out ignored);
                    if(actor && actor.gameObject.activeInHierarchy && inside)
                    {var stopped=actor.GetComponent<NativeResidentNavigation>();if(stopped)stopped.Pause();
                        if(actor.Resident.IsAlive)actor.HoldPhysicalPosition();continue;}
                    RemoveDungeonFollower(pair.Key);continue;
                }
                var world=NativeNavigationWorld.Current;
                if(!NativeDungeonSceneReadiness.AllowsAdvance(inside,world && world.Ready && world.Scene=="dungeon-"+map,
                    game.StreamingWorld.IsInit,game.StreamingWorld.IsReady,game.StreamingWorld.IsRepositioningPlayer))continue;
                if(job.FollowTransition!=null)
                {
                    bool returnedToSource=NpcFollowTransition.IsDungeonSpace(job.LastPosition)==inside;
                    Vector3 landing;
                    if(returnedToSource)
                    {
                        var elapsed=job.FollowTransition.SourcePosition(now);
                        var point=root.TransformPoint(new Vector3((float)elapsed.X,(float)elapsed.Y,(float)elapsed.Z));
                        if(!VerifiedDungeonFloor(point,root,out landing) || !DungeonLandingClear(landing))continue;
                    }
                    else if(now<job.FollowTransition.ArrivesAt || !TryDungeonLanding(inside,map,out landing))continue;
                    // Ordinary town residents return to their existing town plan;
                    // paid or fast-travel followers return to their existing
                    // road owner, including an unpaid agreed Follow.
                    NpcServiceContract contract;
                    bool paid=NpcPaidCompanionTravel.TryReadActiveTravel(store,job,now,out contract);
                    bool fastTravel=job.FastTravelFollowOwner && NpcCompanionFastTravel.CanFollow(store,job,now,true);
                    if(!inside && !paid && !fastTravel && (!streets || !streets.CanReceiveDungeonFollower(job.ResidentId,store,map)))continue;
                    if(inside && !returnedToSource && job.CompanionRecovery!=null)
                    {
                        // The synchronous native entry event precedes our loaded
                        // dungeon mesh. Capture its real paired portal only after
                        // scene readiness, before the follower becomes visible.
                        Vector3 exitPoint;
                        if(!TryActualInteriorEntrance(game.PlayerEnterExit.Dungeon,out exitPoint) ||
                            !store.TryCaptureCompanionDungeonEntrance(job.Id,job.Revision,now,
                                DungeonPoint(map,root,exitPoint,true),
                                job.FollowTransition.SourceRoute[job.FollowTransition.SourceRoute.Count-1]))continue;
                        job=store.ReadNpcAction(job.Id);
                        if(binding.RecruitmentRoute!=null)
                        {
                            if(!store.TryCaptureCompanionRecruitmentRoute(job.Id,job.Revision,now,binding.RecruitmentRoute))continue;
                        }
                        else if(!binding.OriginTravelPreparationStarted)
                        {binding.OriginTravelPreparationStarted=true;StartCoroutine(PrepareCompanionOriginTravel(store,binding));}
                        job=store.ReadNpcAction(job.Id);
                    }
                    if(returnedToSource ? !store.TryResumeNpcFollowSource(job.Id,job.Revision,now) :
                        !store.TryArriveNpcFollowTransition(job.Id,job.Revision,now,DungeonPoint(map,root,landing,inside)))continue;
                    job=store.ReadNpcAction(job.Id);
                    if(returnedToSource)
                    {
                        if(!store.SaveNpcActionPosition(job.Id,job.Revision,DungeonPoint(map,root,landing,inside),false,now))continue;
                        job=store.ReadNpcAction(job.Id);
                    }
                    if(!actor)actor=CreateDungeonFollower(store,job);
                    binding.Actor=actor;actor.TransferOwner(inside?root:transform,navigation);
                    var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.Pause();
                    actor.GetComponent<CrowdMotion>().RestorePlacement(landing);actor.gameObject.SetActive(true);
                }
                else if(!actor)
                {
                    if(!inside || !NpcFollowTransition.IsDungeonSpace(job.LastPosition))continue;
                    var point=root.TransformPoint(new Vector3((float)job.LastPosition.X,(float)job.LastPosition.Y,(float)job.LastPosition.Z));
                    Vector3 landing;if(!VerifiedDungeonFloor(point,root,out landing) || !DungeonLandingClear(landing))continue;
                    actor=CreateDungeonFollower(store,job);binding.Actor=actor;
                    actor.TransferOwner(root,navigation);
                    actor.GetComponent<CrowdMotion>().RestorePlacement(landing);actor.gameObject.SetActive(true);
                }
                if(!inside)
                {
                    if(streets && streets.TryReceiveDungeonFollower(actor,store,map) || TryDeferPaidCompanion(actor,store,now))
                        dungeonFollowers.Remove(pair.Key);
                    continue;
                }
                if(actor.IsConversing || GameManager.IsGamePaused)continue;
                var entity=actor.GetComponent<DaggerfallEntityBehaviour>();
                if(entity && entity.Entity!=null && entity.Entity.IsParalyzed){actor.HoldPhysicalPosition();continue;}
                if(AdvanceDungeonDanger(store,binding,job,now))
                {
                    binding.StalledSeconds=0;binding.LastProgress=actor.transform.position-Vector3.up;
                    if(Time.unscaledTime>=binding.SaveAt)
                    {binding.SaveAt=Time.unscaledTime+0.5f;job=store.ReadNpcAction(job.Id);store.SaveNpcActionPosition(job.Id,job.Revision,
                        DungeonPoint(map,root,actor.transform.position-Vector3.up,true),false,now);}
                    continue;
                }
                var actual=actor.transform.position-Vector3.up;var player=NativeDungeonObserverGround.Read();
                Vector3 lootTarget;
                bool transferred;
                bool looting=NativeCompanionLootClaims.TryLootOrApproach(store,actor,out lootTarget,out transferred);
                if(looting)player=lootTarget;
                // A committed native item transfer is useful progress while
                // standing. Do not walk toward its raised billboard or count
                // a long stack transfer as a failed walking route.
                bool close=transferred;
                var destination=transferred?actual:NativeDungeonFollowTarget.Resolve(actor,player,out close,looting);
                // Fail closed: a rejected native path must not fall through to a
                // direct crowd route through dungeon walls. Place's existing owner
                // can still use its bounded, independently verified jump fallback.
                actor.Place(actual,player-actual,standing:close,navigationDestination:destination);
                ObserveDungeonFollowProgress(store,binding,job,now,close);
                if(Time.unscaledTime>=binding.SaveAt)
                {binding.SaveAt=Time.unscaledTime+0.5f;store.SaveNpcActionPosition(job.Id,job.Revision,
                    DungeonPoint(map,root,actor.transform.position-Vector3.up,true),false,now);}
            }
        }

        private ResidentActor CreateDungeonFollower(WorldStateStore store,NpcActionJob job)
        {
            var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,transform);
            var actor=body.AddComponent<ResidentActor>();actor.Initialize(store.ResolveResident(job.ResidentId),0,navigation,inventoryStore:store);
            return actor;
        }

        private static bool TryDungeonLanding(bool inside,int map,out Vector3 landing)
        {
            landing=Vector3.zero;var game=GameManager.Instance;
            if(inside)
            {
                var dungeon=game.PlayerEnterExit.Dungeon;
                return dungeon && dungeon.Summary.ID==map && dungeon.StartMarker &&
                    VerifiedDungeonFloor(dungeon.StartMarker.transform.position,dungeon.transform,out landing) && DungeonLandingClear(landing);
            }
            var location=game.StreamingWorld.CurrentPlayerLocationObject;
            if(!location || location.Summary.MapID!=map)return false;
            // Same authoritative door selection as StreamingWorld.PositionPlayerToDungeonExit.
            bool found=false;StaticDoor chosen=default(StaticDoor);float lowest=float.MaxValue;
            foreach(var door in DaggerfallStaticDoors.FindDoorsInCollections(location.StaticDoorCollections??new DaggerfallStaticDoors[0],DoorTypes.DungeonEntrance))
                    if(door.doorType==DoorTypes.DungeonEntrance && DaggerfallStaticDoors.GetDoorPosition(door).y<lowest)
                    {lowest=DaggerfallStaticDoors.GetDoorPosition(door).y;chosen=door;found=true;}
            return found && VerifiedExteriorDungeonDoorFloor(chosen,out landing) && DungeonLandingClear(landing);
        }

        private static bool VerifiedExteriorDungeonDoorFloor(StaticDoor door,out Vector3 floor)
        {
            // Some native keep entrances have a model lower edge buried below
            // terrain. A ray starting at that edge cannot see the actual ground.
            // Project only the selected doorway's real vertical span, at its
            // existing exterior offset; never substitute another entrance.
            var bottom=StreetPopulation.FollowExteriorDoorGround(door);
            if(VerifiedDungeonFloor(bottom,null,out floor))return true;
            var top=door;top.centre+=Vector3.up*(Mathf.Abs(door.size.y)*0.5f);
            float upper=DaggerfallStaticDoors.GetDoorPosition(top).y+0.5f;
            var origin=new Vector3(bottom.x,upper,bottom.z);RaycastHit hit;floor=Vector3.zero;
            if(upper<=bottom.y || !Physics.Raycast(origin,Vector3.down,out hit,upper-bottom.y+0.6f,~0,QueryTriggerInteraction.Ignore) ||
                !hit.collider || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider) || hit.normal.y<0.7f)return false;
            floor=hit.point;return true;
        }

        private static bool VerifiedDungeonFloor(Vector3 expected,Transform root,out Vector3 floor)
        {
            floor=Vector3.zero;RaycastHit hit;NavMeshHit mesh;
            if(!Physics.Raycast(expected+Vector3.up*0.5f,Vector3.down,out hit,1.5f,~0,QueryTriggerInteraction.Ignore) ||
                !hit.collider || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider) || hit.normal.y<0.7f ||
                GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon && !hit.collider.transform.IsChildOf(root) ||
                Mathf.Abs(hit.point.y-expected.y)>0.6f)return false;
            if(!GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon){floor=hit.point;return true;}
            if(!NavMesh.SamplePosition(hit.point,out mesh,0.4f,NavMesh.AllAreas))return false;
            floor=mesh.position;return true;
        }
        private static bool DungeonLandingClear(Vector3 ground)
        {
            foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.78f,ground+Vector3.up*1.6f,
                CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Collide))
                if(collider && (!collider.isTrigger || collider.GetComponentInParent<DaggerfallEntityBehaviour>() ||
                    collider.GetComponentInParent<MobilePersonNPC>()))return false;
            return true;
        }

        public bool TryStopDungeonFollow(string id,out string reason)
        {
            reason=null;DungeonFollower binding;
            if(source==null || !dungeonFollowers.TryGetValue(id,out binding))return false;
            var store=source();var job=store.ReadNpcAction(binding.ActionId);double now=DungeonNow();
            if(job!=null && job.FollowTransition==null && binding.Actor && binding.Actor.gameObject.activeInHierarchy &&
                GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon)
            {
                var dungeon=GameManager.Instance.PlayerEnterExit.Dungeon;
                if(dungeon && store.SaveNpcActionPosition(job.Id,job.Revision,DungeonPoint(binding.MapId,dungeon.transform,
                    binding.Actor.transform.position-Vector3.up,true),false,now))job=store.ReadNpcAction(job.Id);
            }
            if(job==null || !EndDungeonFollow(store,job,now,NpcActionObservation.Cancelled("The player asked the companion to stop.")))return false;
            string ignored;StreetPopulation.SettlePaidCompanionAction(store,store.ReadNpcAction(job.Id),NpcServiceEndReason.PlayerDismissal,out ignored);
            var task=store.ActiveNpcTask(id);if(task!=null)store.CancelNpcTask(task.Id,task.Revision,now,"The player asked the companion to stop.");
            reason="Stopped and saved. The companion remains at the observed dungeon position; no return route is claimed.";return true;
        }
        private static bool EndDungeonFollow(WorldStateStore store,NpcActionJob job,double now,NpcActionObservation observation)
        {
            if(job.PaidTravelHandoff!=null)
            {
                bool released=now>=job.FinishAt?store.TryReleaseExpiredPaidTravel(job.Id,job.Revision,now):
                    store.TryResumePaidTravel(job.Id,job.Revision,now);
                if(!released)return false;job=store.ReadNpcAction(job.Id);
            }
            if(!store.ObserveNpcAction(job.Id,job.Revision,now,observation))return false;
            job=store.ReadNpcAction(job.Id);string error;
            if(!StreetPopulation.SettlePaidCompanionAction(store,job,
                job.Phase==NpcActionPhase.Cancelled?NpcServiceEndReason.PlayerDismissal:NpcServiceEndReason.NpcResigned,out error))
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_SETTLEMENT: "+error);
            return true;
        }
        private void RemoveDungeonFollower(string id)
        {
            DungeonFollower binding;
            if(dungeonFollowers.TryGetValue(id,out binding) && binding.Actor)
            {binding.Actor.CaptureState();binding.Actor.gameObject.SetActive(false);Destroy(binding.Actor.gameObject);}
            dungeonFollowers.Remove(id);
        }
        private void ClearDungeonFollowers()
        {foreach(var id in new List<string>(dungeonFollowers.Keys))RemoveDungeonFollower(id);}
        private void CaptureDungeonFollowers()
        {
            if(source==null)return;var store=source();var dungeon=GameManager.Instance.PlayerEnterExit.Dungeon;
            foreach(var pair in dungeonFollowers)
            {
                var binding=pair.Value;var actor=binding.Actor;if(!actor)continue;actor.CaptureState();
                var job=store.ReadNpcAction(binding.ActionId);
                if(dungeon && actor.gameObject.activeInHierarchy && job!=null && job.FollowTransition==null && NpcFollowTransition.IsDungeonSpace(job.LastPosition))
                    store.SaveNpcActionPosition(job.Id,job.Revision,DungeonPoint(binding.MapId,dungeon.transform,actor.transform.position-Vector3.up,true),job.ReturningToRoutine,DungeonNow());
            }
        }
    }
}
