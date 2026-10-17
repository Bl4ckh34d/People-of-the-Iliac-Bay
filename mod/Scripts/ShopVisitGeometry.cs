using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed class ShopVisitGeometry
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
                FactionFile.FactionData faction;
                if(GameManager.Instance.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) &&
                    faction.sgroup==(int)FactionFile.SocialGroups.Merchants &&
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
                foreach(var anchor in geometry.LocalAnchors)
                {
                    var delta=anchor-counter.Value;
                    if(Mathf.Abs(delta.y)<2 && delta.sqrMagnitude>4 && delta.sqrMagnitude<36 && !NearTavernBed(anchor)) shelves.Add(anchor);
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
        public bool TryFreeBrowsePosition(Vector3 occupied,ResidentActor actor,double now,out Vector3 chosen)
        {
            chosen=occupied;
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
                if(NearTavernBed(candidate) || CrowdSteering.Occupied(new GuardPoint3 {X=point.x,Y=point.y,Z=point.z},neighbours,
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
            int wanted=3+(int)(visitSeed%3);
            for(int i=0;i<wanted;i++)
            {
                // Inspect two sections of a shelf before choosing another shelf.
                var stop=Near(shelves[(int)((visitSeed+(uint)(i/2)+(uint)alternative)%(uint)shelves.Count)],browsing,rejected,true);
                if(stop.HasValue) browsing.Add(stop.Value);
            }
            var keeper=purchase?Near(counter.Value,browsing,rejected,false,
                (float)SocialInteractionSpacing.MinimumMeters,(float)SocialInteractionSpacing.MaximumMeters):(Vector3?)null;
            if(browsing.Count==0 || IsTavern && browsing.Count<2 || (purchase && !keeper.HasValue)) return null;
            var entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
            while(browsing.Count>=(IsTavern?2:1))
            {
                var stops=new List<Vector3>(browsing);
                if(purchase) {if(IsTavern) stops.Insert(0,keeper.Value);else stops.Add(keeper.Value);}
                var routes=new List<List<GuardPoint3>>(); var from=entry;
                foreach(var stop in stops) { routes.Add(geometry.RouteBetween(from,stop)); from=stop; }
                routes.Add(geometry.RouteBetween(from,entry));
                try
                {
                    var journey=ShopVisitJourney.Create(start,end,routes,ResidentMovement.WalkSpeedFor(seed),DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.TimeScale,purchase,visitSeed,IsTavern);
                    return journey;
                }
                catch(ArgumentException error) { candidateFailure=error.Message; browsing.RemoveAt(browsing.Count-1); }
            }
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
