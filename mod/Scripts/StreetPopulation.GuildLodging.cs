using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly HashSet<string> guildIdentityPending=new HashSet<string>();
        private readonly HashSet<int> guildLodgingSurveyed=new HashSet<int>();
        private readonly HashSet<int> guildLodgingPending=new HashSet<int>();
        private BuildingDirectory guildLodgingDirectory;
        private WorldStateStore guildLodgingStore;
        private object guildLodgingEpoch;
        private void CheckGuildLodgingOwner()
        {
            if(guildLodgingDirectory==directory && ReferenceEquals(guildLodgingStore,worldStore) && worldStore.IsNpcVitalsWriteEpochCurrent(guildLodgingEpoch))return;
            guildLodgingDirectory=directory;guildLodgingStore=worldStore;guildLodgingEpoch=worldStore.CaptureNpcVitalsWriteEpoch();
            guildIdentityPending.Clear();guildLodgingSurveyed.Clear();guildLodgingPending.Clear();
        }
        private bool EnsureTravelerGuildIdentity(string personId)
        {
            CheckGuildLodgingOwner();var person=worldStore.ResolveResident(personId);
            if(person==null || person.GuildIdentityKnown || person.AgeGroup!=ResidentAgeGroup.Adult)return true;
            if(guildIdentityPending.Contains(personId))return false;
            int origin=-1;var traveler=worldStore.Traveler(personId);
            if(traveler!=null)origin=traveler.HomeSettlement;
            else {var trip=worldStore.GetResidentDialogueTrip(personId);if(trip!=null)origin=trip.HomeSettlement;}
            if(origin<0)return false;
            var reader=DaggerfallUnity.Instance.ContentReader;var pixel=MapsFile.GetPixelFromPixelID(origin & 0x000fffff);
            DaggerfallWorkshop.Utility.ContentReader.MapSummary summary;DFLocation source;
            if(!reader.HasLocation(pixel.X,pixel.Y,out summary) || summary.MapID!=origin || !reader.GetLocation(summary.RegionIndex,summary.MapIndex,out source))return false;
            guildIdentityPending.Add(personId);StartCoroutine(PrepareTravelerGuildIdentity(person,source,worldStore,guildLodgingEpoch));return person.GuildIdentityKnown;
        }
        private IEnumerator PrepareTravelerGuildIdentity(Resident person,DFLocation origin,WorldStateStore owner,object epoch)
        {
            Func<bool> valid=()=>this && ReferenceEquals(worldStore,owner) && owner.IsNpcVitalsWriteEpochCurrent(epoch);
            try{yield return NativeNpcGuildMembership.InitializeSteps(owner,person,origin,valid);}
            finally{if(valid())guildIdentityPending.Remove(person.Id);}
        }
        private bool TryGuildLodgingFallback(string personId,SavedRoutePoint start,double secondsPerMetre,double now,
            Func<int,bool,List<SavedRoutePoint>,double,bool> accept,out bool pending)
        {
            pending=!EnsureTravelerGuildIdentity(personId);if(pending)return false;
            var person=worldStore.ResolveResident(personId);
            if(person==null || !NativeNpcGuildMembership.CanRest(person,person.GuildFactionId))return false;
            var halls=new List<TownGeometry.Entrance>();
            foreach(var door in geometry.Entrances.Values)
            {
                BuildingSummary building;
                if(directory.GetBuildingSummary(door.BuildingKey,out building) && NativeNpcGuildMembership.RestFaction(building)==person.GuildFactionId)halls.Add(door);
            }
            halls.Sort((a,b)=>{double ax=a.LocalDoor.x-start.X,az=a.LocalDoor.z-start.Z,bx=b.LocalDoor.x-start.X,bz=b.LocalDoor.z-start.Z;
                int order=(ax*ax+az*az).CompareTo(bx*bx+bz*bz);return order!=0?order:a.BuildingKey.CompareTo(b.BuildingKey);});
            foreach(var door in halls)
            {
                var route=geometry.RouteBetweenPoints(start,new SavedRoutePoint {X=door.LocalDoor.x,Z=door.LocalDoor.z});
                double walk=TravelerPathSeconds(route,secondsPerMetre);if(walk<=0 || !GuildOpenAt(person,now+walk))continue;
                if(!guildLodgingSurveyed.Contains(door.BuildingKey))
                {
                    pending=true;
                    if(!guildLodgingPending.Contains(door.BuildingKey))
                    {guildLodgingPending.Add(door.BuildingKey);StartCoroutine(PrepareGuildLodging(door.BuildingKey,person.GuildFactionId,worldStore,directory,guildLodgingEpoch));}
                    continue;
                }
                if(worldStore.LodgingGuildFaction(population.SettlementId,door.BuildingKey)!=person.GuildFactionId)continue;
                if(accept(door.BuildingKey,false,route,walk))
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_GUILD_LODGING: person="+personId+"; faction="+person.GuildFactionId+
                    "; rank="+person.GuildRank+"; homeHall="+person.GuildHomeKey+"; hall="+door.BuildingKey+"; nativeCanRest=true; measuredWalkSeconds="+walk);return true;}
            }
            return false;
        }
        private static bool GuildOpenAt(Resident person,double now)
        {
            if(NativeNpcGuildMembership.AccessAnytime(person))return true;
            int type=(int)DFLocation.BuildingTypes.GuildHall;DailyServiceWindow hours;
            return DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out hours) && TravelerInnOpen(now,hours);
        }
        private IEnumerator PrepareGuildLodging(int building,int faction,WorldStateStore owner,BuildingDirectory source,object epoch)
        {
            Func<bool> valid=()=>this && directory==source && ReferenceEquals(worldStore,owner) && owner.IsNpcVitalsWriteEpochCurrent(epoch);
            try
            {
                DaggerfallStaticDoors doors=null;StaticDoor entry=default(StaticDoor);
                foreach(var candidate in source.GetComponentsInChildren<DaggerfallStaticDoors>())foreach(var door in candidate.Doors??new StaticDoor[0])
                    if(door.buildingKey==building && door.doorType==DoorTypes.Building){doors=candidate;entry=door;break;}
                if(!doors)yield break;
                var result=new TavernBedSurvey.Result();
                yield return TavernBedSurvey.ReadServiceSteps(doors.transform,entry,source.LocationData.Climate.ClimateType,result,valid,false,true);
                if(!valid() || result.Beds==null || result.Occupants==null)yield break;
                owner.RegisterGuildLodgingBeds(source.MapID,building,faction,result.Beds);
                NativeInnOccupants.Claim(owner,source,entry,result.Beds,result.Occupants,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUILD_LODGING_INVENTORY: map="+source.MapID+"; hall="+building+"; faction="+faction+
                    "; actualBeds="+result.Beds.Count+"; nativeOccupants="+result.Occupants.Count+"; nativeMarkersUnchanged=true");
            }
            finally{if(valid()){guildLodgingPending.Remove(building);guildLodgingSurveyed.Add(building);}}
        }
    }
}
