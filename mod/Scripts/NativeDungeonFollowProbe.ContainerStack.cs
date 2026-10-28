using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private static IEnumerator VerifyContainerStack(Evidence evidence,DaggerfallLoot container,DaggerfallUnityItem selected)
        {
            var actor=evidence.Actor;var store=evidence.Store;
            var inventory=actor.GetComponent<NativeNpcInventoryOwner>().Binding.Items;
            long beforeSource=NativeItemUnits(container.Items),beforeInventory=NativeItemUnits(inventory);
            int originalCount=selected.stackCount,oldRate=Application.targetFrameRate,oldSync=QualitySettings.vSyncCount;
            float firstTransfer=-1,deadline=Time.realtimeSinceStartup+35,logAt=0;long prior=beforeSource;
            evidence.ResetMotion();
            try
            {
                // A controlled low frame rate exposes time-based stall mistakes
                // during real one-unit-per-frame native stack transfers.
                QualitySettings.vSyncCount=0;Application.targetFrameRate=10;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTAINER_STACK_SETUP: container="+container.LoadID+"; uid="+selected.UID+
                    "; nativeStackCount="+originalCount+"; controlledFrameRate=10; itemsInjected=false");
                while(Time.realtimeSinceStartup<deadline)
                {
                    evidence.Sample();long current=NativeItemUnits(container.Items);
                    if(current<prior)
                    {
                        if(firstTransfer<0)firstTransfer=Time.realtimeSinceStartup;
                        Need(Vector3.Distance(actor.transform.position-Vector3.up,container.transform.position)<=1.1f,
                            "Native stack transfer occurred outside actual loot reach.");
                    }
                    prior=current;
                    var job=store.ReadNpcAction(evidence.ActionId);var seeker=actor.GetComponent<NativeCompanionLootSeeker>();
                    if(Time.realtimeSinceStartup>=logAt)
                    {
                        logAt=Time.realtimeSinceStartup+1;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTAINER_STACK_PROGRESS: removed="+(beforeSource-current)+
                            "; selectedRemaining="+selected.stackCount+"; phase="+job.CompanionRecovery.Phase+
                            "; transferFailure="+(seeker?seeker.LastFailure:"none"));
                    }
                    Need(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.Following,
                        "Native ongoing loot entered obstacle waiting; removed="+(beforeSource-current)+
                        "; selectedRemaining="+selected.stackCount+"; phase="+job.CompanionRecovery.Phase+
                        "; transferFailure="+(seeker?seeker.LastFailure:"none"));
                    if(firstTransfer>=0 && Time.realtimeSinceStartup-firstTransfer>=8.5f)break;
                    yield return null;
                }
                long removed=beforeSource-NativeItemUnits(container.Items),received=NativeItemUnits(inventory)-beforeInventory;
                Need(firstTransfer>=0 && Time.realtimeSinceStartup-firstTransfer>=8.5f && removed>=70 && received==removed,
                    "Native prolonged stack transfer not proven; removed="+removed+"; received="+received);
                var final=store.ReadNpcAction(evidence.ActionId);
                Need(actor.GetInstanceID()==evidence.Body && actor.Person.GetInstanceID()==evidence.Person &&
                    final.AcceptedAt==evidence.Accepted && final.FinishAt==evidence.Finish && final.DeadlineAt==evidence.Deadline &&
                    ContractSignature(store,final)==evidence.Contract,"Native stack loot changed the original companion contract or body.");
                foreach(var original in evidence.Items)
                    Need(ReferenceEquals(inventory.GetItem(original.Key),original.Value),"Native stack loot replaced an existing item object.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CONTAINER_STACK_PASS: removed="+removed+"; received="+received+
                    "; seconds="+(Time.realtimeSinceStartup-firstTransfer)+"; sameBodyAndConsent=true; controlledFrameRate=10; itemsInjected=false; obstacleWait=false");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: containerStack=true; controlledBodyPlacement=true; semanticConsent=false");
            }
            finally {Application.targetFrameRate=oldRate;QualitySettings.vSyncCount=oldSync;}
        }
        private static long NativeItemUnits(ItemCollection items)
        {long result=0;for(int index=0;index<items.Count;index++)result+=items.GetItem(index).stackCount;return result;}
    }
}
