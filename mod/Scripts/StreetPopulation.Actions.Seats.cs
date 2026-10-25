using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private FurnitureSeatCatalog ActionSeatCatalog()
        {
            if(interior) return shopGeometry==null?null:shopGeometry.Seating;
            return directory?FurnitureSeatCatalog.ForLoadedScene(directory.transform,ActionWorldSpace):null;
        }
        private void OfferSeatAction(ResidentActor actor,string text,List<NpcActionOption> options,bool semantic=true)
        {
            var catalog=ActionSeatCatalog();if(!catalog) return;
            if(!interior) OutdoorSeatActivity.PrepareNearby(catalog,actor.transform.position-Vector3.up);
            FurnitureSeatSlot nearest=null;float distance=64;
            foreach(var slot in catalog.Slots)
            {
                float square=(slot.Ground-actor.transform.position+Vector3.up).sqrMagnitude;
                if(!slot.HasApproach || square>=distance) continue;
                if(interior?!shopGeometry.CanTakeSeat(actor,slot.Id):!catalog.Available(actor.Resident.Id,slot.Id,Time.unscaledTime)) continue;
                distance=square;nearest=slot;
            }
            if(nearest==null) return;
            var target=ActionPosition(nearest.Approach);target.ContainerId=nearest.Id;
            AddActionOption(options,NpcActionKind.TakeSeat,target,"take the nearest surveyed seat if it remains available",text,semantic);
        }
        private bool AcquireActionSeat(NpcActionJob job,ResidentActor actor,ActionMotion motion,out string reason)
        {
            reason="The selected seat is unavailable or no longer has a safe approach.";
            if(job.Target==null || !ActionSpaceMatches(job.Target) || string.IsNullOrWhiteSpace(job.Target.ContainerId)) return false;
            var catalog=ActionSeatCatalog();var slot=catalog?catalog.Find(job.Target.ContainerId):null;
            if(slot==null || !slot.HasApproach) return false;
            SelectedSeatAccess access;
            bool available=interior?shopGeometry.TrySeat(actor,slot.Id,out access):catalog.TryAcquire(actor.Resident.Id,slot.Id,out access);
            if(!available) return false;
            motion.Seat=access;return true;
        }
        private void ReleaseActionSeat(string residentId)
        {
            ActionMotion motion;
            if(actionMotions.TryGetValue(residentId,out motion) && motion.Seat!=null) {motion.Seat.Release();motion.Seat=null;}
            if(motion!=null && motion.DepartureSeat!=null) {motion.DepartureSeat.Release();motion.DepartureSeat=null;}
        }
        private bool LeaveRoutineSeatForAction(ResidentActor actor,NpcActionJob job,ActionMotion motion,double now)
        {
            var access=motion.DepartureSeat;
            if(now>=job.DeadlineAt || !access.Refresh())
            {
                access.Release();motion.DepartureSeat=null;
                EndAction(job,now,"The original seat exit is no longer available.",true);return false;
            }
            bool left=access.Advance(actor,true);
            if(Time.unscaledTime>=motion.SaveAt)
            {
                motion.SaveAt=Time.unscaledTime+0.5f;
                worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),job.ReturningToRoutine,now);
            }
            motion.LastProgress=actor.transform.position;motion.ProgressAt=Time.unscaledTime;
            if(!left)return false;
            access.Release();motion.DepartureSeat=null;motion.Indoor=null;motion.Outdoor=null;motion.RepathAt=0;
            return true;
        }
        private bool LeaveActionSeat(ResidentActor actor,ActionMotion motion)
        {
            if(motion.Seat==null) return true;
            if(!motion.Seat.Refresh()) {motion.Seat=null;return true;}
            if(Vector3.Distance(actor.transform.position-Vector3.up,motion.Seat.Slot.Ground)<1.6f && !motion.Seat.Advance(actor,true)) return false;
            motion.Seat.Release();motion.Seat=null;motion.SeatEntry=false;motion.Indoor=null;motion.Outdoor=null;motion.RepathAt=0;
            return true;
        }
    }
}
