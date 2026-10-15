using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class TavernSurveyProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(game.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var taverns=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            if(taverns.Count==0) throw new InvalidOperationException("No taverns to survey.");
            int totalBeds=0;
            foreach(var tavern in taverns)
            {
                DaggerfallStaticDoors owner=null; StaticDoor entry=default(StaticDoor);
                foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in candidate.Doors)
                        if(door.doorType==DoorTypes.Building && door.buildingKey==tavern.buildingKey)
                        { owner=candidate; entry=door; break; }
                if(!owner) throw new InvalidOperationException("Tavern entrance missing.");
                TownSmokeHarness.EnterInterior(owner.transform,entry);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                var room=game.PlayerEnterExit.Interior;
                if(!room || room.EntryDoor.buildingKey!=tavern.buildingKey) throw new InvalidOperationException("Wrong tavern loaded.");
                var beds=BedGeometry.Read(room); totalBeds+=beds.Count;
                var rest=room.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
                for(int index=0;index<rest.Length;index++)
                    Debug.Log("LIVING_CITIES_TAVERN_REST: building="+tavern.buildingKey+"; index="+index+
                        "; position="+room.transform.InverseTransformPoint(rest[index]).ToString("F3"));
                var navigation=new InteriorGeometry(room); int reachable=0;
                var bedIds=new HashSet<int>();
                foreach(var bed in beds)
                {
                    if(!bedIds.Add(bed.RecordIndex)) throw new InvalidOperationException("Duplicate tavern bed identity.");
                    Vector3 ground; bool access=navigation.StandBesideBed(bed,out ground);
                    bool route=access && navigation.RouteToExit(ground).Count>=2; if(route) reachable++;
                    Debug.Log("LIVING_CITIES_TAVERN_BED: building="+tavern.buildingKey+"; bed="+bed.RecordIndex+
                        "; position="+bed.LocalCenter.ToString("F3")+"; accessible="+route);
                }
                var doors=room.GetComponentsInChildren<DaggerfallActionDoor>();
                foreach(var door in doors)
                    Debug.Log("LIVING_CITIES_TAVERN_DOOR: building="+tavern.buildingKey+"; id="+door.LoadID+
                        "; position="+room.transform.InverseTransformPoint(door.transform.position).ToString("F3")+
                        "; closedRotation="+door.ClosedRotation.eulerAngles.ToString("F3"));
                foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
                    Debug.Log("LIVING_CITIES_TAVERN_NPC: building="+tavern.buildingKey+"; hash="+npc.Data.hash+
                        "; faction="+npc.Data.factionID+"; position="+room.transform.InverseTransformPoint(npc.transform.position).ToString("F3"));
                Debug.Log("LIVING_CITIES_TAVERN_SURVEY: building="+tavern.buildingKey+"; beds="+beds.Count+
                    "; reachableBeds="+reachable+"; actionDoors="+doors.Length);
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
            }
            Debug.Log("LIVING_CITIES_TAVERN_SURVEY_PASS: taverns="+taverns.Count+"; beds="+totalBeds+"; native interior census complete.");
        }
    }
}
