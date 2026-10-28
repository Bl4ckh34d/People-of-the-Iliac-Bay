using System.Collections;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        // Uses only naturally present, currently hostile native enemies. No
        // enemy, NPC, consent, health or relationship state is staged here.
        private static IEnumerator VerifyDangerBoundary(Evidence evidence)
        {
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+20;
            float observed=0,toward=0;var previous=evidence.Actor.transform.position;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;evidence.Sample();
                var current=evidence.Actor.transform.position;
                var threat=RoadTravelActors.CurrentDungeonThreat(evidence.Actor,20);
                if(threat && threat.GetComponent<EnemySenses>() &&
                    threat.GetComponent<EnemySenses>().Target==game.PlayerEntityBehaviour &&
                    Vector3.Distance(game.PlayerMotor.transform.position,threat.transform.position)<6 &&
                    Vector3.Distance(previous,threat.transform.position)>=6)
                {
                    observed+=Time.unscaledDeltaTime;
                    // Compare both actor positions against the same current
                    // enemy position, so enemy pursuit is not charged to Follow.
                    float approach=Vector3.Distance(previous,threat.transform.position)-Vector3.Distance(current,threat.transform.position);
                    toward+=Mathf.Max(0,approach-0.002f);
                    Need(toward<=0.25f,"Dungeon follower resumed toward a still-visible native attacker after escaping its immediate danger radius; approach="+toward);
                }
                previous=current;
            }
            Need(observed>=3,"No sustained actual visible attacker near the observer was available for the danger-boundary proof.");
            evidence.CheckIdentity();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_DANGER_BOUNDARY_PASS: observedSeconds="+observed+
                "; towardThreatMetres="+toward+"; sameActionAndBody=true; enemySuppressed=false; originalConsent=true; roundTrip=false");
        }

        private static IEnumerator VerifyDangerExit(int map,Evidence evidence,CharacterController controller)
        {
            // The observer actually leaves through the existing native portal.
            // No enemy state is altered to make the companion catch up inside.
            evidence.Stage="danger-native-exit";yield return Exit(map,evidence);
            Need(!evidence.Roads.OwnsDungeonFollow(evidence.Id) &&
                evidence.Roads.FindPaidCompanion(evidence.Id)==evidence.Actor,
                "Danger exit did not return the same actual fast-travel body to its exterior owner.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FAST_TRAVEL_EXIT_HANDOFF_PASS: nativeExit=true; sameBody=true; exclusiveExteriorOwner=true; originalConsent=true");
            DaggerfallWorkshop.DaggerfallStaticDoors owner;DaggerfallWorkshop.StaticDoor door;
            Need(FindEntrance(map,out owner,out door),"Danger exit lost its actual native exterior entrance.");
            var start=PlayerGround();var normal=DaggerfallWorkshop.DaggerfallStaticDoors.GetDoorNormal(door);normal.y=0;
            Vector3 end=Vector3.zero;bool found=false;
            foreach(float angle in new[]{0f,45f,-45f,90f,-90f,135f,-135f,180f})
            {
                var candidate=start+Quaternion.Euler(0,angle,0)*normal.normalized*5;
                if(!StaticFloor(candidate,null,out end) || !ClearSupportedRoute(new[]{start,end},null,evidence.Actor.transform))continue;
                found=true;break;
            }
            Need(found,"Danger exit has no measured clear exterior follow route.");
            evidence.ResetMotion();evidence.Stage="danger-exterior-follow";
            yield return WalkObserver(new[]{start,end},controller,evidence,true);yield return WaitFollower(evidence,20);
            evidence.CheckIdentity();Need(evidence.Metres>3,"Danger exit follower did not physically resume outside.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_DANGER_EXIT_PASS: nativeExit=true; sameBodyAndItems=true; originalConsent=true; exteriorOwner=true; exteriorMetres="+
                evidence.Metres+"; maximumStep="+evidence.MaximumStep+"; enemySuppressed=false; interiorReturnNotMeasured=true");
        }

        private static void DescribeExteriorLanding(Evidence evidence)
        {
            var game=GameManager.Instance;var job=evidence.Store.ReadNpcAction(evidence.ActionId);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_EXIT_PENDING: now="+Now()+"; arrives="+(job.FollowTransition==null?0:job.FollowTransition.ArrivesAt)+
                "; transition="+(job.FollowTransition!=null)+"; bodyActive="+evidence.Actor.gameObject.activeInHierarchy+
                "; dungeonOwner="+evidence.Roads.OwnsDungeonFollow(evidence.Id)+"; roadOwner="+evidence.Roads.OwnsPaidCompanion(evidence.Id)+
                "; phase="+job.Phase+"; player="+PlayerGround()+"; actor="+evidence.Actor.transform.position);
            var location=game.StreamingWorld.CurrentPlayerLocationObject;if(!location)return;
            foreach(var door in DaggerfallWorkshop.DaggerfallStaticDoors.FindDoorsInCollections(
                location.StaticDoorCollections??new DaggerfallWorkshop.DaggerfallStaticDoors[0],DaggerfallWorkshop.DoorTypes.DungeonEntrance))
            {
                var expected=StreetPopulation.FollowExteriorDoorGround(door);RaycastHit hit;
                bool floor=Physics.Raycast(expected+Vector3.up*0.5f,Vector3.down,out hit,1.5f,~0,QueryTriggerInteraction.Ignore);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_EXIT_FLOOR: expected="+expected+"; hit="+floor+
                    "; floor="+(floor?hit.point:Vector3.zero)+"; collider="+(floor?hit.collider.name:"none")+
                    "; normal="+(floor?hit.normal:Vector3.zero)+"; dynamic="+(floor && NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider)));
                if(!floor)continue;
                foreach(var collider in Physics.OverlapCapsule(hit.point+Vector3.up*0.78f,hit.point+Vector3.up*1.6f,
                    CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Collide))
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_EXIT_BLOCKER: name="+collider.name+"; type="+collider.GetType().Name+
                        "; trigger="+collider.isTrigger+"; position="+collider.transform.position+"; bounds="+collider.bounds);
            }
        }
    }
}
