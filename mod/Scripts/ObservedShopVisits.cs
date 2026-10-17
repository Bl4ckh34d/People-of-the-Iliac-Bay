using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private void CaptureObservedTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            if(GameManager.Instance.SaveLoadManager.LoadInProgress) return;
            CaptureObservedVisits();
        }
        public void CaptureObservedVisits()
        {
            if(!interior || indoorGeometry==null || worldStore==null || population==null) return;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach(var pair in physicalShopVisits)
            {
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
            double shift=end-planned;
            return schedule.EvaluateSeconds(now<resume+shift ? now-shift : now);
        }
    }
}
