using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class WallQuarterProbe
    {
        public static IEnumerator Run(BuildingDirectory directory)
        {
            var game=GameManager.Instance;
            var models=new HashSet<uint>();
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach(var door in owner.Doors ?? new StaticDoor[0])
            {
                BuildingSummary building;
                if(door.doorType!=DoorTypes.Building || !directory.GetBuildingSummary(door.buildingKey,out building) ||
                    building.BuildingType!=DFLocation.BuildingTypes.Town23 || models.Contains(building.ModelID)) continue;
                var surveyed=WallQuarterBeds.Survey(owner.transform,door);
                TownSmokeHarness.EnterInterior(owner.transform,door);
                yield return new WaitForSecondsRealtime(1);
                var interior=game.PlayerEnterExit.Interior;
                if(!interior) throw new InvalidOperationException("Wall-quarter test did not enter native wall.");
                var beds=WallQuarterBeds.Furnish(interior);
                if(beds.Count==0) throw new InvalidOperationException("No safe bed footprint in wall model "+building.ModelID);
                if(surveyed.Count!=beds.Count) throw new InvalidOperationException("Unloaded wall bed survey differs from loaded room.");
                for(int i=0;i<beds.Count;i++)
                    if(Vector3.Distance(surveyed[i].Anchor.LocalCenter,beds[i].Anchor.LocalCenter)>0.02f)
                        throw new InvalidOperationException("Wall bed placement changed on entry: model="+building.ModelID+
                            "; bed="+i+"; surveyed="+surveyed[i].Anchor.LocalCenter.ToString("F5")+
                            "; loaded="+beds[i].Anchor.LocalCenter.ToString("F5"));
                if(WallQuarterBeds.Furnish(interior).Count!=beds.Count)
                    throw new InvalidOperationException("Repeated wall furnishing duplicated beds.");
                var navigation=new InteriorGeometry(interior);
                foreach(var bed in beds)
                {
                    Vector3 beside;
                    if(!navigation.StandBesideBed(bed.Anchor,out beside) || navigation.RouteToExit(beside).Count<2)
                        throw new InvalidOperationException("Wall bed lost reachable entrance after furnishing.");
                    if(!bed.GetComponent<MeshCollider>() || !bed.GetComponent<Renderer>().enabled)
                        throw new InvalidOperationException("Wall bed is not physical visible furniture.");
                }
                models.Add(building.ModelID);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_QUARTER_BEDS: model="+building.ModelID+"; building="+door.buildingKey+"; beds="+beds.Count);
                var route=beds[0].EntranceRoute;
                var end=route[route.Count-1];
                game.PlayerMotor.transform.position=interior.transform.TransformPoint(new Vector3((float)end.X,(float)end.Y,(float)end.Z))+Vector3.up;
                SleepProbe.AimCamera(interior.transform.TransformPoint(beds[0].Anchor.LocalCenter));
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"PeopleOfTheIliacBay-quarter-"+building.ModelID+".png"));
                yield return null;
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if(models.Count<3) throw new InvalidOperationException("Wall quarter test missed native layout models.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_QUARTER_BEDS_PASS: native wall models="+models.Count+"; visible physical beds, repeated furnishing and bedside access.");
        }
    }
}
