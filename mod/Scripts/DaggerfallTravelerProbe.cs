using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class DaggerfallTravelerProbe
    {
        public static IEnumerator Run(bool gateGuardsOnly=false,bool quartersOnly=false)
        {
            var game=GameManager.Instance;
            if(game.IsPlayerInside) { game.PlayerEnterExit.TransitionExterior(); yield return null; }
            DFLocation city;
            if(!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall","Daggerfall",out city))
                throw new InvalidOperationException("Daggerfall city unavailable.");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            var pixel=MapsFile.LongitudeLatitudeToMapPixel(city.MapTableData.Longitude,city.MapTableData.Latitude);
            game.PlayerEntity.GodMode=true;
            game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y);
            yield return null;
            float deadline=Time.realtimeSinceStartup+90;
            while((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer || !game.PlayerGPS.HasCurrentLocation ||
                game.PlayerGPS.CurrentLocation.MapTableData.MapId!=city.MapTableData.MapId) && Time.realtimeSinceStartup<deadline) yield return null;
            if(!game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId!=city.MapTableData.MapId)
                throw new InvalidOperationException("Daggerfall traveler test arrival timed out.");
            yield return new WaitForSecondsRealtime(2);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var gates=directory.GetComponentsInChildren<DaggerfallCityGate>();
            if(gates.Length==0) throw new InvalidOperationException("Daggerfall traveler test lacks actual city gates.");
            if(quartersOnly) { yield return WallQuarterProbe.Run(directory); yield break; }
            if(gateGuardsOnly)
            {
                yield return GateGuardProbe.Run(UnityEngine.Object.FindObjectOfType<GuardPopulation>());
                yield break;
            }
            var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            var inns=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            int connected=0; TownGeometry.TravelerAccess selected=null; int selectedInn=-1;
            foreach(var inn in inns)
            {
                var routes=geometry.SurveyTravelerRoutes(inn.buildingKey);
                Debug.Log("LIVING_CITIES_CITY_TRAVELER_ROUTE: inn="+inn.buildingKey+"; routes="+routes.Count);
                if(routes.Count>0)
                {
                    connected++; selected=routes[0]; selectedInn=inn.buildingKey;
                    int day=(int)(clock.ToSeconds()/86400);
                    uint random=DFRandom.Seed;
                    var proposal=TravelerIdentityAdapter.Create(42,directory.MapID,inn.buildingKey,day,0,selected);
                    var repeated=TravelerIdentityAdapter.Create(42,directory.MapID,inn.buildingKey,day,0,selected);
                    if(proposal.Person.HomeSettlement==directory.MapID || string.IsNullOrWhiteSpace(proposal.Person.Name) ||
                        proposal.Person.Id!=repeated.Person.Id || proposal.Person.Name!=repeated.Person.Name ||
                        proposal.Visit.DesiredArrival!=repeated.Visit.DesiredArrival || DFRandom.Seed!=random)
                        throw new InvalidOperationException("Traveler identity is local, unstable or changes native random state.");
                    var state=new WorldStateStore(42); state.GetOrCreate(directory.MapID,new int[0]);
                    state.RegisterTraveler(proposal.Person); state.RegisterTravelerVisit(proposal.Visit);
                    var copy=new WorldStateStore(0);
                    copy.Restore((WorldState)DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Deserialize(typeof(WorldState),
                        DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Serialize(typeof(WorldState),state.Snapshot())));
                    if(copy.Snapshot().Travelers[0].Name!=proposal.Person.Name) throw new InvalidOperationException("Generated traveler identity lost on reload.");
                    Debug.Log("LIVING_CITIES_CITY_TRAVELER_IDENTITY_PASS: inn="+inn.buildingKey+"; origin="+proposal.Person.HomeSettlement+
                        "; nights="+proposal.Visit.Nights+"; stable native name and visit serialize.");
                }
                foreach(var route in routes) if(!route.Walled) throw new InvalidOperationException("Daggerfall incorrectly classified as unwalled.");
                yield return null;
            }
            if(selected==null || connected!=inns.Count) throw new InvalidOperationException("Not every Daggerfall inn has a route through city gates.");
            double night=Math.Floor(clock.ToSeconds()/86400d)*86400+20*3600;
            var journey=TravelerJourney.Create(12,night,2,600,true,selected.Points);
            if(journey.EntryAt%86400!=21600 || journey.Evaluate(night).Phase!=TravelerPhase.NotArrived)
                throw new InvalidOperationException("Night traveler crossed closed gate schedule.");
            clock.RaiseTime((float)(night-clock.ToSeconds())); yield return null; yield return null;
            foreach(var inn in inns)
            {
                if(geometry.SurveyTravelerRoutes(inn.buildingKey).Count!=0)
                    throw new InvalidOperationException("Closed Daggerfall gates still permit physical traveler routes to "+inn.buildingKey);
                yield return null;
            }
            clock.RaiseTime((float)((21600-clock.ToSeconds()%86400+86400)%86400)); yield return null; yield return null;
            if(geometry.SurveyTravelerRoutes(selectedInn).Count==0)
                throw new InvalidOperationException("Traveler route did not recover when gates reopened at dawn.");
            Debug.Log("LIVING_CITIES_CITY_TRAVELER_PASS: gates="+gates.Length+"; connectedInns="+connected+"/"+inns.Count+
                "; all inn daytime routes and night rejection, dawn recovery and next-opening schedule verified.");
            // The fixture advances world time through night and dawn. Native timed
            // events can leave a modal window open and pause the game, which prevents
            // LivingCitiesMod from preparing the settlement needed by the actor probe.
            DaggerfallUI.Instance.PopToHUD();
            game.PauseGame(false);
            yield return TravelerActorProbe.Run(directory,geometry,selectedInn,selected);
        }
    }
}
