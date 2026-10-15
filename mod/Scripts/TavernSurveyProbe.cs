using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using LivingCities.Core;
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
                VerifyRooms(room,beds,doors);
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
        private static void VerifyRooms(DaggerfallInterior interior,List<BedAnchor> beds,DaggerfallActionDoor[] doors)
        {
            var type=typeof(TavernSurveyProbe).Assembly.GetType("LivingCities.DFU.TavernRoomSurvey");
            if(type==null) throw new InvalidOperationException("Tavern room partition survey is missing.");
            var method=type.GetMethod("Read",BindingFlags.Public|BindingFlags.Static);
            var original=new List<bool>(); foreach(var door in doors) original.Add(door.IsOpen);
            try
            {
                foreach(var door in doors) door.SetOpen(false,true,true); Physics.SyncTransforms();
                var closed=(List<LodgingRoom>)method.Invoke(null,new object[] { interior });
                foreach(var door in doors) door.SetOpen(true,true,true); Physics.SyncTransforms();
                var opened=(List<LodgingRoom>)method.Invoke(null,new object[] { interior });
                Func<List<LodgingRoom>,string> signature=rooms=> {
                    var parts=new List<string>(); foreach(var room in rooms) parts.Add(room.Id+"="+string.Join(",",room.Beds.ToArray()));
                    return string.Join(";",parts.ToArray()); };
                if(signature(closed)!=signature(opened)) throw new InvalidOperationException("Opening tavern doors changed private room partition.");
                var assigned=new HashSet<string>();
                foreach(var room in closed) foreach(var bed in room.Beds)
                    if(!assigned.Add(bed)) throw new InvalidOperationException("Bed belongs to multiple tavern rooms.");
                foreach(var bed in beds) if(!assigned.Contains("bed:"+bed.RecordIndex)) throw new InvalidOperationException("Tavern bed missing from room partition: "+bed.RecordIndex);
                if(closed.Count<2) throw new InvalidOperationException("Known multi-room tavern collapsed into one room.");
                Debug.Log("LIVING_CITIES_TAVERN_ROOMS_PASS: building="+interior.EntryDoor.buildingKey+"; "+signature(closed)+"; open/closed identical.");
            }
            finally { for(int i=0;i<doors.Length;i++) doors[i].SetOpen(original[i],true,true); Physics.SyncTransforms(); }
        }
    }
}
