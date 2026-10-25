using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Semantic furniture IDs come from DFU's DefaultModelsCatalog. Positions,
    // reachability, private-room boundaries and counter sides come from layout.
    internal sealed class InnActivityLayout
    {
        internal sealed class Spot {public Vector3 Position,Target;public bool Seat;public FurnitureSeatSlot Slot;}
        internal readonly List<Spot> Spots=new List<Spot>();
        internal readonly List<Vector3> KeeperApproaches=new List<Vector3>();
        internal readonly FurnitureSeatCatalog Seating;
        internal bool HasCounterBarrier {get;private set;}
        private readonly DaggerfallInterior room;
        private readonly InteriorGeometry geometry;
        private readonly Func<Vector3,Vector3,bool> publicBoundary;
        private readonly Dictionary<Vector3,bool> publicPositions=new Dictionary<Vector3,bool>();
        private readonly Vector3 keeper,entry;
        private Vector3 customerDirection;
        internal Vector3 CustomerDirection {get {return customerDirection;}}

        internal InnActivityLayout(DaggerfallInterior room,InteriorGeometry geometry,Vector3 keeper,
            FurnitureSeatCatalog seating=null,Func<Vector3,Vector3,bool> boundary=null)
        {
            this.room=room;this.geometry=geometry;this.keeper=keeper;
            Seating=seating??FurnitureSeatCatalog.ForInterior(room);
            publicBoundary=boundary??TavernRoomSurvey.PublicAreaBoundary(room);
            entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
            ReadCounter();
            if(customerDirection.sqrMagnitude<0.1f) customerDirection=KeeperApproaches.Count>0 ? KeeperApproaches[0]-keeper : entry-keeper;
            customerDirection.y=0;customerDirection.Normalize();
            var tables=new List<Vector3>();foreach(var table in Seating.TableTargets)tables.Add(room.transform.InverseTransformPoint(table));
            int examined=0;
            foreach(var slot in Seating.Slots)
            {
                if(++examined>64||Spots.Count>=24)break;
                var seat=room.transform.InverseTransformPoint(slot.Ground);
                var front=seat-keeper;front.y=0;if(Vector3.Dot(front,customerDirection)<0.3f)continue;
                var target=seat+room.transform.InverseTransformDirection(slot.Facing);
                int approaches=0;
                foreach(var candidate in geometry.NearbyApproachPoints(seat,1.6f,0.3f,64))
                {
                    if(++approaches>12)break;
                    if(!IsPublic(candidate)||!publicBoundary(room.transform.TransformPoint(candidate),slot.Ground)||!Seating.SetApproach(slot,room.transform.TransformPoint(candidate)))continue;
                    if(Add(seat,target,true)){Spots[Spots.Count-1].Slot=slot;break;}
                }
            }
            // Unknown furniture still gets valid public common-room stops, not
            // speculative bedrooms/staff visits. Prefer nearby real tables.
            int publicStops=0;
            foreach(var anchor in geometry.LocalAnchors)
            {
                if(!IsPublic(anchor)) continue;
                Vector3 target=keeper;float nearest=36;
                foreach(var table in tables)
                    if((table-anchor).sqrMagnitude<nearest) {target=table;nearest=(table-anchor).sqrMagnitude;}
                if(Add(anchor,target,false))publicStops++;
                if(publicStops>=16) break;
            }
        }
        private bool Add(Vector3 position,Vector3 target,bool seat)
        {
            foreach(var existing in Spots) if((existing.Position-position).sqrMagnitude<0.639f) return false;
            Spots.Add(new Spot {Position=position,Target=target,Seat=seat});return true;
        }
        internal bool IsPublic(Vector3 local)
        {
            bool result;if(publicPositions.TryGetValue(local,out result)) return result;
            var front=local-keeper;front.y=0;
            result=customerDirection.sqrMagnitude<0.1f || Vector3.Dot(front,customerDirection)>=0.3f;
            if(result)
            {
                var route=geometry.RouteBetween(entry,local);result=route.Count>=2;
                for(int i=1;result && i<route.Count;i++)
                    result=publicBoundary(World(route[i-1]),World(route[i]));
            }
            publicPositions[local]=result;return result;
        }
        private Vector3 World(GuardPoint3 point)
        {return room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));}
        internal bool AllowsPublicRoute(IList<GuardPoint3> route)
        {
            if(route==null || route.Count<2)return false;
            for(int i=1;i<route.Count;i++)if(!publicBoundary(World(route[i-1]),World(route[i])))return false;
            return true;
        }
        private void ReadCounter()
        {
            var across=new List<Vector3>();var fallback=new List<Vector3>();
            var normals=new Dictionary<Vector3,Vector3>();
            var towardEntry=entry-keeper;towardEntry.y=0;towardEntry.Normalize();
            foreach(var candidate in geometry.NearbyApproachPoints(keeper,4.5f,2,1024))
            {
                var delta=candidate-keeper;delta.y=0;
                if(delta.sqrMagnitude<1 || delta.sqrMagnitude>20.25f || Vector3.Dot(delta,towardEntry)<0) continue;
                var head=room.transform.TransformPoint(new Vector3(keeper.x,candidate.y+1.25f,keeper.z));
                var target=room.transform.TransformPoint(candidate)+Vector3.up*1.25f;
                if(!CivilianResponses.Sees(head,target,4.6f)) continue;
                fallback.Add(candidate);
                var from=room.transform.TransformPoint(new Vector3(keeper.x,candidate.y+0.65f,keeper.z));
                var to=room.transform.TransformPoint(candidate)+Vector3.up*0.65f;var ray=to-from;
                var hits=Physics.RaycastAll(from,ray.normalized,ray.magnitude,~0,QueryTriggerInteraction.Ignore);
                Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
                foreach(var hit in hits)
                    if(CivilianResponses.Blocker(hit.collider) && !hit.collider.GetComponentInParent<DaggerfallActionDoor>())
                    {
                        var normal=room.transform.InverseTransformDirection(-hit.normal);normal.y=0;normal.Normalize();
                        if(Vector3.Dot(normal,delta.normalized)>0.5f) {across.Add(candidate);normals[candidate]=normal;}
                        break;
                    }
            }
            HasCounterBarrier=across.Count>0;
            var choices=HasCounterBarrier?across:fallback;
            choices.Sort((a,b)=>{var x=a-keeper;var y=b-keeper;x.y=y.y=0;return x.sqrMagnitude.CompareTo(y.sqrMagnitude);});
            int checkedRoutes=0;var reachable=new List<GuardPoint3>();Vector3 anchor=Vector3.zero;bool found=false;
            foreach(var candidate in choices)
            {
                // Discover the nearest public frontage first. Subsequent route
                // checks only inspect that band, never rows behind the counter.
                if(checkedRoutes++>=24) break;
                var route=geometry.RouteBetween(entry,candidate);if(route.Count<2) continue;
                bool allowed=true;
                for(int i=1;i<route.Count && allowed;i++) allowed=publicBoundary(World(route[i-1]),World(route[i]));
                if(!allowed) continue;
                anchor=candidate;found=true;break;
            }
            if(!found) return;
            customerDirection=normals.ContainsKey(anchor) ? normals[anchor] : towardEntry;
            float depth=Vector3.Dot(anchor-keeper,customerDirection);
            checkedRoutes=0;
            foreach(var candidate in choices)
            {
                if(Math.Abs(candidate.y-anchor.y)>0.35f || Math.Abs(Vector3.Dot(candidate-keeper,customerDirection)-depth)>0.21f ||
                    normals.ContainsKey(candidate) && Vector3.Dot(normals[candidate],customerDirection)<0.9f) continue;
                if(checkedRoutes++>=48) break;
                var route=geometry.RouteBetween(entry,candidate);if(route.Count<2) continue;
                bool allowed=true;
                for(int i=1;i<route.Count && allowed;i++) allowed=publicBoundary(World(route[i-1]),World(route[i]));
                if(allowed) reachable.Add(new GuardPoint3 {X=candidate.x,Y=candidate.y,Z=candidate.z});
            }
            foreach(var p in CounterServiceSlots.Align(reachable,new GuardPoint3 {X=keeper.x,Y=keeper.y,Z=keeper.z},
                new GuardPoint3 {X=customerDirection.x,Z=customerDirection.z}))
                KeeperApproaches.Add(new Vector3((float)p.X,(float)p.Y,(float)p.Z));
        }
    }
}
