using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ShopVisitGeometry
    {
        private readonly DaggerfallInterior room;
        private readonly InteriorGeometry geometry;
        private readonly List<Vector3> shelves=new List<Vector3>();
        private readonly Dictionary<Vector3,Transform> shelfOwners=new Dictionary<Vector3,Transform>();
        private readonly Dictionary<Vector3,Vector3> browsingFacing=new Dictionary<Vector3,Vector3>();
        private readonly Dictionary<Vector3,List<Vector3>> standing=new Dictionary<Vector3,List<Vector3>>();
        private readonly Func<StaticNPC,bool> livingTavernKeeper;
        private readonly List<Vector3> tavernBedGrounds=new List<Vector3>();
        private Vector3? counter;
        private Transform counterOwner;
        private StaticNPC tavernKeeper;
        private InnActivityLayout innLayout;
        private readonly Dictionary<Vector3,FurnitureSeatSlot> seatStops=new Dictionary<Vector3,FurnitureSeatSlot>();
        public FurnitureSeatCatalog Seating {get{return innLayout==null?null:innLayout.Seating;}}
        private CounterServiceSlots counterClaims;
        private sealed class CounterRoute {public Vector3 Position;public float CheckedAt;}
        private readonly Dictionary<string,CounterRoute> counterRoutes=new Dictionary<string,CounterRoute>();
        private sealed class CounterCrowdCheck {public float CheckedAt;public bool Free;}
        private readonly Dictionary<string,Dictionary<Vector3,CounterCrowdCheck>> counterCrowds=new Dictionary<string,Dictionary<Vector3,CounterCrowdCheck>>();
        private sealed class CounterWait {public Vector3 Position;public float Until;}
        private readonly Dictionary<string,CounterWait> counterWaiters=new Dictionary<string,CounterWait>();
        public bool IsTavern { get; private set; }
        public StaticNPC TavernKeeper { get { return tavernKeeper; } }
        public Vector3? CounterPosition { get { return counter; } }
        private readonly Dictionary<string,List<ShopOccupancy>> reserved=new Dictionary<string,List<ShopOccupancy>>();
        private readonly Dictionary<string,string> rejectedVisits=new Dictionary<string,string>();
        private string candidateFailure;
        public string RejectionFor(string id) { string reason;return rejectedVisits.TryGetValue(id,out reason)?reason:null; }
        public ShopVisitGeometry(DaggerfallInterior room,InteriorGeometry geometry,Func<StaticNPC,bool> livingTavernKeeper=null)
        {
            this.room=room; this.geometry=geometry;
            this.livingTavernKeeper=livingTavernKeeper;
            IsTavern=room.BuildingData.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Tavern;
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
                if(loot.ContainerType==LootContainerTypes.ShopShelves)
                {
                    var local=room.transform.InverseTransformPoint(loot.transform.position);
                    shelves.Add(local); shelfOwners[local]=loot.transform;
                }
            shelves.Sort((a,b)=>{ int x=a.x.CompareTo(b.x); return x!=0 ? x : a.z.CompareTo(b.z); });
            foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
            {
                if(NativeBusinessServices.IsKeeper(npc,room.BuildingData.BuildingType) &&
                    (!IsTavern || this.livingTavernKeeper!=null && this.livingTavernKeeper(npc)))
                {
                    counter=room.transform.InverseTransformPoint(npc.transform.position);
                    counterOwner=npc.transform;
                    if(IsTavern) tavernKeeper=npc;
                    break;
                }
            }
            if(IsTavern && counter.HasValue)
            {
                shelves.Clear();
                foreach(var bed in BedGeometry.Read(room))
                {
                    Vector3 ground;
                    if(geometry.StandBesideBed(bed,out ground)) tavernBedGrounds.Add(ground);
                }
                if(geometry.EntryGround.HasValue)
                {
                    innLayout=new InnActivityLayout(room,geometry,counter.Value);
                    var slots=new List<GuardPoint3>();foreach(var p in innLayout.KeeperApproaches)
                        slots.Add(new GuardPoint3 {X=p.x,Y=p.y,Z=p.z});
                    counterClaims=new CounterServiceSlots(slots);
                    foreach(var spot in innLayout.Spots) if(!NearTavernBed(spot.Position))
                    {shelves.Add(spot.Position);browsingFacing[spot.Position]=spot.Target;if(spot.Slot!=null)seatStops[spot.Position]=spot.Slot;}
                }
            }
        }
        public Vector3 Face(Vector3 position,ShopVisitPhase phase)
        {
            if(phase==ShopVisitPhase.Haggling && counter.HasValue) return counter.Value-position;
            Vector3 target;
            // A chosen stop retains its validated activity target. Looking up
            // the nearest shelf again can select one across the partition wall.
            return browsingFacing.TryGetValue(position,out target) ? target-position : Vector3.zero;
        }
        public void ResetReservations() { reserved.Clear(); }
        public void ReleaseCounter(string id,bool releaseWaiting=true) {if(counterClaims!=null) counterClaims.Release(id);counterRoutes.Remove(id);counterCrowds.Remove(id);if(releaseWaiting)counterWaiters.Remove(id);}
        public bool TryCounterPosition(ResidentActor actor,Vector3 preferred,out Vector3 chosen)
        {
            chosen=preferred;if(!IsTavern || counterClaims==null) return true;
            int first=0;float distance=float.MaxValue;
            for(int i=0;i<innLayout.KeeperApproaches.Count;i++)
            {float d=(innLayout.KeeperApproaches[i]-preferred).sqrMagnitude;if(d<distance){first=i;distance=d;}}
            var actual=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            GuardPoint3 point;
            bool available=counterClaims.TryClaim(actor.Resident.Id,Time.unscaledTime,first,p=>{
                var local=new Vector3((float)p.X,(float)p.Y,(float)p.Z);var world=room.transform.TransformPoint(local);
                Dictionary<Vector3,CounterCrowdCheck> checks;
                if(!counterCrowds.TryGetValue(actor.Resident.Id,out checks))
                {checks=new Dictionary<Vector3,CounterCrowdCheck>();counterCrowds[actor.Resident.Id]=checks;}
                CounterCrowdCheck check;
                if(!checks.TryGetValue(local,out check) || Time.unscaledTime-check.CheckedAt>=0.25f)
                {
                    bool free=true;var neighbours=StreetPopulation.ShopDepartureNeighbours(world,actor);
                    foreach(var other in neighbours)
                    {var d=other-world;d.y=0;if(Math.Abs(other.y-world.y)<2.1f && d.sqrMagnitude<(CrowdMotion.Separation+0.08f)*(CrowdMotion.Separation+0.08f)) {free=false;break;}}
                    check=new CounterCrowdCheck {CheckedAt=Time.unscaledTime,Free=free};checks[local]=check;
                }
                if(!check.Free) return false;
                if((local-actual).sqrMagnitude<0.04f) return true;
                CounterRoute cached;
                if(counterRoutes.TryGetValue(actor.Resident.Id,out cached) && (cached.Position-local).sqrMagnitude<0.001f &&
                    Time.unscaledTime-cached.CheckedAt<10) return true;
                if(CounterRouteBetween(actual,local).Count<2) return false;
                counterRoutes[actor.Resident.Id]=new CounterRoute {Position=local,CheckedAt=Time.unscaledTime};return true;
            },out point);
            if(available) {chosen=new Vector3((float)point.X,(float)point.Y,(float)point.Z);counterWaiters.Remove(actor.Resident.Id);}
            return available;
        }
        public bool TryCounterWaitingPosition(ResidentActor actor,out Vector3 chosen)
        {
            chosen=Vector3.zero;if(!IsTavern || innLayout==null || shelves.Count==0 || !geometry.EntryGround.HasValue) return false;
            float now=Time.unscaledTime;
            foreach(var id in new List<string>(counterWaiters.Keys)) if(counterWaiters[id].Until<now) counterWaiters.Remove(id);
            CounterWait current;
            if(counterWaiters.TryGetValue(actor.Resident.Id,out current)) {current.Until=now+2;chosen=current.Position;return true;}
            var entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
            var actual=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            for(int i=0;i<shelves.Count;i++)
            {
                var point=shelves[(i+(int)(actor.Resident.AppearanceSeed%(uint)shelves.Count))%shelves.Count];
                if(seatStops.ContainsKey(point))continue;
                if((point-entry).sqrMagnitude<6.25f) continue;
                bool busy=false;
                foreach(var counterSpot in innLayout.KeeperApproaches) if((point-counterSpot).sqrMagnitude<1.44f) {busy=true;break;}
                foreach(var wait in counterWaiters.Values) if((wait.Position-point).sqrMagnitude<0.64f) {busy=true;break;}
                if(busy) continue;
                var world=room.transform.TransformPoint(point);
                foreach(var other in StreetPopulation.ShopDepartureNeighbours(world,actor))
                {var d=other-world;d.y=0;if(Math.Abs(other.y-world.y)<2.1f && d.sqrMagnitude<0.64f){busy=true;break;}}
                if(busy || geometry.RouteBetween(actual,point).Count<2) continue;
                counterWaiters[actor.Resident.Id]=new CounterWait {Position=point,Until=now+2};chosen=point;return true;
            }
            return false;
        }
        public List<GuardPoint3> CounterRouteBetween(Vector3 actual,Vector3 target)
        {return BuildCounterRoute(actual,target,false);}
        private List<GuardPoint3> BuildCounterRoute(Vector3 actual,Vector3 target,bool strict)
        {
            List<GuardPoint3> Fallback(){return strict?new List<GuardPoint3>():ActivityRouteBetween(actual,target);}
            if(!IsTavern || innLayout==null) return Fallback();
            var normal=innLayout.CustomerDirection;Vector3 endApron,startApron;
            if(!CounterApron(target+normal,target,out endApron)) return Fallback();
            float depth=Vector3.Dot(actual-target,normal);
            if(depth>=0.8f) startApron=actual;
            else if(!CounterApron(actual+normal*(1-depth),target,out startApron)) return Fallback();
            var result=new List<GuardPoint3>();var from=actual;
            foreach(var stop in new[]{startApron,endApron,target})
            {
                if((stop-from).sqrMagnitude<0.0001f) continue;
                var segment=result.Count==0?ActivityRouteBetween(from,stop):geometry.RouteBetween(from,stop);
                if(segment.Count<2) return Fallback();
                for(int i=result.Count==0 ? 0 : 1;i<segment.Count;i++) result.Add(segment[i]);
                from=stop;
            }
            return strict && !innLayout.AllowsPublicRoute(result)?new List<GuardPoint3>():result;
        }
        private bool CounterApron(Vector3 desired,Vector3 counterSpot,out Vector3 chosen)
        {
            var normal=innLayout.CustomerDirection;
            foreach(var p in geometry.NearbyApproachPoints(desired,0.6f,0.3f,32))
                if(Vector3.Dot(p-counterSpot,normal)>=0.7f && innLayout.IsPublic(p)) {chosen=p;return true;}
            chosen=Vector3.zero;return false;
        }
        public bool TryFreeBrowsePosition(Vector3 occupied,ResidentActor actor,double now,out Vector3 chosen)
        {
            chosen=occupied;
            if(seatStops.ContainsKey(occupied))return false;
            Vector3 target;
            if(!browsingFacing.TryGetValue(occupied,out target)) return false;
            var crowd=StreetPopulation.ShopDepartureNeighbours(room.transform.TransformPoint(occupied),actor);
            var neighbours=new List<GuardPoint3>();
            foreach(var other in crowd) neighbours.Add(new GuardPoint3 {X=other.x,Y=other.y,Z=other.z});
            var world=room.transform.TransformPoint(occupied);
            if(!CrowdSteering.Occupied(new GuardPoint3 {X=world.x,Y=world.y,Z=world.z},neighbours,CrowdMotion.Separation)) return false;
            Transform owner;shelfOwners.TryGetValue(target,out owner);
            var actual=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            foreach(var candidate in geometry.NearbyApproachPoints(occupied,1.2f,0.15f,64))
            {
                var point=room.transform.TransformPoint(candidate);
                if(NearTavernBed(candidate) || IsTavern && innLayout!=null && !innLayout.IsPublic(candidate) ||
                    CrowdSteering.Occupied(new GuardPoint3 {X=point.x,Y=point.y,Z=point.z},neighbours,
                    CrowdMotion.Separation+0.08f) || !CanSeeTarget(candidate,target,owner)) continue;
                bool claimed=false;
                foreach(var pair in reserved) if(pair.Key!=actor.Resident.Id) foreach(var claim in pair.Value)
                    if(claim.Start<=now && claim.End>now && Math.Abs(claim.Position.Y-candidate.y)<2.1 &&
                        (new Vector3((float)claim.Position.X,(float)claim.Position.Y,(float)claim.Position.Z)-candidate).sqrMagnitude<
                        CrowdMotion.Separation*CrowdMotion.Separation) claimed=true;
                if(claimed || geometry.RouteBetween(actual,candidate).Count<2) continue;
                List<ShopOccupancy> claims;
                if(!reserved.TryGetValue(actor.Resident.Id,out claims)) {claims=new List<ShopOccupancy>();reserved.Add(actor.Resident.Id,claims);}
                claims.Add(new ShopOccupancy {Start=now,End=now+30,Position=new GuardPoint3 {X=candidate.x,Y=candidate.y,Z=candidate.z}});
                browsingFacing[candidate]=target;chosen=candidate;
                return true;
            }
            return false;
        }
        public ShopVisitJourney Create(string id,uint seed,double start,double end)
        {
            rejectedVisits.Remove(id);candidateFailure=null;
            var rejected=new List<Vector3>();
            for(int attempt=0;attempt<8;attempt++)
            {
                var journey=CreateCandidate(seed,start,end,rejected,attempt);
                if(journey==null) {rejectedVisits[id]=candidateFailure??"No usable shelf/counter anchors.";continue;}
                var occupancy=new List<ShopOccupancy>(journey.Occupancies());bool conflict=false;
                foreach(var current in occupancy) foreach(var pair in reserved)
                {
                    if(pair.Key==id) continue;
                    foreach(var previous in pair.Value)
                    {
                        double dx=current.Position.X-previous.Position.X,dz=current.Position.Z-previous.Position.Z;
                        if(current.Start<previous.End && current.End>previous.Start &&
                            Math.Abs(current.Position.Y-previous.Position.Y)<2 && dx*dx+dz*dz<CrowdMotion.Separation*CrowdMotion.Separation)
                        {rejected.Add(new Vector3((float)current.Position.X,(float)current.Position.Y,(float)current.Position.Z));conflict=true;}
                    }
                }
                if(conflict) continue;
                reserved[id]=occupancy;rejectedVisits.Remove(id);return journey;
            }
            if(!rejectedVisits.ContainsKey(id)) rejectedVisits[id]="Activity reservations exhausted eight alternatives.";
            return null;
        }
        private ShopVisitJourney CreateCandidate(uint seed,double start,double end,List<Vector3> rejected,int alternative)
        {
            if(shelves.Count==0 || !geometry.EntryGround.HasValue) return null;
            if(IsTavern)
            {
                if(!tavernKeeper || livingTavernKeeper==null || !livingTavernKeeper(tavernKeeper)) return null;
                counter=room.transform.InverseTransformPoint(tavernKeeper.transform.position);
            }
            uint visitSeed=StableSeed.Combine(seed,(uint)room.EntryDoor.buildingKey,(uint)Math.Floor(start/86400),0x53484f50);
            bool purchase=IsTavern || (visitSeed&1)==0;
            if(purchase && !counter.HasValue) return null;
            var browsing=new List<Vector3>();
            var actualSeats=new List<Vector3>(seatStops.Keys);actualSeats.Sort((a,b)=>{int x=a.x.CompareTo(b.x);return x!=0?x:a.z.CompareTo(b.z);});
            int wanted=IsTavern?2+(int)(visitSeed%2):3+(int)(visitSeed%3);
            for(int i=0;i<wanted;i++)
            {
                // Inspect two sections of a shelf before choosing another shelf.
                var target=IsTavern&&i==0&&actualSeats.Count>0 ? actualSeats[(int)((visitSeed+(uint)alternative)%(uint)actualSeats.Count)] :
                    shelves[(int)((visitSeed+(uint)(IsTavern?i:i/2)+(uint)alternative)%(uint)shelves.Count)];
                var stop=IsTavern ? FreeInnStop(target,browsing,rejected) : Near(target,browsing,rejected,true);
                if(stop.HasValue) browsing.Add(stop.Value);
            }
            var keeper=purchase ? (IsTavern ? InnKeeperStop(browsing,rejected) : Near(counter.Value,browsing,rejected,false,
                (float)SocialInteractionSpacing.MinimumMeters,(float)SocialInteractionSpacing.MaximumMeters)) : (Vector3?)null;
            if(browsing.Count==0 || purchase && !keeper.HasValue) return null;
            var entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
            while(browsing.Count>=1)
            {
                var stops=new List<Vector3>(browsing);
                if(purchase) {if(IsTavern) stops.Insert(0,keeper.Value);else stops.Add(keeper.Value);}
                var routes=new List<List<GuardPoint3>>(); var from=entry;
                foreach(var stop in stops) { routes.Add(ActivityRouteBetween(from,stop)); from=stop; }
                routes.Add(ActivityRouteBetween(from,entry));
                try
                {
                    var journey=ShopVisitJourney.Create(start,end,routes,ResidentMovement.WalkSpeedFor(seed),DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.TimeScale,purchase,visitSeed,IsTavern);
                    for(int i=0;i<stops.Count;i++) {FurnitureSeatSlot slot;if(seatStops.TryGetValue(stops[i],out slot)&&journey.StopPhase(i)==ShopVisitPhase.Browsing)journey.AssignSeat(i,slot.Id);}
                    return journey;
                }
                catch(ArgumentException error) { candidateFailure=error.Message; browsing.RemoveAt(browsing.Count-1); }
            }
            return null;
        }
        private Vector3? FreeInnStop(Vector3 target,List<Vector3> chosen,List<Vector3> rejected)
        {
            if(rejected.Contains(target)) return null;
            foreach(var other in chosen) if((target-other).sqrMagnitude<0.64f) return null;
            return target;
        }
        public bool TrySeat(ResidentActor actor,string id,out SelectedSeatAccess access)
        {
            access=null;return CanTakeSeat(actor,id)&&Seating.TryAcquire(actor.Resident.Id,id,out access);
        }
        internal bool TryCreateSharedInn(ResidentActor first,ResidentActor second,double now,double firstEnd,double secondEnd,
            out ShopVisitJourney firstJourney,out ShopVisitJourney secondJourney,out SelectedSeatPairAccess seats)
        {
            firstJourney=null;secondJourney=null;seats=null;
            if(!first || !second || first==second || !IsTavern || Seating==null || !geometry.EntryGround.HasValue ||
                !tavernKeeper || livingTavernKeeper==null || !livingTavernKeeper(tavernKeeper))return false;
            var slots=new List<FurnitureSeatSlot>(seatStops.Values);
            slots.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
            if(slots.Count<2)return false;
            int start=(int)(first.Resident.AppearanceSeed%(uint)slots.Count);
            for(int i=0;i<slots.Count;i++)for(int j=0;j<slots.Count;j++)
            {
                var a=slots[(start+i)%slots.Count];var b=slots[(start+j)%slots.Count];
                if(!Seating.CanShareTable(a.Id,b.Id) || !CanTakeSeat(first,a.Id) || !CanTakeSeat(second,b.Id))continue;
                var firstPlan=SharedInnJourney(first,a,now,firstEnd);if(firstPlan==null)continue;
                var secondPlan=SharedInnJourney(second,b,now,secondEnd);if(secondPlan==null)continue;
                SelectedSeatPairAccess access;
                if(!Seating.TryAcquirePair(first.Resident.Id,a.Id,second.Resident.Id,b.Id,out access))continue;
                firstJourney=firstPlan;secondJourney=secondPlan;seats=access;return true;
            }
            return false;
        }
        private ShopVisitJourney SharedInnJourney(ResidentActor actor,FurnitureSeatSlot seat,double now,double end)
        {
            var keeper=InnKeeperStop(new List<Vector3>(),new List<Vector3>());if(!keeper.HasValue)return null;
            var entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
            var approach=room.transform.InverseTransformPoint(seat.Approach);var ground=room.transform.InverseTransformPoint(seat.Ground);
            var routes=new List<List<GuardPoint3>>{CounterRouteBetween(entry,keeper.Value),ActivityRouteBetween(keeper.Value,approach),
                ActivityRouteBetween(approach,ground),ActivityRouteBetween(ground,entry)};
            try{return ShopVisitJourney.CreateSharedInn(now,end,routes,actor.WalkingSpeed,DaggerfallUnity.Instance.WorldTime.TimeScale,
                actor.Resident.AppearanceSeed,seat.Id);}
            catch(ArgumentException){return null;}
        }
        public bool CanTakeSeat(ResidentActor actor,string id)
        {
            if(Seating==null)return false;
            var slot=Seating.Find(id);if(slot==null||!seatStops.ContainsValue(slot))return false;
            foreach(var other in StreetPopulation.ShopDepartureNeighbours(slot.Ground,actor))
            {var delta=other-slot.Ground;delta.y=0;if(Math.Abs(other.y-slot.Ground.y)<2.1f&&delta.sqrMagnitude<CrowdMotion.Separation*CrowdMotion.Separation)return false;}
            return Seating.Available(actor.Resident.Id,id,Time.unscaledTime);
        }
        public List<GuardPoint3> ActivityRouteBetween(Vector3 actual,Vector3 target,SelectedSeatAccess egressAccess=null)
        {
            FurnitureSeatSlot startSeat,endSeat;seatStops.TryGetValue(actual,out startSeat);seatStops.TryGetValue(target,out endSeat);
            var worldActual=room.transform.TransformPoint(actual);
            // During observed chair egress the body can be beyond the seat
            // center lookup while its capsule is still inside that chair's
            // apron. Preserve the exact reserved exit prefix until clear.
            if(egressAccess!=null && egressAccess.Valid && egressAccess.Permits(worldActual,egressAccess.Slot.Approach) &&
                egressAccess.Slot.Furniture.Blocks(worldActual,worldActual,CrowdMotion.BodyRadius))startSeat=egressAccess.Slot;
            if(startSeat==null)foreach(var pair in seatStops)if((pair.Key-actual).sqrMagnitude<0.1225f){startSeat=pair.Value;break;}
            var start=startSeat==null?actual:room.transform.InverseTransformPoint(startSeat.Approach);
            var end=endSeat==null?target:room.transform.InverseTransformPoint(endSeat.Approach);
            var result=(start-end).sqrMagnitude<0.000001f?new List<GuardPoint3>{Point(start)}:geometry.RouteBetween(start,end);
            if(result.Count==0)return result;
            if(startSeat!=null)result.Insert(0,Point(actual));
            if(endSeat!=null)result.Add(Point(target));
            return result;
        }
        private static GuardPoint3 Point(Vector3 p){return new GuardPoint3 {X=p.x,Y=p.y,Z=p.z};}
        private Vector3? InnKeeperStop(List<Vector3> chosen,List<Vector3> rejected)
        {
            if(innLayout==null) return null;
            foreach(var point in innLayout.KeeperApproaches)
                if(FreeInnStop(point,chosen,rejected).HasValue) return point;
            return null;
        }
        private Vector3? Near(Vector3 target,List<Vector3> chosen,List<Vector3> rejected,bool socialStop,float minimumDistance=0,float maximumDistance=2)
        {
            float minimumSquare=minimumDistance*minimumDistance,maximumSquare=maximumDistance*maximumDistance;
            float distance=maximumSquare; Vector3? selected=null;
            List<Vector3> candidates;
            if(!standing.TryGetValue(target,out candidates))
            {
                // Merchant transforms are at sprite centre, while these anchors
                // are on the floor. Personal space is a horizontal distance.
                candidates=minimumDistance>0 ? geometry.ApproachPoints(target,maximumDistance,2) : geometry.StandingNear(target,2);
                Transform owner;
                if(!shelfOwners.TryGetValue(target,out owner)) owner=counter.HasValue && target==counter.Value ? counterOwner : null;
                candidates.RemoveAll(anchor=>!CanSeeTarget(anchor,target,owner));
                standing[target]=candidates;
            }
            foreach(var anchor in candidates)
            {
                if(socialStop && NearTavernBed(anchor)) continue;
                // Tavern targets are open common-room points, not shelf meshes.
                // Stand beside them and look into that space, never at our own feet.
                if(IsTavern && socialStop && (anchor-target).sqrMagnitude<1) continue;
                bool taken=chosen.Contains(anchor) || rejected.Contains(anchor);
                foreach(var previous in chosen) if((previous-anchor).sqrMagnitude<CrowdMotion.Separation*CrowdMotion.Separation) taken=true;
                if(taken) continue;
                var delta=anchor-target;
                float d=minimumDistance>0 ? delta.x*delta.x+delta.z*delta.z : delta.sqrMagnitude;
                if(d>distance || d<minimumSquare || Mathf.Abs(anchor.y-target.y)>2) continue;
                selected=anchor; distance=d;
            }
            if(selected.HasValue && socialStop) browsingFacing[selected.Value]=target;
            return selected;
        }
        private bool CanSeeTarget(Vector3 anchor,Vector3 target,Transform owner)
        {
            Vector3 from=room.transform.TransformPoint(anchor)+Vector3.up*1.2f;
            Vector3 to=room.transform.TransformPoint(target);
            if(owner)
            {
                var collider=owner.GetComponentInChildren<Collider>();
                if(collider)
                {
                    var bounds=collider.bounds;
                    to=bounds.center;
                    to.y=Mathf.Clamp(from.y,bounds.min.y,bounds.max.y);
                }
                else to.y=from.y;
            }
            else to+=Vector3.up*1.2f;
            return InteriorActivitySight.Visible(from,to,owner);
        }
        private bool NearTavernBed(Vector3 anchor)
        {
            if(!IsTavern) return false;
            foreach(var ground in tavernBedGrounds) if((anchor-ground).sqrMagnitude<6.25f) return true;
            return false;
        }
    }
}
