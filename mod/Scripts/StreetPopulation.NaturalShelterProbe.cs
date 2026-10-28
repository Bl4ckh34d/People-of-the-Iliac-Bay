using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Diagnostic only: initialize genuine origin membership with the same
        // production rule, then observe normal lodging decisions. Never fill
        // beds, replace visits, grant membership or move an NPC for this census.
        private IEnumerator ObserveNaturalShelterCensus()
        {
            string advance=Environment.GetEnvironmentVariable("POTIB_SHELTER_CENSUS_HOURS");int hours;
            if(!string.IsNullOrEmpty(advance))
            {
                NaturalNeed(int.TryParse(advance,out hours) && hours>0 && hours<=96,"Shelter census time advance must be1..96hours.");
                double before=ActionNow();DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(hours*3600);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_SHELTER_CLOCK: from="+before+"; until="+ActionNow()+
                    "; hours="+hours+"; skippedIntervalObserved=false; physicalArrivalClaimed=false");
            }
            // Let ordinary demand/inventory processing establish the current
            // candidates, including after an explicitly controlled clock jump.
            float prepare=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<prepare)yield return null;
            var candidates=new List<TravelerVisit>();
            string requested=Environment.GetEnvironmentVariable("POTIB_SHELTER_CENSUS_PEOPLE");
            var selected=string.IsNullOrEmpty(requested)?null:new HashSet<string>(requested.Split(';'));
            foreach(var visit in worldStore.TravelerVisitsFor(directory.MapID))
            {
                if(selected!=null ? !selected.Contains(visit.PersonId) :
                    !worldStore.TravelerAwaitingLodgingDecision(visit.Id,ActionNow()) &&
                    !(visit.Diversion!=null && worldStore.TravelerJourneyFor(visit.Id).CheckOutAt>ActionNow()))continue;
                candidates.Add(visit);
                EnsureTravelerGuildIdentity(visit.PersonId);
            }
            float until=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<until)yield return null;
            int members=0,pending=0,camps=0,guilds=0,inns=0,refused=0;
            foreach(var visit in candidates)
            {
                var person=worldStore.ResolveResident(visit.PersonId);
                bool canRest=person!=null && NativeNpcGuildMembership.CanRest(person,person.GuildFactionId);
                if(person==null || !person.GuildIdentityKnown)pending++;
                if(canRest)members++;
                // A refused journey has an early departure. Availability must
                // cover the original intended stay, as the production selector does.
                var original=visit.Diversion==null?visit.Route:visit.Diversion.OriginalRoute;
                var points=new List<RoutePoint>();foreach(var point in original)points.Add(new RoutePoint(point.X,point.Z));
                var journey=TravelerJourney.Create(worldStore.Traveler(visit.PersonId).AppearanceSeed,visit.DesiredArrival,visit.Nights,
                    visit.Diversion==null?visit.CommuteSeconds:visit.Diversion.OriginalCommuteSeconds,visit.Walled,points);
                int offers=0,halls=0;
                foreach(var door in geometry.Entrances.Values)
                {
                    BuildingSummary building;
                    if(!directory.GetBuildingSummary(door.BuildingKey,out building))continue;
                    if(canRest && NativeNpcGuildMembership.RestFaction(building)==person.GuildFactionId)halls++;
                    if(building.BuildingType==DFLocation.BuildingTypes.Tavern &&
                        worldStore.HasLodgingInventory(directory.MapID,door.BuildingKey) &&
                        journey.CheckOutAt>ActionNow() &&
                        worldStore.AvailableLodgingBeds(directory.MapID,door.BuildingKey,1,ActionNow(),journey.CheckOutAt)!=null)offers++;
                }
                bool camp=visit.Diversion!=null && visit.Diversion.Camping;
                bool guild=visit.Diversion!=null && !camp && worldStore.LodgingGuildFaction(directory.MapID,visit.TavernKey)>0;
                if(camp)camps++;else if(guild)guilds++;else if(visit.Diversion!=null)inns++;else refused++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_SHELTER_CANDIDATE: person="+visit.PersonId+"; visit="+visit.Id+
                    "; alive="+(person!=null && person.IsAlive)+"; known="+(person!=null && person.GuildIdentityKnown)+
                    "; faction="+(person==null?0:person.GuildFactionId)+"; rank="+(person==null?-1:person.GuildRank)+
                    "; homeHall="+(person==null?-1:person.GuildHomeKey)+"; nativeCanRest="+canRest+"; matchingHalls="+halls+
                    "; innOffersBeforeReachability="+offers+"; camp="+camp+"; guild="+guild+"; admission="+visit.Admission+
                    "; diversion="+(visit.Diversion!=null)+"; checkin="+journey.CheckInAt+"; checkout="+journey.CheckOutAt);
            }
            var host=GetComponent<PeopleOfTheIliacBayMod>();
            if(Environment.GetEnvironmentVariable("POTIB_SHELTER_REQUIRE_VISITOR_REPORT")=="1")
            {
                TravelerPerson traveler;var corpse=NaturalForeignCorpseReport(out traveler);
                float reportUntil=Time.realtimeSinceStartup+30;
                while(corpse==null && Time.realtimeSinceStartup<reportUntil)
                {yield return null;corpse=NaturalForeignCorpseReport(out traveler);}
                NaturalNeed(traveler!=null && traveler.HomeSettlement!=directory.MapID && corpse!=null,
                    "Actual foreign visitor has not reported the naturally encountered aggressor corpse.");
                string witness=traveler.Id;
                var saved=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),host.GetSaveData()));
                var restored=new WorldStateStore(saved.WorldSeed);restored.Restore(saved);
                var retained=restored.GetDefenseCombatant(corpse.Id);
                NaturalNeed(retained!=null && retained.Death.Cleanup!=null &&
                    retained.Death.Cleanup.Reports.Exists(report=>report.ObserverId==witness),
                    "Actual visitor corpse report was lost during native serialization and validated restore.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_CORPSE_REPORT_PASS: person="+witness+"; home="+traveler.HomeSettlement+
                    "; observedSettlement="+directory.MapID+"; corpse="+corpse.Id+"; nativeCorpse=true; actualVisibleDiscovery=true"+
                    "; foreignVisitor=true; nativeSerializer=true; validatedRestore=true; probeWitnessInjection=false; probeCombatInjection=false");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_SHELTER_CENSUS_DONE: map="+directory.MapID+"; candidates="+candidates.Count+
                "; nativeMembers="+members+"; identitiesPending="+pending+"; actualCampPlans="+camps+
                "; actualGuildPlans="+guilds+"; actualAlternateInns="+inns+"; unresolved="+refused+
                "; innInventoryPending="+(host && host.HasPendingInnInventory(directory))+
                "; physicalArrivalClaimed=false; probeBookings=false; probeMembershipGrant=false");
        }

        private DefenseCombatant NaturalForeignCorpseReport(out TravelerPerson traveler)
        {
            foreach(var corpse in worldStore.DefenseCorpsesFor(directory.MapID))
            {
                if(corpse.Death.Cleanup==null)continue;
                foreach(var report in corpse.Death.Cleanup.Reports)
                {
                    var visitor=worldStore.Traveler(report.ObserverId);
                    if(visitor==null || visitor.HomeSettlement==directory.MapID)continue;
                    traveler=visitor;return corpse;
                }
            }
            traveler=null;return null;
        }
    }
}
