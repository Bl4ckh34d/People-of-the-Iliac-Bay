using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class HouseholdInnPhysicalProbe
    {
        private static WorldStateStore NativeReload(WorldStateStore source)
        {
            var result=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            result.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),source.Snapshot())));
            return result;
        }
        private static IEnumerator VerifyOffscreenLifecycle(WorldStateStore original,HouseholdDayPlan source,StreetPopulation streets,DaggerfallInterior room)
        {
            var store=NativeReload(original);var plan=store.ReadHouseholdDay(source.SettlementId,source.HomeKey,source.AbsoluteDay);
            Fund(store,plan.First.ResidentId);Fund(store,plan.Second.ResidentId);
            string reason;if(!store.TryPublishJointHouseholdDay(plan,plan.PlannedAt,out reason))throw new InvalidOperationException(reason);
            double start=plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            double walk=Math.Max(plan.JointInn.First.Itinerary.Stops[0].WalkSeconds,plan.JointInn.Second.Itinerary.Stops[0].WalkSeconds);
            store.AdvanceHouseholdJointLifecycle(plan.Id,start+walk+1,NativeInnCalendar.ForServiceAt,canReservePair:streets.CanReserveHouseholdSeats);
            var pending=store.ReadHouseholdInnServiceAdmission(plan.Id);
            if(pending==null)throw new InvalidOperationException("Actual Inn has no available native table for unloaded family service.");
            double at=pending.SeatedAt+1;
            store.AdvanceHouseholdJointLifecycle(plan.Id,at,NativeInnCalendar.ForServiceAt,canReservePair:streets.CanReserveHouseholdSeats);
            var paid=store.ReadHouseholdInnServiceAdmission(plan.Id);
            if(paid?.Outcome!="purchased" || store.Snapshot().ObservedVisits.Exists(v=>v.HouseholdDayId==plan.Id))
                throw new InvalidOperationException("Unloaded native service did not pay both real wallets without physical visit fabrication.");
            int firstGold=store.ReadNpcInventory(plan.First.ResidentId).WalletGold,secondGold=store.ReadNpcInventory(plan.Second.ResidentId).WalletGold;
            store=NativeReload(store);
            var field=typeof(StreetPopulation).GetField("worldStore",Private);var prior=field.GetValue(streets);
            var bodies=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            var old=new Dictionary<string,ResidentActor>();var active=new Dictionary<string,bool>();
            foreach(string id in new[]{plan.First.ResidentId,plan.Second.ResidentId})
            {ResidentActor actor;if(bodies.TryGetValue(id,out actor) && actor){old[id]=actor;active[id]=actor.gameObject.activeSelf;actor.gameObject.SetActive(false);bodies.Remove(id);}}
            try
            {
                field.SetValue(streets,store);
                for(int pass=0;pass<2;pass++)
                {
                    var simulation=store.ReadHouseholdJointSimulation(plan.SettlementId,plan.HomeKey);
                    var actual=store.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey);
                    var restore=typeof(StreetPopulation).GetMethod("RestoreHouseholdInnBodies",Private);
                    if(!(bool)restore.Invoke(streets,new object[]{actual,simulation,at}))
                        throw new InvalidOperationException("Production native Inn pair reconstruction failed on handoff "+pass);
                    ResidentActor a,b;
                    if(!bodies.TryGetValue(plan.First.ResidentId,out a) || !bodies.TryGetValue(plan.Second.ResidentId,out b) ||
                        !a || !b || a.transform.parent!=room.transform || b.transform.parent!=room.transform ||
                        store.ReadHouseholdJointSimulation(plan.SettlementId,plan.HomeKey).Active)
                        throw new InvalidOperationException("Native pair reconstruction published only one room body or left both owners active.");
                    store=NativeReload(store);field.SetValue(streets,store);
                    if(store.ReadNpcInventory(plan.First.ResidentId).WalletGold!=firstGold ||
                        store.ReadNpcInventory(plan.Second.ResidentId).WalletGold!=secondGold)
                        throw new InvalidOperationException("Native scene handback duplicated payment.");
                    var current=store.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey);
                    store.BeginHouseholdJointSimulation(plan.Id,current.Sequence,at);
                    foreach(string id in new[]{plan.First.ResidentId,plan.Second.ResidentId})
                    {var actor=bodies[id];actor.gameObject.SetActive(false);UnityEngine.Object.Destroy(actor.gameObject);bodies.Remove(id);}
                    at+=5;store.AdvanceHouseholdJointLifecycle(plan.Id,at,NativeInnCalendar.ForServiceAt);
                    yield return null;
                }
                var end=store.ReadHouseholdInnServiceAdmission(plan.Id).ReturnAt+
                    Math.Max(plan.JointInn.First.Itinerary.ExitWalkSeconds,plan.JointInn.Second.Itinerary.ExitWalkSeconds)+600;
                var completed=store.AdvanceHouseholdJointLifecycle(plan.Id,end,NativeInnCalendar.ForServiceAt);
                if(completed.Phase!=HouseholdJointPhase.Completed || !store.ReleaseCompletedHouseholdSimulation(plan.Id,end))
                    throw new InvalidOperationException("Native-source family did not finish its full unloaded return.");
                store=NativeReload(store);
                if(store.HasActiveHouseholdJoint(plan.SettlementId,plan.First.ResidentId) || store.ReadHouseholdJointSimulation(plan.SettlementId,plan.HomeKey).Active ||
                    store.ReadNpcInventory(plan.First.ResidentId).WalletGold!=firstGold || store.ReadNpcInventory(plan.Second.ResidentId).WalletGold!=secondGold)
                    throw new InvalidOperationException("Native reloaded home completion lost ownership or payment conservation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_OFFSCREEN_PASS: nativeCatalog=true; nativeTariff=true; actualWallets=true; productionInnAdapter=true; repeatedHandoff=true; nativeSerializer=true; simulatedHome=true; physicalHome=false; duplicatePayment=false");
            }
            finally
            {
                foreach(string id in new[]{plan.First.ResidentId,plan.Second.ResidentId})
                {ResidentActor actor;if(bodies.TryGetValue(id,out actor) && actor){actor.gameObject.SetActive(false);UnityEngine.Object.Destroy(actor.gameObject);}bodies.Remove(id);}
                foreach(var pair in old)if(pair.Value){bodies[pair.Key]=pair.Value;pair.Value.gameObject.SetActive(active[pair.Key]);}
                field.SetValue(streets,prior);
            }
        }
    }
}
