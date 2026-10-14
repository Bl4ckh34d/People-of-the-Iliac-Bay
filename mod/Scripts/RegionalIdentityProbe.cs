using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Runs only in the disposable smoke-test session; does not save regional fixtures.
    public static class RegionalIdentityProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            var reader=DaggerfallUnity.Instance.ContentReader;
            foreach(var bank in new[] { NameHelper.BankTypes.Redguard,NameHelper.BankTypes.Nord })
            {
                DFLocation location=default(DFLocation); bool found=false;
                for(int r=0;r<MapsFile.RegionRaces.Length && !found;r++)
                {
                    var region=reader.MapFileReader.GetRegion(r);
                    for(int i=0;i<region.LocationCount && !found;i++)
                        if(region.MapTable[i].LocationType==DFRegion.LocationTypes.TownCity)
                        {
                            found=reader.GetLocation(region.Name,region.MapNames[i],out location) &&
                                location.Climate.People==(bank==NameHelper.BankTypes.Redguard ? FactionFile.FactionRaces.Redguard : FactionFile.FactionRaces.Nord);
                        }
                }
                if(!found) throw new InvalidOperationException("No regional test city for "+bank);
                var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
                game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y);
                yield return null;
                float deadline=Time.realtimeSinceStartup+60;
                while((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                    !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId!=location.MapTableData.MapId)
                    && Time.realtimeSinceStartup<deadline) yield return null;
                if(!game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId!=location.MapTableData.MapId)
                    throw new InvalidOperationException("Regional arrival timed out: "+bank);
                yield return new WaitForSecondsRealtime(2);
                DaggerfallUI.Instance.PopToHUD();
                var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
                SettlementPopulation town=null;
                deadline=Time.realtimeSinceStartup+20;
                do
                {
                    town=((WorldState)mod.GetSaveData()).Settlements.Find(s=>s.SettlementId==location.MapTableData.MapId);
                    if(town!=null) break;
                    yield return null;
                } while(Time.realtimeSinceStartup<deadline);
                if(town==null || town.Households.Count==0) throw new InvalidOperationException("Regional population absent: "+mod.TestStatus());
                var people=game.PlayerGPS.ClimateSettings.People;
                var race=people==FactionFile.FactionRaces.Redguard ? Races.Redguard : people==FactionFile.FactionRaces.Nord ? Races.Nord : Races.Breton;
                int checkedPeople=0;
                foreach(var home in town.Households)
                {
                    foreach(var resident in home.Residents)
                    {
                        if(resident.Race!=(int)race || string.IsNullOrWhiteSpace(resident.FullName) || resident.FullName.Contains("LocaleText-NotFound"))
                            throw new InvalidOperationException("Invalid live regional identity: "+resident.FullName);
                        checkedPeople++;
                    }
                }
                if(checkedPeople==0) throw new InvalidOperationException("Empty regional population.");
                var streets=mod.GetComponent<StreetPopulation>(); var guards=mod.GetComponent<GuardPopulation>();
                deadline=Time.realtimeSinceStartup+60;
                while((!streets.Ready || guards.Roster==null || guards.Roster.SettlementId!=town.SettlementId) && Time.realtimeSinceStartup<deadline)
                    yield return null;
                if(!streets.Ready || guards.Roster==null || guards.Roster.SettlementId!=town.SettlementId)
                    throw new InvalidOperationException("Regional routines/guards not ready: "+mod.TestStatus());
                int actorsChecked=0;
                foreach(var home in town.Households)
                {
                    foreach(var resident in home.Residents)
                    {
                        if(resident.AgeGroup==ResidentAgeGroup.Child) continue;
                        var go=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject);
                        try
                        {
                            var actor=go.AddComponent<ResidentActor>();
                            actor.Initialize(resident,home.SurnameSeed,game.StreamingWorld.GetCurrentCityNavigation());
                            if(actor.Person.Race!=race || actor.Person.NameNPC!=resident.FullName ||
                                actor.Person.Gender!=(resident.Sex==ResidentSex.Male ? Genders.Male : Genders.Female) || !actor.Person.Asset)
                                throw new InvalidOperationException("Regional actor identity mismatch.");
                            actorsChecked++;
                        }
                        finally { UnityEngine.Object.Destroy(go); }
                        if(actorsChecked>=2) break;
                    }
                    if(actorsChecked>=2) break;
                }
                if(actorsChecked<2 || streets.PlannedCount==0) throw new InvalidOperationException("Regional actor/schedule sample missing.");
                Debug.Log("LIVING_CITIES_REGIONAL_ACTORS_PASS: "+location.Name+"; plans="+streets.PlannedCount+"; guards="+guards.Roster.Target+"; actors="+actorsChecked);
                Debug.Log("LIVING_CITIES_REGIONAL_LIVE_PASS: bank="+bank+"; location="+location.Name+"; race="+race+"; residents="+checkedPeople+"; example="+town.Households[0].Residents[0].FullName);
            }
        }
    }
}
