using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Disposable test-session survey. Does not alter saves or enable city actors.
    public static class DaggerfallRefugeProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            DFLocation city;
            if (!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Daggerfall", out city))
                throw new InvalidOperationException("Daggerfall city missing.");
            DFPosition pixel = MapsFile.LongitudeLatitudeToMapPixel(city.MapTableData.Longitude, city.MapTableData.Latitude);
            game.PlayerEntity.GodMode = true;
            game.StreamingWorld.TeleportToCoordinates(pixel.X, pixel.Y);
            yield return null;
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != city.MapTableData.MapId) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != city.MapTableData.MapId)
                throw new InvalidOperationException("Daggerfall survey arrival timed out.");
            yield return new WaitForSecondsRealtime(2);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation());
            int candidates = 0, connected = 0;
            var representatives = new SortedDictionary<uint, int>();
            foreach (var entrance in geometry.Entrances.Values)
            {
                BuildingSummary building;
                if (!directory.GetBuildingSummary(entrance.BuildingKey, out building) ||
                    building.BuildingType != DFLocation.BuildingTypes.Town23) continue;
                candidates++;
                bool reached = false;
                foreach (var other in geometry.Entrances.Values)
                {
                    if (other.BuildingKey == entrance.BuildingKey ||
                        Vector3.Distance(other.LocalDoor, entrance.LocalDoor) > 100) continue;
                    var route = geometry.RouteBetween(other, entrance);
                    if (route.Count < 2) continue;
                    connected++; reached = true;
                    if (!representatives.ContainsKey(building.ModelID)) representatives.Add(building.ModelID, entrance.BuildingKey);
                    Debug.Log("LIVING_CITIES_REFUGE_ROUTE: key=" + entrance.BuildingKey + "; model=" + building.ModelID +
                        "; from=" + other.BuildingKey + "; points=" + route.Count + "; door=" + entrance.LocalDoor);
                    break;
                }
                if (!reached) Debug.Log("LIVING_CITIES_REFUGE_UNREACHED: key=" + entrance.BuildingKey + "; model=" + building.ModelID);
                yield return null;
            }
            Debug.Log("LIVING_CITIES_REFUGE_SURVEY: groundEntrances=" + candidates + "; connected=" + connected +
                "; rejectedDoors=" + geometry.RejectedDoors);
            if (connected == 0) throw new InvalidOperationException("No reachable Daggerfall wall entrance found.");
            foreach (var representative in representatives)
            {
                DaggerfallStaticDoors owner = null;
                StaticDoor selected = default(StaticDoor);
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                {
                    if (owner || candidate.Doors == null) continue;
                    foreach (var door in candidate.Doors)
                        if (door.doorType == DoorTypes.Building && door.buildingKey == representative.Value)
                        { owner = candidate; selected = door; break; }
                }
                if (!owner) throw new InvalidOperationException("Surveyed wall entrance disappeared.");
                TownSmokeHarness.EnterInterior(owner.transform, selected);
                yield return new WaitForSecondsRealtime(1);
                var interior = game.PlayerEnterExit.Interior;
                if (!interior || !game.PlayerEnterExit.IsPlayerInsideBuilding)
                    throw new InvalidOperationException("Wall interior entry failed.");
                var inside = new InteriorGeometry(interior);
                int reachable = 0;
                foreach (var anchor in inside.LocalAnchors)
                    if (inside.RouteToExit(interior.transform.TransformPoint(anchor)).Count >= 2) reachable++;
                Debug.Log("LIVING_CITIES_REFUGE_INTERIOR: model=" + representative.Key + "; key=" + representative.Value +
                    "; anchors=" + inside.LocalAnchors.Count + "; reachable=" + reachable);
                if (!inside.EntryGround.HasValue || reachable == 0)
                    throw new InvalidOperationException("Wall interior has no checked refuge floor: " + representative.Key);
                var shelter=interior.transform.TransformPoint(inside.LocalAnchors[inside.LocalAnchors.Count-1]);
                var indoorRoute=inside.RouteToExit(shelter); indoorRoute.Reverse();
                var roster=GuardRoster.Create(city.MapTableData.MapId,42,1,new[] { new SavedRoutePoint() });
                string guardId=roster.Guards[0].Id;
                roster.TakeRefuge(guardId,GuardRefugeJourney.Create(representative.Value,
                    new[] { new GuardPoint3(),new GuardPoint3 { X=10 } },indoorRoute,100,2,12),100);
                var actor=GuardActor.CreateInterior(roster,guardId,interior.transform,100000);
                yield return null;
                actor.Place(100000);
                var difference=actor.transform.position-shelter;
                if (!actor.IsInterior || new Vector2(difference.x,difference.z).magnitude>0.05f || difference.y<0.4f || difference.y>2)
                    throw new InvalidOperationException("Refuge guard not standing at its saved interior destination.");
                roster.Guards[0].Refuge.Release(100000);
                var beforeExit=actor.transform.position;
                actor.Place(100001);
                if ((actor.transform.position-beforeExit).sqrMagnitude<0.0001f)
                    throw new InvalidOperationException("Released refuge actor did not move.");
                UnityEngine.Object.Destroy(actor.gameObject);
                var controllerObject=new GameObject("Living Cities refuge controller probe");
                var controller=controllerObject.AddComponent<GuardPopulation>(); controller.enabled=false;
                try
                {
                    var type=typeof(GuardPopulation);
                    type.GetProperty("Roster").GetSetMethod(true).Invoke(controller,new object[] { roster });
                    type.GetField("interior",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,interior);
                    var update=type.GetMethod("UpdateInterior",BindingFlags.Instance|BindingFlags.NonPublic);
                    update.Invoke(controller,new object[] { 100001d });
                    if(controller.ActiveCount!=1) throw new InvalidOperationException("Controller must show the released refuge guard in its interior.");
                    update.Invoke(controller,new object[] { 200000d });
                    if(controller.ActiveCount!=0) throw new InvalidOperationException("Controller retained a refuge guard after it left the interior.");
                }
                finally { UnityEngine.Object.Destroy(controllerObject); }
                Debug.Log("LIVING_CITIES_REFUGE_ACTOR_PASS: model="+representative.Key+"; saved shelter placement and exit movement.");
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1);
            }
            Debug.Log("LIVING_CITIES_TOWN_TEST_PASS: Daggerfall wall exterior routes and representative interior floor routes; models=" + representatives.Count);
        }
    }
}
