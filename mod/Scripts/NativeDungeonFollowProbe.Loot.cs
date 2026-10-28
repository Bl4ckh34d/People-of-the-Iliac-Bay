using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private static IEnumerator VerifyLoot(Evidence evidence)
        {
            var store=evidence.Store;var actor=evidence.Actor;var dungeon=GameManager.Instance.PlayerEnterExit.Dungeon;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();DaggerfallEntityBehaviour enemy=null;
            var controller=GameManager.Instance.PlayerMotor.GetComponent<CharacterController>();
            NavMeshPath approach=null;float shortest=40;
            foreach(var candidate in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if(candidate && candidate.Entity!=null && candidate.Entity.CurrentHealth>0 && candidate.transform.IsChildOf(dungeon.transform) &&
                    NativeNpcCombatAssist.OwnershipClear(candidate) && NativeNpcCombatAssist.Identity(candidate)!=null &&
                    Vector3.Distance(actor.transform.position,candidate.transform.position)<=30)
                {
                    for(int direction=0;direction<8;direction++)
                    {
                        NavMeshHit hit;Vector3 floor;
                        var near=candidate.transform.position+Quaternion.Euler(0,direction*45,0)*Vector3.forward*2.5f;
                        if(!NavMesh.SamplePosition(near,out hit,2,NavMesh.AllAreas) ||
                            !StaticFloor(hit.position,dungeon.transform,out floor) ||
                            !BodyClear(floor,floor,controller.transform,true,controller.radius+0.02f) ||
                            !CivilianResponses.Sees(floor+Vector3.up,candidate.transform.position,6,candidate.transform))continue;
                        var path=new NavMeshPath();
                        if(!NavMesh.CalculatePath(PlayerGround(),floor,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete ||
                            !ClearSupportedRoute(path.corners,dungeon.transform,actor.transform))continue;
                        float length=0;for(int index=1;index<path.corners.Length;index++)length+=Vector3.Distance(path.corners[index-1],path.corners[index]);
                        if(length>=shortest)continue;shortest=length;approach=path;enemy=candidate;
                    }
                }
            Need(enemy,"No actual non-quest dungeon enemy has a supported clear observer approach within 40 metres. No enemy or corpse is fabricated.");
            var job=store.ReadNpcAction(evidence.ActionId);NpcCompanionEnterprise enterprise;
            Need(store.TryBeginCompanionEnterprise(evidence.Id,job.Revision,"native-loot-plan:"+Guid.NewGuid().ToString("N"),
                NpcCompanionEnterpriseKind.DungeonCrawl,Now(),out enterprise),"Controlled enterprise planning failed.");
            string enemyId=NativeNpcCombatAssist.Identity(enemy);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_LOOT_SETUP: person="+evidence.Id+"; enemy="+enemyId+
                "; existingNativeEnemy=true; controlledLethalDamage=true; naturalCombat=false; fabricatedReceipt=false; observerApproach="+shortest);
            // Actual native damage carries the actual source body through the
            // installed defense hook. No store defeat API is called by this probe.
            enemy.DamageHealthFromSource(body,enemy.Entity.CurrentHealth,true,enemy.transform.position);
            float deadline=Time.realtimeSinceStartup+8;DaggerfallLoot corpse=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                enterprise=store.ReadCompanionEnterprise(evidence.Id);
                Need(enterprise!=null,"Actual companion enterprise ended before its native corpse receipt could bind.");
                var defeat=enterprise.Defeats.Find(value=>value.EnemyId==enemyId);
                if(defeat!=null && defeat.CorpseLoadId!=0)
                    foreach(var loaded in UnityEngine.Object.FindObjectsOfType<DaggerfallLoot>())
                        if(loaded.LoadID==defeat.CorpseLoadId && loaded.ContainerType==LootContainerTypes.CorpseMarker){corpse=loaded;break;}
                if(corpse)break;yield return null;
            }
            Need(corpse,"Actual native lethal damage did not bind its exact companion defeat receipt to the real corpse LoadID.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_LOOT_RECEIPT_PASS: enemy="+enemyId+"; corpse="+corpse.LoadID+
                "; actualCorpse="+corpse.transform.position+"; actualCompanion="+actor.transform.position+"; municipalOwnerRequired=false");
            Vector3 target;int before=corpse.Items.Count;
            Need(!NativeCompanionLootClaims.TryLootOrApproach(store,actor,out target) && corpse.Items.Count==before,
                "No-consent companion looted its real corpse.");
            // Native death projects the corpse onto its real floor. Approach
            // that actual marker, not the former enemy's eye-level position.
            approach=null;shortest=40;
            for(int direction=0;direction<8;direction++)
            {
                NavMeshHit hit;Vector3 floor;
                var near=corpse.transform.position+Quaternion.Euler(0,direction*45,0)*Vector3.forward*0.9f;
                if(!NavMesh.SamplePosition(near,out hit,1,NavMesh.AllAreas) || !StaticFloor(hit.position,dungeon.transform,out floor) ||
                    !BodyClear(floor,floor,controller.transform,true,controller.radius+0.02f) ||
                    !CivilianResponses.Sees(floor+Vector3.up,corpse.transform.position+Vector3.up*0.3f,6,corpse.transform))continue;
                var path=new NavMeshPath();
                if(!NavMesh.CalculatePath(PlayerGround(),floor,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete ||
                    !ClearSupportedRoute(path.corners,dungeon.transform,actor.transform))continue;
                float length=0;for(int index=1;index<path.corners.Length;index++)length+=Vector3.Distance(path.corners[index-1],path.corners[index]);
                if(length>=shortest)continue;shortest=length;approach=path;
            }
            Need(approach!=null,"Actual native corpse has no supported clear observer approach; corpse="+corpse.transform.position);
            evidence.Stage="loot-approach";evidence.ResetMotion();
            yield return WalkObserver(approach.corners,controller,evidence,true);
            yield return WaitFollower(evidence,20);evidence.CheckIdentity();
            Need(Vector3.Distance(actor.transform.position,corpse.transform.position)<=6 &&
                CivilianResponses.Sees(actor.transform.position,corpse.transform.position+Vector3.up*0.3f,6,corpse.transform),
                "Actual corpse is not within the companion's native loot perception after the physical approach; actor="+actor.transform.position+
                "; corpse="+corpse.transform.position+"; observer="+PlayerGround()+"; distance="+Vector3.Distance(actor.transform.position,corpse.transform.position));
            Need(!NativeCompanionLootClaims.TryLootOrApproach(store,actor,out target) && corpse.Items.Count==before,
                "No-consent companion looted its real corpse after the physical approach.");
            var inventory=actor.GetComponent<NativeNpcInventoryOwner>().Binding.Items;DaggerfallUnityItem item=null;
            for(int index=0;index<corpse.Items.Count;index++)
            {
                var candidate=corpse.Items.GetItem(index);
                if(candidate!=null && candidate.stackCount==1 && !candidate.IsQuestItem && !candidate.IsArtifact &&
                    !candidate.RepairData.IsBeingRepaired() && body.Entity.Items.GetWeight()+candidate.EffectiveUnitWeightInKg()<=body.Entity.MaxEncumbrance)
                {item=candidate;break;}
            }
            Need(item!=null,"Actual corpse has no single native item within the companion's actual weight limit.");
            ulong uid=item.UID;
            Need(store.TryGrantCompanionLoot(evidence.Id,enterprise.Id,enterprise.Revision,"native-loot-grant:"+Guid.NewGuid().ToString("N"),true,false,Now()),
                "Explicit controlled own-kill consent failed.");
            deadline=Time.realtimeSinceStartup+30;var previous=actor.transform.position;
            while(Time.realtimeSinceStartup<deadline && inventory.GetItem(uid)==null)
            {
                Need(Vector3.Distance(previous,actor.transform.position)<=0.55f,"Native corpse approach snapped.");previous=actor.transform.position;
                yield return null;
            }
            job=store.ReadNpcAction(evidence.ActionId);var native=actor.GetComponent<NativeResidentNavigation>();
            var blocker=BodyBlocker(actor.transform.position-Vector3.up,corpse.transform.position,actor.transform,false,CrowdMotion.BodyRadius);
            Need(inventory.GetItem(uid)!=null && corpse.Items.GetItem(uid)==null &&
                Vector3.Distance(actor.transform.position-Vector3.up,corpse.transform.position)<=1.1f,
                "Own-kill consent did not cause actual close-range exact UID transfer from the native corpse; actor="+(actor.transform.position-Vector3.up)+
                "; corpse="+corpse.transform.position+"; observer="+PlayerGround()+"; received="+(inventory.GetItem(uid)!=null)+
                "; corpseItems="+corpse.Items.Count+"; recovery="+job.CompanionRecovery.Phase+"; paused="+GameManager.IsGamePaused+
                "; nativeReason="+(native?native.FailureReason:"none")+"; path="+(native?native.PathState:"none")+
                "; blocker="+(blocker?blocker.name:"none")+"; ownGrant="+store.ReadCompanionEnterprise(evidence.Id).OwnDefeatedEnemies);
            actor.CaptureInventory();enterprise=store.ReadCompanionEnterprise(evidence.Id);
            Need(enterprise.OwnDefeatedEnemies && !enterprise.Containers && !enterprise.AllowsLoadedLoot(enterprise.Id,false,true),
                "Native own-kill grant incorrectly authorizes unrelated dungeon containers.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_LOOT_PASS: person="+evidence.Id+"; enemy="+enemyId+"; corpse="+corpse.LoadID+
                "; exactUid="+uid+"; noConsentBlocked=true; actualKillerReceipt=true; actualNativeTransfer=true; containerGrant=false; naturalCombat=false");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: loot=true; roundTrip=false; stop=false; semanticConsent=false");
        }
    }
}
