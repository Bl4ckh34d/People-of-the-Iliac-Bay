using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void CaptureObservedTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            if(GameManager.Instance.SaveLoadManager.LoadInProgress) return;
            // Native DFU has already faded the player's view before invoking
            // this callback. A rejected NPC checkpoint must never abort that
            // player's transition coroutine and leave the screen black.
            try {CaptureObservedVisits();}
            catch(Exception error){Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TRANSITION_CHECKPOINT_REJECTED: "+error);}
            try {YieldHouseholds(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());}
            catch(Exception error){Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TRANSITION_HOUSEHOLD_REJECTED: "+error);}
        }
        public void CaptureObservedVisits()
        { CaptureObservedVisits(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()); }
        private void CaptureObservedVisits(double now)
        {
            if(!interior || indoorGeometry==null || worldStore==null || population==null) return;
            CaptureHouseholdInns(now);
            CaptureBankVisitors(now);
            foreach(var pair in physicalShopVisits)
            {
                if(pair.Value.HouseholdDayId!=null)continue;
                var actor=FindActor(pair.Key);
                if(!actor || !actor.Resident.IsAlive || Reaction(pair.Key,now)!=null) continue;
                var saved=pair.Value.Capture(actor,interior,indoorGeometry,now,population.SettlementId);
                if(saved!=null) worldStore.StoreObservedVisit(saved);
            }
        }
        private bool RestoreObservedShopVisit(ResidentActor actor,double now,out PhysicalShopVisit physical)
        {
            physical=null;
            var saved=worldStore.GetObservedVisit(actor.Resident.Id);
            if(saved==null || saved.SettlementId!=population.SettlementId || saved.BuildingKey!=interior.EntryDoor.buildingKey ||
                now<saved.ObservedAt || now>=saved.EndsAt) return false;
            return RestoreShopCheckpoint(actor,saved,now,out physical);
        }
        private bool RestoreShopCheckpoint(ResidentActor actor,ObservedVisit saved,double now,out PhysicalShopVisit physical)
        {
            var state=saved.Evaluate(now);
            var ground=interior.transform.TransformPoint(new Vector3((float)state.Position.X,(float)state.Position.Y,(float)state.Position.Z));
            var motion=actor.GetComponent<CrowdMotion>();
            if(motion) motion.RestorePlacement(ground);
            else actor.transform.position=ground+Vector3.up;
            physical=new PhysicalShopVisit(saved,now);
            shopJourneys[actor.Resident.Id]=physical.Journey;
            return true;
        }
        private double ShopResumeUntil(ResidentActor actor,double seconds,double plannedEnd)
        {
            foreach(var plan in plans) if(plan.Resident.Id==actor.Resident.Id)
                return Math.Max(plannedEnd,ActiveSchedule(plan).ReturnHomeAt(seconds));
            return Math.Max(plannedEnd,worldStore.ResidentTripResumeUntil(actor.Resident.Id,plannedEnd));
        }
        private ActivitySnapshot ObservedSchedule(Plan plan,double now,DailySchedule schedule)
        {
            PhysicalShopVisit physical;
            if(interior && physicalShopVisits.TryGetValue(plan.Resident.Id,out physical) && physical.Snapshot.Phase!=ShopVisitPhase.Complete)
                return new ActivitySnapshot(Activity.Visit,interior.EntryDoor.buildingKey,new RoutePoint(),0);
            int town,building;double observed,end,planned,resume;
            if(!worldStore.TryObservedVisitMetadata(plan.Resident.Id,out town,out building,out observed,out end,out planned,out resume) ||
                town!=population.SettlementId || now<observed) return schedule.EvaluateSeconds(now);
            if(now<end) return new ActivitySnapshot(Activity.Visit,building,new RoutePoint(),0);
            if(CurrentRoutineRecovery(plan,now)!=null) return schedule.EvaluateSeconds(now);
            double shift=end-planned;
            return schedule.EvaluateSeconds(now<resume+shift ? now-shift : now);
        }
    }
}
