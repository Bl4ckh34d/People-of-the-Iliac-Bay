using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Explicit capacity fixture, distinct from the natural-arrival probes.
        // Use an existing real visitor, its real route and native membership.
        // Only a later visit, legal bed reservations and observer/clock setup
        // are controlled. Production chooses and physically reaches the shelter.
        private IEnumerator ObserveControlledShelter(bool camping)
        {
            string id=camping?"s:1244869923:h:131329:r:0":"s:1242772764:h:131074:r:0";
            var person=worldStore.Traveler(id);var resident=worldStore.ResolveResident(id);
            NaturalNeed(person!=null && resident!=null && person.IsAlive && worldStore.ActiveNpcAction(id)==null &&
                worldStore.ActiveNpcTask(id)==null,"Controlled shelter requires the existing unbound living traveler.");
            float until=Time.realtimeSinceStartup+30;
            while(!EnsureTravelerGuildIdentity(id) && Time.realtimeSinceStartup<until)yield return null;
            NaturalNeed(resident.GuildIdentityKnown && NativeNpcGuildMembership.CanRest(resident,resident.GuildFactionId)!=camping,
                "Existing visitor does not have the required genuine native guild status.");
            var host=GetComponent<PeopleOfTheIliacBayMod>();
            int innCount=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern).Count;
            int completed=-1;until=Time.realtimeSinceStartup+30;
            while(host.HasPendingInnInventory(directory))
            {
                int current=host.CompletedInnInventoryCount(directory);
                if(current>completed)
                {
                    completed=current;until=Time.realtimeSinceStartup+30;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_INVENTORY: completed="+completed+"; total="+innCount+"; noProgressLimitSeconds=30");
                }
                NaturalNeed(Time.realtimeSinceStartup<until,"Native Inn inventory survey made no progress for30seconds.");
                yield return null;
            }
            NaturalNeed(!host.HasPendingInnInventory(directory),"Native Inn inventory survey did not finish; no unknown capacity is replaced.");
            TravelerVisit template=null;double available=ActionNow()+86400;
            foreach(var existing in worldStore.TravelerVisitsFor(directory.MapID))
            {
                if(existing.PersonId!=id)continue;
                if(template==null || existing.DesiredArrival>template.DesiredArrival)template=existing;
                var old=worldStore.TravelerJourneyFor(existing.Id);
                available=Math.Max(available,Math.Max(old.ExitAt,existing.ReturnDepartureNotBefore)+existing.NativeReturnSeconds+86400);
            }
            NaturalNeed(template!=null && template.Diversion==null && template.Route.Count>=2,"Existing native visit route is unavailable.");
            var route=new List<SavedRoutePoint>();foreach(var point in template.Route)route.Add(new SavedRoutePoint{X=point.X,Z=point.Z});
            var entrance=geometry.Entrances[template.TavernKey];var last=route[route.Count-1];
            NaturalNeed(Vector2.Distance(new Vector2((float)last.X,(float)last.Z),new Vector2(entrance.LocalDoor.x,entrance.LocalDoor.z))<=0.55f,
                "Saved visit route does not end at its current native Inn door.");
            double start=Math.Ceiling(available/86400)*86400+12*3600;
            double walk=TravelerPathSeconds(route,DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed));
            string visitId="probe:controlled-shelter:"+(camping?"camp":"guild");
            worldStore.RegisterTravelerVisit(new TravelerVisit{Id=visitId,PersonId=id,SettlementId=directory.MapID,
                TavernKey=template.TavernKey,DesiredArrival=start,Nights=1,Walled=template.Walled,CommuteSeconds=walk,
                NativeReturnSeconds=template.NativeReturnSeconds,Route=route,Purpose=template.Purpose});
            var visit=worldStore.TravelerVisitsFor(directory.MapID).Find(value=>value.Id==visitId);
            var intended=worldStore.TravelerJourneyFor(visitId);
            var snapshot=worldStore.Snapshot();var guests=new List<Tuple<string,int>>();var used=new HashSet<string>();
            foreach(var town in snapshot.Settlements)
                if(town.SettlementId!=directory.MapID)foreach(var home in town.Households)foreach(var guest in home.Residents)
                    if(guest.Id!=id && guest.IsAlive && guest.AgeGroup==ResidentAgeGroup.Adult && !string.IsNullOrEmpty(guest.FullName))
                        guests.Add(Tuple.Create(guest.Id,town.SettlementId));
            int reservations=0,guestIndex=0,checkedInns=0;
            foreach(var inn in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))
            {
                NaturalNeed(worldStore.HasLodgingInventory(directory.MapID,inn.buildingKey),"Actual Inn has no measured beds.");
                checkedInns++;
                while(worldStore.AvailableLodgingBeds(directory.MapID,inn.buildingKey,1,intended.CheckInAt-20,intended.CheckOutAt+1)!=null)
                {
                    bool booked=false;
                    while(guestIndex<guests.Count && !booked)
                    {
                        var guest=guests[guestIndex++];if(!used.Add(guest.Item1))continue;
                        booked=worldStore.TryBookLodging(directory.MapID,inn.buildingKey,"probe:capacity:"+guest.Item1,
                            guest.Item1,guest.Item2,false,1,intended.CheckInAt-20,intended.CheckOutAt+1);
                    }
                    NaturalNeed(booked,"Not enough existing distinct foreign adults to reserve the actual capacity.");reservations++;
                }
            }
            var prepared=new WorldStateStore(snapshot.WorldSeed);prepared.Restore(worldStore.Snapshot());
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_SETUP: person="+id+"; visit="+visitId+"; originalInn="+template.TavernKey+
                "; measuredInns="+checkedInns+"; controlledReservations="+reservations+"; existingPeopleOnly=true; futureVisitControlled=true"+
                "; realNativeRoute=true; membershipInjected=false; nativeCanRest="+!camping+"; preparedRestore=true; npcMovedByProbe=false");
            NaturalSkipUnobserved(intended.CheckInAt-10,id,"controlled-capacity-future-arrival");
            NaturalObserveAt(geometry.ScenePoint(new RoutePoint(last.X,last.Z)));
            until=Time.realtimeSinceStartup+60;
            while(visit.Diversion==null && Time.realtimeSinceStartup<until)yield return null;
            NaturalNeed(visit.Diversion!=null && visit.Diversion.Camping==camping &&
                (camping || worldStore.LodgingGuildFaction(directory.MapID,visit.TavernKey)==resident.GuildFactionId),
                "Ordinary production did not choose the required actual shelter after capacity refusal.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_SELECTED: person="+id+"; camping="+camping+"; destination="+visit.TavernKey+
                "; productionDecision=true; measuredWalkSeconds="+visit.Diversion.WalkSeconds);
            var target=visit.Route[visit.Route.Count-1];var destination=geometry.ScenePoint(new RoutePoint(target.X,target.Z));
            float metres=0;ResidentActor tracked=null;Vector3 previous=Vector3.zero;bool reached=false;
            until=Time.realtimeSinceStartup+(float)(visit.Diversion.WalkSeconds/DaggerfallUnity.Instance.WorldTime.TimeScale)+30;
            while(Time.realtimeSinceStartup<until)
            {
                var actor=FindActor(id);
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    var ground=actor.transform.position-Vector3.up;
                    if(tracked==actor)metres+=NaturalStep(previous,ground);
                    tracked=actor;previous=ground;NaturalObserveAt(ground);
                    if(Vector3.Distance(ground,destination)<=0.6f){reached=true;break;}
                }
                yield return null;
            }
            NaturalNeed(reached && metres>1,"Chosen shelter was not physically reached by its actual visitor.");
            float roomMetres=0;string bed=null;
            if(camping)
            {
                bool outside=false;
                foreach(var terrain in Terrain.activeTerrains)
                {
                    var native=terrain.GetComponent<DaggerfallTerrain>();if(!native || !native.MapData.hasLocation)continue;
                    var local=terrain.transform.InverseTransformPoint(destination);var size=terrain.terrainData.size;
                    if(local.x<0 || local.z<0 || local.x>size.x || local.z>size.z)continue;
                    outside=!native.MapData.locationRect.Contains(new Vector2(local.x/size.x*MapsFile.WorldMapTileDim,local.z/size.z*MapsFile.WorldMapTileDim));
                }
                NaturalNeed(outside && worldStore.TravelerBed(visitId,ActionNow())==null,"Camp arrival is not outside the actual settlement or improperly owns a bed.");
            }
            else
            {
                yield return NaturalEnter(visit.TavernKey);
                until=Time.realtimeSinceStartup+30;ResidentActor body=null;
                while(!(body=FindActor(id)) && Time.realtimeSinceStartup<until)yield return null;
                bed=worldStore.TravelerBed(visitId,ActionNow());
                var anchor=BedGeometry.Read(interior).Find(value=>"bed:"+value.RecordIndex==bed);Vector3 standing=Vector3.zero;
                NaturalNeed(body && anchor!=null && indoorGeometry.StandBesideBed(anchor,out standing),"Actual authorized hall guest/bed floor is missing.");
                NaturalObserveAt(standing);previous=body.transform.position-Vector3.up;until=Time.realtimeSinceStartup+60;
                while(Vector3.Distance(body.transform.position-Vector3.up,standing)>0.55f && Time.realtimeSinceStartup<until)
                {
                    yield return null;NaturalNeed(body && FindActor(id)==body,"Observed guild visitor vanished before its actual bed.");
                    var point=body.transform.position-Vector3.up;roomMetres+=NaturalStep(previous,point);previous=point;
                }
                NaturalNeed(Vector3.Distance(body.transform.position-Vector3.up,standing)<=0.55f && roomMetres>1,
                    "Guild guest did not physically reach its assigned native bed.");
            }
            var saved=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),host.GetSaveData()));
            var restored=new WorldStateStore(saved.WorldSeed);restored.Restore(saved);
            var retained=restored.TravelerVisitsFor(directory.MapID).Find(value=>value.Id==visitId);
            NaturalNeed(retained!=null && retained.Diversion!=null && retained.Diversion.Camping==camping &&
                restored.TravelerBed(visitId,ActionNow())==bed,"Native serialized shelter identity/bed changed.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTROLLED_SHELTER_ARRIVAL_PASS: person="+id+"; camping="+camping+
                "; exteriorMetres="+metres+"; roomMetres="+roomMetres+"; bed="+bed+"; actualArrival=true; nativeSerializer=true"+
                "; reservationsControlled=true; visitControlled=true; membershipInjected=false; npcMovedByProbe=false");
        }
    }
}
