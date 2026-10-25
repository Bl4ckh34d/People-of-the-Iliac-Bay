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
    internal static class OccupiedApproachProbe
    {
        private static double Now() {var n=DaggerfallUnity.Instance.WorldTime.Now;return (double)n.ToSeconds()+n.Second-Math.Floor(n.Second);}
        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(character,"People - Shop entrance");
            if(slot<0) throw new InvalidOperationException("Occupied approach probe lacks shop fixture.");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler handler=data=>loaded=true;
            SaveLoadManager.OnLoad+=handler;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();float deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Shop fixture load timed out.");
            }
            finally {SaveLoadManager.OnLoad-=handler;}
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>()) foreach(var door in doors.Doors)
            {
                BuildingSummary summary;
                if(door.doorType==DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey,out summary) &&
                    summary.BuildingType==DFLocation.BuildingTypes.GeneralStore) {owner=doors;entry=door;break;}
            }
            if(!owner) throw new InvalidOperationException("Fixture has no general store.");
            TownSmokeHarness.EnterInterior(owner.transform,entry);yield return new WaitForSecondsRealtime(1);
            DaggerfallUI.Instance.PopToHUD();var room=game.PlayerEnterExit.Interior;
            if(!room) throw new InvalidOperationException("Shop entry failed.");
            bool enabled=host.enabled,streetsEnabled=streets.enabled;host.enabled=false;streets.enabled=false;
            var hidden=new List<GameObject>();foreach(var existing in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(existing.gameObject.activeSelf) {hidden.Add(existing.gameObject);existing.gameObject.SetActive(false);}
            GameObject first=null,second=null;
            try
            {
                var geometry=new InteriorGeometry(room);var activities=new ShopVisitGeometry(room,geometry);
                ShopVisitJourney journey=null;
                for(uint seed=0;seed<30 && journey==null;seed++) journey=activities.Create("occupied-probe",seed,Now(),Now()+1200);
                if(journey==null || journey.StopPhase(0)!=ShopVisitPhase.Browsing) throw new InvalidOperationException("Shop lacks a browsing route.");
                var firstRoute=journey.LegRoute(0);var end=firstRoute[firstRoute.Count-1];
                var destination=room.transform.TransformPoint(new Vector3((float)end.X,(float)end.Y,(float)end.Z));
                first=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
                second=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
                var actor=first.AddComponent<ResidentActor>();var blocker=second.AddComponent<ResidentActor>();
                actor.Initialize(new Resident {Id="occupied-probe",FullName="Approaching Patron",AppearanceSeed=42},1,game.StreamingWorld.GetCurrentCityNavigation());
                blocker.Initialize(new Resident {Id="occupied-blocker",FullName="Browsing Patron",AppearanceSeed=99},1,game.StreamingWorld.GetCurrentCityNavigation());
                blocker.Place(destination,Vector3.forward,true);actor.Place(geometry.EntryGround.Value,Vector3.forward);
                var physical=new PhysicalShopVisit(journey);float limit=Time.realtimeSinceStartup+45;
                while(physical.Snapshot.Phase!=ShopVisitPhase.Browsing && Time.realtimeSinceStartup<limit)
                {physical.Advance(actor,room,geometry,activities,Now());yield return null;}
                if(physical.Snapshot.Phase!=ShopVisitPhase.Browsing || Vector3.Distance(first.transform.position,second.transform.position)<CrowdMotion.Separation)
                    throw new InvalidOperationException("Patron did not reach a separate nearby browsing position: "+physical.LastFailure);
                var saved=physical.Capture(actor,room,geometry,Now(),game.PlayerGPS.CurrentLocation.MapTableData.MapId);
                if(saved==null) throw new InvalidOperationException("Relocated browse position could not be captured.");
                ObservedVisit.Copy(saved);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FREE_BROWSE_PASS: occupied shelf slot avoided; same-shelf alternative reached; connected capture verified.");
                Vector3 before=first.transform.position;Quaternion facing=first.transform.rotation;
                Vector3 target=destination+(before-Vector3.up-destination).normalized*0.2f;
                for(int frame=0;frame<90;frame++)
                {
                    actor.GetComponent<CrowdMotion>().ResumeFromPause();actor.Place(target,Vector3.forward,waitAtOccupiedTarget:true);
                    // Arrived activities disable comfort steering; they must still
                    // honor occupied-target waiting rather than sidestepping.
                    actor.GetComponent<CrowdMotion>().ResumeFromPause();actor.Place(target,actor.transform.forward,true,waitAtOccupiedTarget:true);
                    if(Vector3.Distance(before,first.transform.position)>0.002f || Quaternion.Angle(facing,first.transform.rotation)>0.1f || !actor.IsStandingStill)
                        throw new InvalidOperationException("Waiting patron moved, flickered or animated walking.");
                    yield return null;
                }
                second.SetActive(false);float resumeUntil=Time.realtimeSinceStartup+1;
                while(Time.realtimeSinceStartup<resumeUntil)
                {actor.GetComponent<CrowdMotion>().ResumeFromPause();actor.Place(target,Vector3.forward,waitAtOccupiedTarget:true);yield return null;}
                if(Vector3.Distance(before,first.transform.position)<0.1f) throw new InvalidOperationException("Patron failed to resume after blocker left.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OCCUPIED_APPROACH_PASS: stationary idle/facing for 90 frames; resumes after blocker leaves.");
            }
            finally
            {
                if(first) UnityEngine.Object.Destroy(first);if(second) UnityEngine.Object.Destroy(second);
                foreach(var item in hidden) if(item) item.SetActive(true);host.enabled=enabled;streets.enabled=streetsEnabled;
            }
        }
    }
}
