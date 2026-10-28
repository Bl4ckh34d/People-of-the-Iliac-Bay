using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors
    {
        private static bool IsCompanionRecovering(NpcActionJob job)
        {return job!=null && job.CompanionRecovery!=null && job.CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.Following && job.CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.ResumedRoutine;}

        private void ObserveDungeonFollowProgress(WorldStateStore store,DungeonFollower binding,NpcActionJob job,double now,bool close)
        {
            var actor=binding.Actor;var actual=actor.transform.position-Vector3.up;
            var jump=actor.GetComponent<NativeDungeonJumpTraversal>();
            var traversal=actor.GetComponent<NativeDungeonSurfaceTraversal>();
            bool progress=close || jump && jump.Active || traversal && traversal.Active || Vector3.Distance(actual,binding.LastProgress)>=0.15f;
            binding.StalledSeconds=NpcCompanionWaitPolicy.UnpausedStallSeconds(binding.StalledSeconds,Time.unscaledDeltaTime,
                !GameManager.IsGamePaused && !actor.IsConversing,progress);
            if(progress){binding.LastProgress=actual;return;}
            if(binding.StalledSeconds<6 || actor.IsConversing || GameManager.IsGamePaused)return;
            job=store.ReadNpcAction(job.Id);
            if(!store.SaveNpcActionPosition(job.Id,job.Revision,DungeonPoint(binding.MapId,GameManager.Instance.PlayerEnterExit.Dungeon.transform,actual,true),false,now))return;
            job=store.ReadNpcAction(job.Id);
            if(store.TryBeginCompanionObstacleWait(job.Id,job.Revision,now))
            {
                var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.Pause();actor.HoldPhysicalPosition();
                job=store.ReadNpcAction(job.Id);CaptureCompanionRecoveryRoute(store,binding,job,now,GameManager.Instance.PlayerEnterExit.Dungeon.transform,true);
            }
        }

        private void UpdateCompanionRecovery(WorldStateStore store,DungeonFollower binding,NpcActionJob job,double now,bool inside,int map,Transform root)
        {
            var value=job.CompanionRecovery;
            var resident=store.ResolveResident(job.ResidentId);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected || streets && streets.QuestOwnsResident!=null && streets.QuestOwnsResident(job.ResidentId))
            {if(binding.Actor){binding.Actor.CaptureState();binding.Actor.gameObject.SetActive(false);Destroy(binding.Actor.gameObject);binding.Actor=null;}return;}
            // Recovery is a physical obligation, not a paid-day renewal. Settle
            // the original service at its existing deadline, then retain its body.
            if(!NpcActionExecutor.IsTerminal(job) && (now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt))
            {EndDungeonFollow(store,job,now,NpcActionObservation.Interrupted("The agreed companion service ended; the recorded obstacle return continues."));job=store.ReadNpcAction(job.Id);value=job.CompanionRecovery;}
            if(value.Phase==NpcCompanionRecoveryPhase.WaitingInside && now>=value.WaitUntil)
            {store.TryAdvanceCompanionRecovery(job.Id,job.Revision,now,NpcCompanionRecoveryPhase.ReturningToExit);job=store.ReadNpcAction(job.Id);value=job.CompanionRecovery;}
            if(value.Phase==NpcCompanionRecoveryPhase.ReturningToExit && job.CompanionEnterprise!=null && job.CompanionEnterprise.Active)
            {store.TryEndCompanionEnterprise(job.ResidentId,job.CompanionEnterprise.Id,now);job=store.ReadNpcAction(job.Id);value=job.CompanionRecovery;}
            if(value.Phase==NpcCompanionRecoveryPhase.WaitingOutside && now>=value.WaitUntil)
            {store.TryAdvanceCompanionRecovery(job.Id,job.Revision,now,NpcCompanionRecoveryPhase.ReturningToOrigin);job=store.ReadNpcAction(job.Id);value=job.CompanionRecovery;}
            if(value.OverlandStartedAt>0 && value.MeasuredRoute==null && !NpcFollowTransition.SameSpace(job.LastPosition,value.RecruitmentArrival))return;
            bool sameSpace=job.LastPosition!=null && job.LastPosition.SettlementId==map && job.LastPosition.Kind==NpcActionTargetKind.WorldPoint &&
                job.LastPosition.WorldSpaceId==(inside?"dungeon:":"settlement:")+map;
            if(!sameSpace)
            {
                if(binding.Actor && binding.Actor.gameObject.activeInHierarchy)
                {binding.Actor.CaptureState();binding.Actor.gameObject.SetActive(false);}
                return;
            }
            if(!binding.Actor)
            {
                var expected=root.TransformPoint(new Vector3((float)job.LastPosition.X,(float)job.LastPosition.Y,(float)job.LastPosition.Z));Vector3 ground;
                if(VisibleOverlandReconstruction(value,expected))return;
                if(!VerifiedDungeonFloor(expected,root,out ground) || !DungeonLandingClear(ground))return;
                binding.Actor=CreateDungeonFollower(store,job);binding.Actor.TransferOwner(root,navigation);
                binding.Actor.GetComponent<CrowdMotion>().RestorePlacement(ground);binding.Actor.gameObject.SetActive(true);
            }
            var actor=binding.Actor;
            if(!actor.gameObject.activeInHierarchy)
            {
                var expected=root.TransformPoint(new Vector3((float)job.LastPosition.X,(float)job.LastPosition.Y,(float)job.LastPosition.Z));Vector3 ground;
                if(VisibleOverlandReconstruction(value,expected))return;
                if(!VerifiedDungeonFloor(expected,root,out ground) || !DungeonLandingClear(ground))return;
                actor.TransferOwner(root,navigation);actor.GetComponent<CrowdMotion>().RestorePlacement(ground);actor.gameObject.SetActive(true);
            }
            if(actor.IsConversing || GameManager.IsGamePaused)return;
            var player=NativeDungeonObserverGround.Read();
            if(!NpcActionExecutor.IsTerminal(job) && Vector3.Distance(actor.transform.position-Vector3.up,player)<=1.8f)
            {
                var approach=new NavMeshPath();float length=0;
                bool reachable=NavMesh.CalculatePath(actor.transform.position-Vector3.up,player,NavMesh.AllAreas,approach) && approach.status==NavMeshPathStatus.PathComplete;
                for(int index=1;index<approach.corners.Length;index++)length+=Vector3.Distance(approach.corners[index-1],approach.corners[index]);
                if(NpcFollowArrival.CanHold(Vector3.Distance(actor.transform.position-Vector3.up,player),reachable,length) &&
                    store.TryResumeCompanionFollow(job.Id,job.Revision,now))
                {binding.StalledSeconds=0;binding.LastProgress=actor.transform.position-Vector3.up;return;}
            }
            if(!value.NoticeDelivered)DeliverCompanionRecoveryNotice(store,job,actor,now);
            if(GameManager.IsGamePaused || actor.IsConversing)return;
            if(value.Phase==NpcCompanionRecoveryPhase.ReturningToOrigin && value.RecruitmentArrival!=null && value.OverlandStartedAt==0 &&
                NpcFollowTransition.SameSpace(job.LastPosition,value.ExteriorEntrance))
            {DepartVisibleCompanionOverland(store,binding,store.ReadNpcAction(job.Id),now);if(binding.Actor)SaveCompanionRecoveryPosition(store,binding,now,root,inside);return;}
            if(value.Phase==NpcCompanionRecoveryPhase.WaitingInside || value.Phase==NpcCompanionRecoveryPhase.WaitingOutside)
            {
                if(value.MeasuredRoute==null){job=store.ReadNpcAction(job.Id);CaptureCompanionRecoveryRoute(store,binding,job,now,root,inside);}
                var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.Pause();actor.HoldPhysicalPosition();return;
            }
            if(value.MeasuredRoute!=null)
            {
                // A saved night arrival remains outside the real gate until its
                // native opening; retain this route if the player leaves again.
                if(now<value.MeasuredRoute.StartedAt){actor.HoldPhysicalPosition();return;}
                var world=NativeNavigationWorld.Current;if(!world || !world.Ready || world.Scene!=(inside?"dungeon-":"city-")+map)return;
                job=store.ReadNpcAction(job.Id);store.TryClearCompanionRecoveryRoute(job.Id,job.Revision,now);job=store.ReadNpcAction(job.Id);value=job.CompanionRecovery;
            }
            var target=value.Phase==NpcCompanionRecoveryPhase.ReturningToExit?value.InteriorEntrance:value.RecruitmentArrival??value.RecruitmentOrigin;
            // A loaded native path must prove the whole return. Different towns
            // and unloaded recruitment interiors remain unresolved, never arrived.
            if(!NpcFollowTransition.SameSpace(target,job.LastPosition))return;
            Vector3 goal=root.TransformPoint(new Vector3((float)target.X,(float)target.Y,(float)target.Z));
            Vector3 actual=actor.transform.position-Vector3.up;
            if(Vector3.Distance(actual,goal)>0.4f)
            {
                Vector3 destination=goal;
                if(inside){bool close;destination=NativeDungeonFollowTarget.Resolve(actor,goal,out close,true);}
                else
                {
                    var path=new NavMeshPath();
                    if(!NavMesh.CalculatePath(actual,goal,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)return;
                }
                actor.Place(actual,goal-actual,navigationDestination:destination);
                SaveCompanionRecoveryPosition(store,binding,now,root,inside);return;
            }
            SaveCompanionRecoveryPosition(store,binding,now,root,inside,true);job=store.ReadNpcAction(job.Id);
            if(value.Phase==NpcCompanionRecoveryPhase.ReturningToExit)
            {
                if(!store.TryAdvanceCompanionRecovery(job.Id,job.Revision,now,NpcCompanionRecoveryPhase.WaitingOutside,value.ExteriorEntrance))return;
                job=store.ReadNpcAction(job.Id);
                if(job.CompanionRecovery.RecruitmentRoute!=null)
                    store.TryCaptureCompanionRecoveryRoute(job.Id,job.Revision,now,job.CompanionRecovery.RecruitmentRoute,
                        job.CompanionRecovery.RecruitmentOrigin,DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed);
                // The actor physically reached the actual native portal. Its
                // paired exterior is unloaded; rebuild only there when observed.
                actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);binding.Actor=null;
            }
            else if(store.TryAdvanceCompanionRecovery(job.Id,job.Revision,now,NpcCompanionRecoveryPhase.ResumedRoutine,
                DungeonPoint(map,root,actual,inside)))
            {
                job=store.ReadNpcAction(job.Id);
                if(!NpcActionExecutor.IsTerminal(job))EndDungeonFollow(store,job,now,NpcActionObservation.Interrupted("The companion returned to the actual recruitment origin."));
                if(streets && streets.TryReceiveDungeonFollower(actor,store,map))dungeonFollowers.Remove(job.ResidentId);
            }
        }
        private void SaveCompanionRecoveryPosition(WorldStateStore store,DungeonFollower binding,double now,Transform root,bool inside,bool force=false)
        {
            if(!force && Time.unscaledTime<binding.SaveAt)return;binding.SaveAt=Time.unscaledTime+0.5f;
            var job=store.ReadNpcAction(binding.ActionId);
            store.SaveNpcActionPosition(job.Id,job.Revision,DungeonPoint(binding.MapId,root,binding.Actor.transform.position-Vector3.up,inside),NpcActionExecutor.IsTerminal(job),now);
        }
        private static void DeliverCompanionRecoveryNotice(WorldStateStore store,NpcActionJob job,ResidentActor actor,double now)
        {
            var recovery=job.CompanionRecovery;
            string inside=Hours(recovery.InsideHours),outside=Hours(recovery.OutsideHours);
            string line=recovery.Phase==NpcCompanionRecoveryPhase.WaitingInside?
                "I cannot cross this obstacle. I will wait here for "+inside+", then walk to the dungeon entrance and wait outside for "+outside+". Then I will return to where you recruited me, if the route is passable.":
                recovery.Phase==NpcCompanionRecoveryPhase.WaitingOutside?
                "I reached the dungeon entrance. I will wait outside for "+outside+", then return to where you recruited me, if the route is passable.":
                recovery.Phase==NpcCompanionRecoveryPhase.ReturningToExit?
                "My "+inside+" wait is over. I am returning to the dungeon entrance, then waiting outside for "+outside+".":
                "My "+outside+" wait outside is over. I am returning to where you recruited me. I will stop if the return route is blocked.";
            var game=GameManager.Instance;
            bool nearby=Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=3f;
            if(nearby && DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow))return;
            // Claim before opening native UI: refusal, close callbacks and later
            // recovery stages cannot repeatedly reopen the conversation.
            var current=store.ReadNpcAction(job.Id);
            if(!store.TryMarkCompanionRecoveryNotice(current.Id,current.Revision,now))return;
            if(nearby)
            {
                game.TalkManager.TalkToMobileNPC(actor.Person);
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow))
                {DaggerfallUI.AddHUDText(actor.Resident.FullName+": "+line,8f);return;}
                var message=new DaggerfallMessageBox(DaggerfallUI.UIManager);message.SetText(new[]{actor.Resident.FullName,line});
                message.ClickAnywhereToClose=true;DaggerfallUI.UIManager.PushWindow(message);
            }
            else DaggerfallUI.AddHUDText(actor.Resident.FullName+": "+line,8f);
        }
        private static string Hours(int hours) {return hours>=24 && hours%24==0?(hours/24)+" game days":hours+" game hours";}
        private static bool VisibleOverlandReconstruction(NpcCompanionRecovery value,Vector3 ground)
        {
            if(value.OverlandStartedAt<=0 || !Camera.main)return false;
            var view=Camera.main.WorldToViewportPoint(ground+Vector3.up);
            return view.z>0 && view.x>=0 && view.x<=1 && view.y>=0 && view.y<=1 &&
                CivilianResponses.Sees(Camera.main.transform.position,ground+Vector3.up,165);
        }
    }
}
