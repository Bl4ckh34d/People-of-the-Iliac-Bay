using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Owned fixture only. Ordinary housing and guard surveys perform all setup.
    // Teleporting the observer never inserts a household, post, route or guard.
    internal static class GuardCivilianLocationProbe
    {
        internal static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Civilian guard coverage requires an owned outdoor fixture.");
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var guards=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            if(!host || !guards)throw new InvalidOperationException("Civilian guard fixture lacks production controllers.");
            var reader=DaggerfallUnity.Instance.ContentReader;
            var region=reader.MapFileReader.GetRegion(game.PlayerGPS.CurrentRegionIndex);
            var targets=new List<DFLocation>();
            foreach(int category in new[]{3,5,6,4,11})
            {
                bool found=false;
                for(int i=0;i<region.LocationCount;i++)
                {
                    if((int)region.MapTable[i].LocationType!=category)continue;
                    DFLocation location;
                    if(!reader.GetLocation(region.Name,region.MapNames[i],out location) || !location.Loaded ||
                        location.Exterior.ExteriorData.Width<1 || location.Exterior.ExteriorData.Height<1)continue;
                    targets.Add(location);found=true;break;
                }
                if(!found)throw new InvalidOperationException("Fixture region lacks native location category "+category);
            }
            game.PlayerEntity.GodMode=true;
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            foreach(var location in targets)
            {
                var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
                game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y,StreamingWorld.RepositionMethods.RandomStartMarker);
                float deadline=Time.realtimeSinceStartup+90;
                BuildingDirectory directory=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                    if(game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer && directory &&
                        directory.MapID==location.MapTableData.MapId &&
                        directory.GetComponentsInChildren<DaggerfallRMBBlock>().Length==
                            location.Exterior.ExteriorData.Width*location.Exterior.ExteriorData.Height)break;
                    yield return null;
                }
                if(!directory || directory.MapID!=location.MapTableData.MapId)
                    throw new InvalidOperationException("Native civilian location did not load: "+location.Name);
                yield return null;yield return null;
                int category=(int)location.MapTableData.LocationType;
                if(!SettlementClassification.SupportsGuards(category))
                {
                    deadline=Time.realtimeSinceStartup+15;
                    while(guards.Roster!=null && guards.Roster.SettlementId==directory.MapID && Time.realtimeSinceStartup<deadline)yield return null;
                    var store=Field<WorldStateStore>(host,"store");
                    if(store.GetGuards(directory.MapID)!=null || guards.Roster!=null && guards.Roster.SettlementId==directory.MapID)
                        throw new InvalidOperationException("Excluded native location gained municipal guard ownership: type="+category);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_LOCATION_EXCLUDED_PASS: type="+category+"; location="+location.Name+"; map="+directory.MapID);
                    continue;
                }
                deadline=Time.realtimeSinceStartup+120;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(guards.Roster!=null && guards.Roster.SettlementId==directory.MapID &&
                        Field<List<PatrolPairPlan>>(guards,"patrolTemplates").Count>=2)break;
                    yield return null;
                }
                var roster=guards.Roster;
                if(roster==null || roster.SettlementId!=directory.MapID || roster.Guards.Count<6)
                    throw new InvalidOperationException("Civilian location lacks its six-guard minimum: "+location.Name);
                var population=Field<SettlementPopulation>(host,"current");
                if(population==null || population.SettlementId!=directory.MapID)
                    throw new InvalidOperationException("Civilian guard census belongs to another location.");
                var homes=HousingSurvey.Read(directory);var keys=new HashSet<int>();
                foreach(var home in homes)keys.Add(home.BuildingKey);
                foreach(var home in population.Households)
                    if(!keys.Contains(home.BuildingKey))throw new InvalidOperationException("Guard setup invented a household outside the native home survey.");
                int residents=0;foreach(var home in population.Households)residents+=home.Residents.Count;
                int expected=GuardRoster.TargetWithGateSlots(GuardRoster.TargetCount(SettlementClassification.GuardScale(category),residents),0);
                if(roster.Target!=expected)throw new InvalidOperationException("Civilian guard quota differs from actual census.");
                if(roster.Guards.FindAll(g=>g.Shift==GuardShift.Day).Count<3 || roster.Guards.FindAll(g=>g.Shift==GuardShift.Night).Count<3)
                    throw new InvalidOperationException("Civilian guard shift lacks a pair and fixed post.");
                var geometry=Field<TownGeometry>(guards,"geometry");
                var templates=Field<List<PatrolPairPlan>>(guards,"patrolTemplates");
                foreach(var shift in new[]{GuardShift.Day,GuardShift.Night})
                {
                    var template=templates.Find(p=>roster.Guards.Exists(g=>g.Id==p.LeaderId && g.Shift==shift));
                    if(template==null || template.Circuit.Count<4 ||
                        geometry.RouteBetweenPoints(template.Circuit[0],template.Circuit[1]).Count<2)
                        throw new InvalidOperationException("Civilian location lacks a physically surveyed patrol circuit for "+shift+": "+location.Name);
                }
                foreach(var guard in roster.Guards)
                    if(geometry.RouteBetweenPoints(guard.Post,guard.Post).Count<2)
                        throw new InvalidOperationException("Civilian guard post is not reachable on actual native geometry.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_LOCATION_PASS: type="+category+"; location="+location.Name+
                    "; map="+directory.MapID+"; homes="+population.Households.Count+"; residents="+residents+
                    "; guards="+roster.Guards.Count+"; patrolTemplates="+templates.Count+"; inventedHouseholds=false");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_CIVILIAN_LOCATIONS_PASS: farm=true; temple=true; tavern=true; dungeonExcluded=true; poorHomeExcluded=true");
        }
        private static T Field<T>(object instance,string name)
        {return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);}
    }
}
