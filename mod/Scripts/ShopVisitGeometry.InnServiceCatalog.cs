using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ShopVisitGeometry
    {
        // Passive native record source. No keeper component, actors or claims.
        internal ShopVisitGeometry(DaggerfallInterior room,InteriorGeometry geometry,Vector3 keeper,
            FurnitureSeatCatalog seating,Func<Vector3,Vector3,bool> boundary,DFBlock sourceBlock)
        {
            this.room=room;this.geometry=geometry;IsTavern=true;counter=keeper;
            if(!geometry.EntryGround.HasValue)return;
            foreach(var bed in BedGeometry.Read(room,sourceBlock))
            {Vector3 ground;if(geometry.StandBesideBed(bed,out ground))tavernBedGrounds.Add(ground);}
            innLayout=new InnActivityLayout(room,geometry,keeper,seating,boundary);
            foreach(var spot in innLayout.Spots)if(!NearTavernBed(spot.Position))
            {shelves.Add(spot.Position);browsingFacing[spot.Position]=spot.Target;if(spot.Slot!=null)seatStops[spot.Position]=spot.Slot;}
        }
        internal bool OwnsInnServiceSource(DaggerfallInterior source,NativePerson keeper)
        {
            return room && ReferenceEquals(room,source) && IsTavern && innLayout!=null && geometry.EntryGround.HasValue &&
                tavernKeeper && keeper!=null && keeper.IsAlive && keeper.WorkKey==room.EntryDoor.buildingKey &&
                keeper.NativeHash==tavernKeeper.Data.hash;
        }
        private sealed class ServiceSeat
        {
            internal FurnitureSeatSlot Slot;
            internal TableSeatPosition Position;
            internal List<GuardPoint3> FromCounter,ToEntry;
        }
        // A capacity snapshot only: no actors, availability checks or leases.
        // Reuse the already surveyed layout, spreading route work across frames.
        internal IEnumerator CaptureInnServiceGeometry(NpcInnServiceCatalog draft,Func<bool> current)
        {
            if(draft==null || current==null || !current() || !room || innLayout==null ||
                !geometry.EntryGround.HasValue)yield break;
            var entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
            Vector3 counterStop=Vector3.zero;bool found=false;
            foreach(var candidate in innLayout.KeeperApproaches)
            {
                if(!current() || !room)yield break;
                var route=BuildCounterRoute(entry,candidate,true);
                if(route.Count>=2){draft.EntryToCounter=route;counterStop=candidate;found=true;}
                yield return null;
                if(found)break;
            }
            if(!found)yield break;
            var slots=new List<FurnitureSeatSlot>(seatStops.Values);
            slots.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
            var seats=new List<ServiceSeat>();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var slot in slots)
            {
                if(!current() || !room)yield break;
                if(!slot.HasApproach || !seen.Add(slot.Id))continue;
                var ground=room.transform.InverseTransformPoint(slot.Ground);
                var surface=room.transform.InverseTransformPoint(slot.Surface);
                var facing=room.transform.InverseTransformDirection(slot.Facing);
                var from=ActivityRouteBetween(counterStop,ground);
                yield return null;
                if(!current() || !room)yield break;
                var exit=ActivityRouteBetween(ground,entry);
                yield return null;
                if(!current() || !room)yield break;
                if(from.Count<2 || exit.Count<2 || !innLayout.AllowsPublicRoute(from) || !innLayout.AllowsPublicRoute(exit))continue;
                seats.Add(new ServiceSeat{Slot=slot,Position=new TableSeatPosition{Id=slot.Id,TableId=slot.TableId,
                    X=ground.x,Z=ground.z,Height=surface.y,FacingX=facing.x,FacingZ=facing.z},FromCounter=from,ToEntry=exit});
            }
            if(!current() || !room)yield break;
            int points=draft.EntryToCounter.Count;
            for(int i=0;i<seats.Count;i++)for(int j=i+1;j<seats.Count;j++)
            {
                if(draft.Pairs.Count>=32)yield break;
                var a=seats[i];var b=seats[j];
                if(!Seating.CanShareTable(a.Slot.Id,b.Slot.Id))continue;
                int count=a.FromCounter.Count+b.FromCounter.Count+a.ToEntry.Count+b.ToEntry.Count;
                if(points+count>20000)continue;
                draft.Pairs.Add(new NpcInnServiceSeatPair{First=a.Position,Second=b.Position,FirstCounterToSeat=a.FromCounter,
                    SecondCounterToSeat=b.FromCounter,FirstSeatToEntry=a.ToEntry,SecondSeatToEntry=b.ToEntry});points+=count;
            }
            if(draft.Pairs.Count>0)NpcInnServiceCatalog.CanonicalizeMeasuredRoutes(draft);
        }
    }
}
