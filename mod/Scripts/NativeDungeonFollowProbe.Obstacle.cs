using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private static IEnumerator VerifyAutomaticObstacle(Evidence evidence,CharacterController controller,NavMeshPath entryPath)
        {
            var game=GameManager.Instance;var root=game.PlayerEnterExit.Dungeon.transform;
            var approach=new List<Vector3>{entryPath.corners[0]};float remaining=3;
            for(int index=1;index<entryPath.corners.Length && remaining>0;index++)
            {
                var from=approach[approach.Count-1];float length=Vector3.Distance(from,entryPath.corners[index]);
                approach.Add(Vector3.MoveTowards(from,entryPath.corners[index],remaining));remaining-=length;
            }
            evidence.Stage="obstacle-entry";yield return WalkObserver(approach.ToArray(),controller,evidence,false);
            yield return WaitFollower(evidence,20);evidence.CheckIdentity();
            var actor=evidence.Actor;var job=evidence.Store.ReadNpcAction(evidence.ActionId);
            Need(job.CompanionRecovery!=null && job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.Following &&
                job.CompanionRecovery.InteriorEntrance!=null,"Automatic obstacle needs the real entered Follow and its captured portal.");
            var actual=actor.transform.position-Vector3.up;var mesh=NavMesh.CalculateTriangulation();
            Vector3 target=Vector3.zero;bool found=false;NavMeshPathStatus status=NavMeshPathStatus.PathInvalid;string obstruction=null;
            for(int index=0;index+2<mesh.indices.Length;index+=3)
            {
                if(index%192==0)yield return null;
                var candidate=(mesh.vertices[mesh.indices[index]]+mesh.vertices[mesh.indices[index+1]]+mesh.vertices[mesh.indices[index+2]])/3;
                float distance=Vector3.Distance(actual,candidate);Vector3 floor;
                if(distance<8 || distance>40 || Mathf.Abs(actual.y-candidate.y)>8 || !StaticFloor(candidate,root,out floor) ||
                    !BodyClear(floor,floor,controller.transform,false,controller.radius+0.02f))continue;
                var path=new NavMeshPath();bool calculated=NavMesh.CalculatePath(actual,floor,NavMesh.AllAreas,path);
                if(calculated && path.status==NavMeshPathStatus.PathComplete)
                {
                    DaggerfallActionDoor blockedDoor=null;
                    for(int corner=1;corner<path.corners.Length && !blockedDoor;corner++)
                        foreach(var collider in CollisionCandidates(path.corners[corner-1],path.corners[corner],CrowdMotion.BodyRadius))
                        {
                            var door=collider.GetComponentInParent<DaggerfallActionDoor>();
                            if(door && door.IsClosed && (door.IsLocked || door.GetComponent<DaggerfallAction>())){blockedDoor=door;break;}
                        }
                    if(!blockedDoor)continue;
                    obstruction="native-door:"+blockedDoor.LoadID+"; locked="+blockedDoor.IsLocked+"; actionOwned="+(bool)blockedDoor.GetComponent<DaggerfallAction>();
                }
                else
                {
                    var blocker=BodyBlocker(actual,floor,actor.transform,true,CrowdMotion.BodyRadius);
                    if(!blocker || !blocker.transform.IsChildOf(root) || NativeNavigationGeometry.IsDynamicNavigationBody(blocker))continue;
                    obstruction="native-static:"+blocker.name;
                }
                target=floor;status=path.status;found=true;break;
            }
            Need(found,"No actual supported dungeon floor behind an incomplete route or a locked/action-owned native door was available.");
            PlaceObstacleObserver(target,controller);evidence.ResetMotion();evidence.Stage="automatic-obstacle";
            float began=Time.realtimeSinceStartup,deadline=began+60;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_OBSTACLE_SETUP: actor="+actual+"; observer="+target+
                "; nativePath="+status+"; obstruction="+obstruction+"; controlledObserverPlacement=true; npcPlaced=false; geometryChanged=false; recoveryStateInjected=false");
            while(Time.realtimeSinceStartup<deadline)
            {
                job=evidence.Store.ReadNpcAction(evidence.ActionId);
                if(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.WaitingInside && job.CompanionRecovery.NoticeDelivered)break;
                evidence.Sample();yield return null;
            }
            job=evidence.Store.ReadNpcAction(evidence.ActionId);var native=actor.GetComponent<NativeResidentNavigation>();
            Need(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.WaitingInside && job.CompanionRecovery.NoticeDelivered,
                "Native obstacle did not automatically start waiting and deliver its notice; phase="+job.CompanionRecovery.Phase+
                "; actual="+(actor.transform.position-Vector3.up)+"; nativeReason="+(native?native.FailureReason:"none")+
                "; actualThreat="+(RoadTravelActors.CurrentDungeonThreat(actor)?RoadTravelActors.CurrentDungeonThreat(actor).name:"none"));
            Need(Time.realtimeSinceStartup-began>=5.5f && job.CompanionRecovery.WaitUntil-job.CompanionRecovery.WaitStartedAt==job.CompanionRecovery.InsideHours*3600d,
                "Automatic obstacle skipped the physical stall or changed its finite wait band.");
            var popup=DaggerfallUI.Instance.DaggerfallHUD.PopupText;
            var rows=(LinkedList<TextLabel>)typeof(PopupText).GetField("textRows",Private).GetValue(popup);
            string text="";foreach(var row in rows)text+=row.Text+" ";
            Need(popup.Enabled && text.Contains("I cannot cross this obstacle."),"Automatic obstacle did not reach the actual native HUD text.");
            evidence.CheckIdentity();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_AUTOMATIC_OBSTACLE_WAIT_PASS: elapsedSeconds="+(Time.realtimeSinceStartup-began)+
                "; insideHours="+job.CompanionRecovery.InsideHours+"; outsideHours="+job.CompanionRecovery.OutsideHours+
                "; nativeHud=true; noStateInjection=true; noTimeSkip=true; sameBodyAndConsent=true");

            actual=actor.transform.position-Vector3.up;found=false;
            for(int index=0;index<8;index++)
            {
                var candidate=actual+Quaternion.Euler(0,index*45,0)*Vector3.forward*1.4f;Vector3 floor;
                if(!StaticFloor(candidate,root,out floor) || !BodyClear(floor,floor,controller.transform,false,controller.radius+0.02f))continue;
                var path=new NavMeshPath();if(!NavMesh.CalculatePath(actual,floor,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)continue;
                float length=0;for(int corner=1;corner<path.corners.Length;corner++)length+=Vector3.Distance(path.corners[corner-1],path.corners[corner]);
                if(length>1.8f)continue;target=floor;found=true;break;
            }
            Need(found,"No actual reachable nearby observer floor was available for resuming the waiting companion.");
            PlaceObstacleObserver(target,controller);deadline=Time.realtimeSinceStartup+5;
            while(Time.realtimeSinceStartup<deadline && evidence.Store.ReadNpcAction(evidence.ActionId).CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.Following)yield return null;
            job=evidence.Store.ReadNpcAction(evidence.ActionId);
            Need(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.Following && job.CompanionRecovery.WaitUntil==0 && job.CompanionRecovery.MeasuredRoute==null,
                "Reachable returning observer did not resume the same original Follow.");
            evidence.CheckIdentity();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_AUTOMATIC_OBSTACLE_RESUME_PASS: sameActionAndBody=true; originalConsent=true; actualNearbyReachablePlayer=true; controlledObserverReturn=true; npcPlaced=false");
        }

        private static void PlaceObstacleObserver(Vector3 floor,CharacterController controller)
        {
            controller.enabled=false;controller.transform.position=floor+Vector3.up;controller.enabled=true;Physics.SyncTransforms();
        }
    }
}
