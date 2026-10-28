using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private static IEnumerator VerifyContainerLoot(Evidence evidence)
        {
            var game=GameManager.Instance;var dungeon=game.PlayerEnterExit.Dungeon.transform;
            var store=evidence.Store;var actor=evidence.Actor;
            var controller=game.PlayerMotor.GetComponent<CharacterController>();
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var inventory=actor.GetComponent<NativeNpcInventoryOwner>().Binding.Items;
            DaggerfallLoot container=null;DaggerfallUnityItem selected=null;NavMeshPath approach=null;
            bool staged=Environment.GetEnvironmentVariable("POTIB_DUNGEON_CONTAINER_STAGE")=="1";
            bool stackProbe=Environment.GetEnvironmentVariable("POTIB_DUNGEON_CONTAINER_STACK")=="1";
            Need(!stackProbe || staged,"The focused native stack probe requires its explicit local staging flag.");
            Vector3 actorStart=Vector3.zero,observerStart=Vector3.zero;
            float shortest=180;int realContainers=0,eligibleContainers=0;
            foreach(var candidate in UnityEngine.Object.FindObjectsOfType<DaggerfallLoot>())
            {
                if(!candidate || !candidate.gameObject.activeInHierarchy || !candidate.transform.IsChildOf(dungeon) ||
                    candidate.ContainerType!=LootContainerTypes.RandomTreasure || candidate.LoadID==0 ||
                    candidate.corpseQuestUID!=0 || candidate.playerOwned || candidate.houseOwned || candidate.GetComponentInParent<QuestResourceBehaviour>())continue;
                realContainers++;DaggerfallUnityItem item=null;
                for(int index=0;index<candidate.Items.Count;index++)
                {
                    var actual=candidate.Items.GetItem(index);string reason;
                    if(actual!=null && (stackProbe?actual.IsAStack() && actual.stackCount>=80:actual.stackCount==1) && !actual.IsQuestItem && !actual.IsArtifact && !actual.RepairData.IsBeingRepaired() &&
                        body.Entity.Items.GetWeight()+actual.EffectiveUnitWeightInKg()*(stackProbe?80:1)<=body.Entity.MaxEncumbrance &&
                        NativeNpcInventoryBridge.CanAppendExactItem(inventory,actual,out reason)){item=actual;break;}
                }
                if(item==null)continue;eligibleContainers++;
                float direct=Vector3.Distance(PlayerGround(),candidate.transform.position);
                int floors=0,paths=0,clear=0;float nearestPath=float.MaxValue;
                if(direct>60){Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTAINER_ROUTE: id="+candidate.LoadID+"; at="+candidate.transform.position+"; direct="+direct+"; outsideBound=true");continue;}
                for(int direction=0;direction<8;direction++)
                {
                    NavMeshHit hit;Vector3 floor;
                    var near=candidate.transform.position+Quaternion.Euler(0,direction*45,0)*Vector3.forward*0.9f;
                    if(!NavMesh.SamplePosition(near,out hit,1,NavMesh.AllAreas) || !StaticFloor(hit.position,dungeon,out floor) ||
                        !BodyClear(floor,floor,controller.transform,true,controller.radius+0.02f) ||
                        !CivilianResponses.Sees(floor+Vector3.up,candidate.transform.position+Vector3.up*0.3f,6,candidate.transform))continue;
                    floors++;
                    var path=new NavMeshPath();
                    Vector3 stagedActor=Vector3.zero,stagedObserver=Vector3.zero;
                    if(staged)
                    {
                        if(!TryContainerStaging(candidate,floor,dungeon,actor,controller,out stagedActor,out stagedObserver,out path))continue;
                    }
                    else if(!NavMesh.CalculatePath(PlayerGround(),floor,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)continue;
                    paths++;
                    float length=0;for(int index=1;index<path.corners.Length;index++)length+=Vector3.Distance(path.corners[index-1],path.corners[index]);
                    nearestPath=Mathf.Min(nearestPath,length);
                    if(ClearSupportedRoute(path.corners,dungeon,actor.transform))clear++;
                    if(length>=shortest)continue;
                    shortest=length;container=candidate;selected=item;approach=path;
                    actorStart=stagedActor;observerStart=stagedObserver;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTAINER_ROUTE: id="+candidate.LoadID+"; at="+candidate.transform.position+
                    "; direct="+direct+"; floors="+floors+"; completePaths="+paths+"; nearestPath="+nearestPath+"; clearRoutes="+clear);
            }
            Need(container,"No actual unowned dungeon treasure has an eligible single item and complete native path within 180 metres; real="+realContainers+"; eligible="+eligibleContainers);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CONTAINER_SETUP: container="+container.LoadID+"; uid="+selected.UID+
                "; item="+selected.LongName+"; stackable="+selected.IsAStack()+"; stackCount="+selected.stackCount+
                "; plannedLocalPath="+shortest+"; actualNativeTreasure=true; controlledPermission=true; controlledBodyPlacement="+staged+"; enemyChanged=false");
            evidence.Stage="container-approach";evidence.ResetMotion();
            if(staged)
            {
                // Isolate permission and actual short loot approach from the
                // separately unverified long observer route. Never move treasure.
                var navigation=actor.GetComponent<NativeResidentNavigation>();if(navigation)navigation.ResetPlacement();
                actor.GetComponent<CrowdMotion>().RestorePlacement(actorStart);
                PlaceObstacleObserver(observerStart,controller);Physics.SyncTransforms();evidence.ResetMotion();
                Need(Vector3.Distance(actorStart,container.transform.position)>2,"Controlled container start is already within transfer reach.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTAINER_STAGED: actor="+actorStart+"; observer="+observerStart+
                    "; sameBody=true; sourceJourneyMeasured=false; treasureMoved=false; physicalLootApproachRequired=true");
            }
            else
            {
                yield return WalkObserver(approach.corners,controller,evidence,true,shortest/1.8f+30,true,false);
                if(Environment.GetEnvironmentVariable("POTIB_DUNGEON_MULTITHREAT")=="1")
                {Need(evidence.MultipleThreatsVerified,"No actual multi-threat encounter was verified along the approach.");yield break;}
                yield return WaitFollower(evidence,20);
            }
            evidence.CheckIdentity();
            Need(Vector3.Distance(actor.transform.position,container.transform.position)<=6 &&
                CivilianResponses.Sees(actor.transform.position,container.transform.position+Vector3.up*0.3f,6,container.transform),
                "Actual treasure is outside companion perception after physical approach; actor="+actor.transform.position+"; container="+container.transform.position);
            var job=store.ReadNpcAction(evidence.ActionId);NpcCompanionEnterprise enterprise;
            Need(store.TryBeginCompanionEnterprise(evidence.Id,job.Revision,"native-container-plan:"+Guid.NewGuid().ToString("N"),
                NpcCompanionEnterpriseKind.DungeonCrawl,Now(),out enterprise),"Controlled container enterprise planning failed.");
            Vector3 target;int before=container.Items.Count;ulong uid=selected.UID;
            Need(!NativeCompanionLootClaims.TryLootOrApproach(store,actor,out target) && container.Items.Count==before &&
                ReferenceEquals(container.Items.GetItem(uid),selected),"Container looted without any grant.");
            Need(store.TryGrantCompanionLoot(evidence.Id,enterprise.Id,enterprise.Revision,"native-container-own:"+Guid.NewGuid().ToString("N"),true,false,Now()),
                "Controlled own-kill-only grant failed.");
            float deadline=Time.realtimeSinceStartup+1.2f;
            while(Time.realtimeSinceStartup<deadline){evidence.Sample();yield return null;}
            Need(!NativeCompanionLootClaims.TryLootOrApproach(store,actor,out target) && container.Items.Count==before &&
                ReferenceEquals(container.Items.GetItem(uid),selected),"Own-kill-only grant authorized native dungeon treasure.");
            evidence.CheckIdentity();enterprise=store.ReadCompanionEnterprise(evidence.Id);
            Need(store.TryGrantCompanionLoot(evidence.Id,enterprise.Id,enterprise.Revision,"native-container-express:"+Guid.NewGuid().ToString("N"),false,true,Now()),
                "Separate explicit container grant failed.");
            if(stackProbe){yield return VerifyContainerStack(evidence,container,selected);yield break;}
            deadline=Time.realtimeSinceStartup+30;var previous=actor.transform.position;
            evidence.ResetMotion();
            while(Time.realtimeSinceStartup<deadline && inventory.GetItem(uid)==null)
            {
                Need(Vector3.Distance(previous,actor.transform.position)<=0.55f,"Native container approach snapped.");
                previous=actor.transform.position;evidence.Sample();yield return null;
            }
            var native=actor.GetComponent<NativeResidentNavigation>();job=store.ReadNpcAction(evidence.ActionId);
            var seeker=actor.GetComponent<NativeCompanionLootSeeker>();
            Need(ReferenceEquals(inventory.GetItem(uid),selected) && container.Items.GetItem(uid)==null &&
                Vector3.Distance(actor.transform.position-Vector3.up,container.transform.position)<=1.1f,
                "Express container grant did not transfer the exact nearby native item; actor="+(actor.transform.position-Vector3.up)+
                "; container="+container.transform.position+"; recovery="+job.CompanionRecovery.Phase+
                "; sourceRetained="+ReferenceEquals(container.Items.GetItem(uid),selected)+"; inventoryItems="+inventory.Count+
                "; sourceItems="+container.Items.Count+"; selectedStack="+selected.stackCount+
                "; lastSource="+(seeker?seeker.LastSource:0)+"; lastItem="+(seeker?seeker.LastItem:0)+"; transferFailure="+(seeker?seeker.LastFailure:"none")+
                "; nativeReason="+(native?native.FailureReason:"none")+"; path="+(native?native.PathState:"none"));
            actor.CaptureInventory();enterprise=store.ReadCompanionEnterprise(evidence.Id);
            Need(enterprise.Containers && !enterprise.OwnDefeatedEnemies,"Container-only grant acquired unrelated own-kill rights.");
            Need(actor.GetInstanceID()==evidence.Body && actor.Person.GetInstanceID()==evidence.Person &&
                job.AcceptedAt==evidence.Accepted && job.FinishAt==evidence.Finish && job.DeadlineAt==evidence.Deadline &&
                ContractSignature(store,job)==evidence.Contract,"Container loot changed body, person or original companion consent.");
            foreach(var original in evidence.Items)
                Need(ReferenceEquals(inventory.GetItem(original.Key),original.Value),"Container loot replaced an original native item.");
            NpcCompanionEnterprise next;job=store.ReadNpcAction(evidence.ActionId);
            Need(store.TryBeginCompanionEnterprise(evidence.Id,job.Revision,"native-container-next:"+Guid.NewGuid().ToString("N"),
                NpcCompanionEnterpriseKind.DungeonCrawl,Now(),out next) && !next.Containers && !next.OwnDefeatedEnemies,
                "A new enterprise inherited the previous loot permission.");
            Need(!store.TryGrantCompanionLoot(evidence.Id,enterprise.Id,enterprise.Revision,"native-container-stale",false,true,Now()) &&
                !NativeCompanionLootClaims.TryLootOrApproach(store,actor,out target),"Old enterprise permission authorized new-enterprise loot.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CONTAINER_PASS: container="+container.LoadID+"; exactUid="+uid+
                "; noGrantBlocked=true; ownKillOnlyBlocked=true; separateContainerGrant=true; sameNativeItem=true; originalBodyAndConsent=true; newEnterpriseResets=true; staleGrantRejected=true; controlledBodyPlacement="+staged+"; semanticConsent=false");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: containerLoot=true; roundTrip=false; stop=false; semanticConsent=false");
        }

        private static bool TryContainerStaging(DaggerfallLoot loot,Vector3 endpoint,Transform dungeon,ResidentActor actor,
            CharacterController controller,out Vector3 actorFloor,out Vector3 observerFloor,out NavMeshPath path)
        {
            actorFloor=observerFloor=Vector3.zero;path=null;
            for(int direction=0;direction<8;direction++)
            {
                var radial=Quaternion.Euler(0,direction*45,0)*Vector3.forward;NavMeshHit hit;Vector3 start,observer;
                var seed=loot.transform.position+radial*2.8f;seed.y=endpoint.y;
                if(!NavMesh.SamplePosition(seed,out hit,0.6f,NavMesh.AllAreas) ||
                    !StaticFloor(hit.position,dungeon,out start) || Vector3.Distance(start,loot.transform.position)<=2 ||
                    !BodyClear(start,start,actor.transform,true) ||
                    !CivilianResponses.Sees(start+Vector3.up,loot.transform.position+Vector3.up*0.3f,6,loot.transform))continue;
                var route=new NavMeshPath();
                if(!NavMesh.CalculatePath(start,endpoint,NavMesh.AllAreas,route) || route.status!=NavMeshPathStatus.PathComplete ||
                    !ClearSupportedRoute(route.corners,dungeon,actor.transform))continue;
                float length=0;for(int index=1;index<route.corners.Length;index++)length+=Vector3.Distance(route.corners[index-1],route.corners[index]);
                if(length>5)continue;
                foreach(int side in new[]{1,-1})
                {
                    if(!StaticFloor(start+Vector3.Cross(Vector3.up,radial)*1.5f*side,dungeon,out observer) ||
                        Mathf.Abs(observer.y-start.y)>0.4f || !BodyClear(observer,observer,controller.transform,true,controller.radius+0.02f))continue;
                    bool enemyNearby=false;
                    foreach(var enemy in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                        if(enemy && enemy.gameObject.activeInHierarchy && enemy.Entity!=null && enemy.Entity.CurrentHealth>0 &&
                            NativeNpcCombatAssist.Identity(enemy)!=null &&
                            (Vector3.Distance(enemy.transform.position,start)<14 || Vector3.Distance(enemy.transform.position,observer)<14))
                        {enemyNearby=true;break;}
                    if(enemyNearby)continue;
                    actorFloor=start;observerFloor=observer;path=route;return true;
                }
            }
            return false;
        }
    }
}
