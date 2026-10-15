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
        public static IEnumerator Run()
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
            var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            var inns=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            int connected=0; TownGeometry.TravelerAccess selected=null; int selectedInn=-1;
            foreach(var inn in inns)
            {
                var routes=geometry.SurveyTravelerRoutes(inn.buildingKey);
                Debug.Log("LIVING_CITIES_CITY_TRAVELER_ROUTE: inn="+inn.buildingKey+"; routes="+routes.Count);
                if(routes.Count>0) { connected++; selected=routes[0]; selectedInn=inn.buildingKey; }
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
        }
    }
}
