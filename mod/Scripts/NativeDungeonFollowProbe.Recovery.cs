using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private static IEnumerator VerifyRecovery(Evidence evidence,CharacterController controller)
        {
            var game=GameManager.Instance;var store=evidence.Store;var actor=evidence.Actor;
            var dungeon=game.PlayerEnterExit.Dungeon;var job=store.ReadNpcAction(evidence.ActionId);
            Need(dungeon && job.CompanionRecovery!=null && job.CompanionRecovery.InteriorEntrance!=null,
                "Recovery fixture requires the real crossed native portal and recruitment origin; recovery="+(job.CompanionRecovery!=null)+
                "; entrance="+(job.CompanionRecovery!=null && job.CompanionRecovery.InteriorEntrance!=null));
            var origin=NpcActionExecutor.CopyTarget(job.CompanionRecovery.RecruitmentOrigin);
            if(Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>2.8f)
            {
                // The recovery test needs a nearby observer, not a fleeing NPC
                // to abandon native danger and catch that observer first.
                float approachDeadline=Time.realtimeSinceStartup+20;
                while(Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>2.8f && Time.realtimeSinceStartup<approachDeadline)
                {
                    Need(actor.gameObject.activeInHierarchy,"Recovery observer lost the actual follower.");
                    MoveObserverToward(actor.transform.position-Vector3.up,controller);
                    evidence.Sample();yield return null;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_RECOVERY_OBSERVER_APPROACH: nativeController=true; npcRelocated=false; enemiesSuppressed=false");
            }
            Need(Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=3,
                "Recovery notice fixture requires the real nearby player.");
            job=store.ReadNpcAction(evidence.ActionId);
            var actual=dungeon.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            var saved=NpcActionExecutor.CopyTarget(job.LastPosition);saved.X=actual.x;saved.Y=actual.y;saved.Z=actual.z;
            Need(store.SaveNpcActionPosition(job.Id,job.Revision,saved,false,Now()),"Could not save actual recovery body point.");
            job=store.ReadNpcAction(job.Id);
            Need(store.TryBeginCompanionObstacleWait(job.Id,job.Revision,Now()),"Controlled production recovery start was rejected.");
            job=store.ReadNpcAction(job.Id);double started=job.CompanionRecovery.WaitStartedAt,insideUntil=job.CompanionRecovery.WaitUntil;
            Need(insideUntil-started==job.CompanionRecovery.InsideHours*3600d,"Inside wait did not use selected finite game-hour band.");
            var notice=typeof(RoadTravelActors).GetMethod("DeliverCompanionRecoveryNotice",BindingFlags.Static|BindingFlags.NonPublic);
            Need(notice!=null,"Production recovery notice adapter unavailable.");
            notice.Invoke(null,new object[]{store,job,actor,Now()});
            Need(store.ReadNpcAction(job.Id).CompanionRecovery.NoticeDelivered &&
                DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox && game.TalkManager.MobileNPC==actor.Person,
                "Production nearby recovery notice did not open the actual speaker's native Talk and message.");
            float pauseReadyUntil=Time.realtimeSinceStartup+2;
            while(!GameManager.IsGamePaused && Time.realtimeSinceStartup<pauseReadyUntil)yield return null;
            Vector3 held=actor.transform.position;double pausedAt=Now();float pauseUntil=Time.realtimeSinceStartup+1;
            while(Time.realtimeSinceStartup<pauseUntil)
            {
                Need(GameManager.IsGamePaused && Math.Abs(Now()-pausedAt)<0.01 && Vector3.Distance(held,actor.transform.position)<0.05f,
                    "Native Talk pause advanced the game clock or recovery body.");yield return null;
            }
            var restored=new WorldStateStore(1);
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot(),false)));
            var reloaded=restored.ReadNpcAction(job.Id);
            // The native legacy-runtime serializer emits 15 significant digits.
            // Permit only sub-millisecond/sub-micrometre numeric roundoff, never
            // a different source space, a reset notice or a renewed deadline.
            Need(Math.Abs(reloaded.CompanionRecovery.WaitUntil-insideUntil)<0.001 && reloaded.CompanionRecovery.NoticeDelivered &&
                NpcFollowTransition.SameSpace(reloaded.CompanionRecovery.RecruitmentOrigin,origin) &&
                NpcFollowTransition.Distance(reloaded.CompanionRecovery.RecruitmentOrigin,origin)<0.000001,
                "Native serialization reset the recovery deadline, notice or recruitment origin; expectedUntil="+insideUntil.ToString("R")+
                "; restoredUntil="+reloaded.CompanionRecovery.WaitUntil.ToString("R")+"; notice="+reloaded.CompanionRecovery.NoticeDelivered+
                "; sameOriginSpace="+NpcFollowTransition.SameSpace(reloaded.CompanionRecovery.RecruitmentOrigin,origin)+
                "; originDistance="+NpcFollowTransition.Distance(reloaded.CompanionRecovery.RecruitmentOrigin,origin).ToString("R"));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_RECOVERY_WAIT_PASS: person="+evidence.Id+
                "; controlledStart=true; automaticObstacle=false; actualNativeTalk=true; pausedClock=true; nativeSerializer=true; diskReload=false"+
                "; insideHours="+job.CompanionRecovery.InsideHours+"; outsideHours="+job.CompanionRecovery.OutsideHours+"; waitUntil="+insideUntil);

            // Expire the original paid service as well as the inside wait. The
            // observed return must then remain recovery, never renewed Follow.
            double releaseAt=Math.Max(insideUntil,Math.Max(job.FinishAt,job.DeadlineAt))+0.5;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(releaseAt-Now()));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);evidence.ResetMotion();
            float deadline=Time.realtimeSinceStartup+35;
            while(Time.realtimeSinceStartup<deadline)
            {
                job=store.ReadNpcAction(evidence.ActionId);
                if(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.WaitingOutside)break;
                Need(job.CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.Following,"Expired recovery resumed paid Follow.");
                if(actor && actor.gameObject.activeInHierarchy)evidence.Sample();yield return null;
            }
            job=store.ReadNpcAction(evidence.ActionId);
            var returnNavigation=actor?actor.GetComponent<NativeResidentNavigation>():null;
            Need(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.WaitingOutside && evidence.Metres>3 &&
                NpcActionExecutor.IsTerminal(job) && job.FinishAt==evidence.Finish && job.DeadlineAt==evidence.Deadline &&
                NpcFollowTransition.SameSpace(job.LastPosition,job.CompanionRecovery.ExteriorEntrance) &&
                NpcFollowTransition.Distance(job.LastPosition,job.CompanionRecovery.ExteriorEntrance)<=0.55,
                "Observed physical recovery did not reach and cross its actual paired portal after service expiry; phase="+job.CompanionRecovery.Phase+
                "; action="+job.Phase+"; metres="+evidence.Metres+"; actual="+(actor?actor.transform.position-Vector3.up:Vector3.zero)+
                "; portal="+dungeon.transform.TransformPoint(new Vector3((float)job.CompanionRecovery.InteriorEntrance.X,
                    (float)job.CompanionRecovery.InteriorEntrance.Y,(float)job.CompanionRecovery.InteriorEntrance.Z))+
                "; conversing="+(actor&&actor.IsConversing)+"; paused="+GameManager.IsGamePaused+
                "; nativeReason="+(returnNavigation?returnNavigation.FailureReason:"none")+
                "; nativePath="+(returnNavigation?returnNavigation.PathState:"none"));
            double outsideStarted=job.CompanionRecovery.WaitStartedAt,outsideUntil=job.CompanionRecovery.WaitUntil;
            Need(outsideUntil-outsideStarted==job.CompanionRecovery.OutsideHours*3600d && outsideStarted>=releaseAt,
                "Exterior wait did not begin at the actual observed portal arrival.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_RECOVERY_PORTAL_PASS: person="+evidence.Id+"; metres="+evidence.Metres+
                "; maximumStep="+evidence.MaximumStep+"; actualPortal=true; observedReturnWalk=true; insideTimeSkipped=true; expiredService=true; paidDeadlineUnchanged=true");

            game.PlayerEnterExit.TransitionDungeonExterior();
            yield return WaitForExteriorFloor(job.CompanionRecovery.ExteriorEntrance.SettlementId);
            DaggerfallStaticDoors exitOwner;StaticDoor exitDoor;
            Need(FindEntrance(job.CompanionRecovery.ExteriorEntrance.SettlementId,out exitOwner,out exitDoor),
                "Recovery observer is not beside the actual exterior dungeon door.");
            var start=PlayerGround();var forward=DaggerfallStaticDoors.GetDoorNormal(exitDoor);forward.y=0;Vector3 clear;
            Need(StaticFloor(start+forward.normalized*3,null,out clear) && ClearSupportedRoute(new[]{start,clear},null,null),
                "Actual exterior landing has no clear observer departure for recovery reconstruction.");
            var delta=clear-start;controller.Move(delta);Physics.SyncTransforms();
            yield return null;
            Need(Vector3.Distance(PlayerGround(),clear)<=0.55,"Native observer controller did not clear the actual landing.");
            deadline=Time.realtimeSinceStartup+30;ResidentActor outside=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                outside=evidence.Roads.FindDungeonCompanion(evidence.Id);
                if(outside && outside.gameObject.activeInHierarchy)break;yield return null;
            }
            Need(outside && outside.gameObject.activeInHierarchy,"Recovery companion did not reconstruct at the real exterior entrance.");
            held=outside.transform.position;deadline=Time.realtimeSinceStartup+1;
            while(Time.realtimeSinceStartup<deadline)
            {Need(Vector3.Distance(held,outside.transform.position)<0.05f,"Exterior waiting body moved before its deadline.");yield return null;}
            Need(InventorySignature(store.ReadNpcInventory(evidence.Id))==evidence.Inventory,"Portal recovery changed native item UIDs or wallet.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_RECOVERY_OUTSIDE_PASS: person="+evidence.Id+
                "; actualExteriorBody=true; held=true; actualArrival="+outsideStarted+"; waitUntil="+outsideUntil+"; sameInventory=true; noWaitReset=true");

            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(outsideUntil+0.5-Now()));
            // Observe the real beginning of departure, then leave through the
            // real portal. Only the now-unobserved leg may use native travel time.
            Vector3 departure=outside.transform.position,previous=departure;float walked=0;
            deadline=Time.realtimeSinceStartup+10;
            while(outside && outside.gameObject.activeInHierarchy && Time.realtimeSinceStartup<deadline && walked<0.5f)
            {
                float step=Vector3.Distance(previous,outside.transform.position);
                Need(step<=0.55f,"Visible outside recovery departure snapped.");walked+=step;previous=outside.transform.position;
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;
            }
            Need(walked>=0.2f,"Exterior recovery did not begin a real supported departure before observer teardown.");
            DaggerfallStaticDoors entranceOwner;StaticDoor entrance;
            Need(FindEntrance(job.CompanionRecovery.ExteriorEntrance.SettlementId,out entranceOwner,out entrance),
                "Recovery observer did not retain its actual native entrance.");
            game.PlayerEnterExit.TransitionDungeonInterior(entranceOwner.transform,entrance,game.PlayerGPS.CurrentLocation);
            deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline)
            {
                job=store.ReadNpcAction(evidence.ActionId);
                if(job.CompanionRecovery.RecruitmentArrival!=null && job.CompanionRecovery.OverlandStartedAt>0)break;
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;
            }
            job=store.ReadNpcAction(evidence.ActionId);var recovery=job.CompanionRecovery;
            Need(recovery.RecruitmentArrival!=null && recovery.OverlandStartedAt>=outsideUntil && recovery.OverlandSeconds>=60 &&
                recovery.RecruitmentApproach!=null && recovery.RecruitmentApproach.Count>=2 &&
                recovery.RecruitmentArrival.SettlementId==origin.SettlementId &&
                (origin.Kind!=NpcActionTargetKind.IndoorPoint || recovery.RecruitmentArrival.BuildingKey==origin.BuildingKey) &&
                NpcFollowTransition.Distance(recovery.RecruitmentOrigin,origin)==0,
                "Native return preparation did not preserve the actual recruitment location and measured final approach.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_RECOVERY_ORIGIN_PLAN_PASS: person="+evidence.Id+
                "; actualRecruitmentMap="+origin.SettlementId+"; actualRecruitmentBuilding="+origin.BuildingKey+
                "; nativeTravelSeconds="+recovery.OverlandSeconds+"; approachPoints="+recovery.RecruitmentApproach.Count+
                "; observedDepartureMetres="+walked+"; timedDeparture=true; nativeOriginSurvey=true; destinationArrivalObserved=false; automaticObstacle=false");
        }
    }
}
