using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class RefugeGeometry
    {
        public static List<List<GuardPoint3>> Survey(Transform owner,StaticDoor door)
        {
            var result=new List<List<GuardPoint3>>();
            var operation=SurveySteps(owner,door,result);
            try { while(operation.MoveNext()) { } }
            finally { (operation as IDisposable)?.Dispose(); }
            return result;
        }
        public static System.Collections.IEnumerator SurveySteps(Transform owner,StaticDoor door,List<List<GuardPoint3>> result,
            List<WallQuarterBeds.SurveyedBed> beds=null)
        {
            if(!owner || door.doorType!=DoorTypes.Building) throw new ArgumentException("Missing refuge building door.");
            var temporary=new GameObject("People of the Iliac Bay isolated refuge geometry");
            // A model-only layout cannot register people or furniture
            // interactions. Move it above the world before querying colliders.
            var offset=Vector3.up*100;
            var shifted=door; shifted.ownerPosition+=offset;
            try
            {
                var interior=temporary.AddComponent<DaggerfallInterior>();
                var climate=ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType);
                if(!interior.DoLayoutAutomap(owner,shifted,climate)) yield break;
                temporary.transform.position=door.ownerPosition+(Vector3)door.buildingMatrix.GetColumn(3)+offset;
                temporary.transform.rotation=GameObjectHelper.QuaternionFromMatrix(door.buildingMatrix);
                foreach(var renderer in temporary.GetComponentsInChildren<Renderer>()) renderer.enabled=false;
                Physics.SyncTransforms();
                yield return null;
                var geometry=new InteriorGeometry(interior,deferSurvey:true);
                while(geometry.AdvanceSurvey()) yield return null;
                if(beds!=null)
                    foreach(var bed in BedGeometry.Read(interior))
                    {
                        yield return null;
                        Vector3 beside;
                        if(!geometry.StandBesideBed(bed,out beside)) continue;
                        var route=geometry.RouteToExit(beside);
                        if(route.Count<2) continue;
                        route.Reverse();
                        beds.Add(new WallQuarterBeds.SurveyedBed {Anchor=bed,EntranceRoute=route});
                    }
                int checkedAnchors=0;
                foreach(var anchor in geometry.LocalAnchors)
                {
                    if(++checkedAnchors%4==0) yield return null;
                    var ground=temporary.transform.TransformPoint(anchor);
                    // Do not reserve a foot position on a riser/ledge boundary.
                    // Small scene-transform rounding must not select another tread.
                    bool stable=true;
                    foreach(float x in new[] { -0.04f,0f,0.04f })
                    foreach(float z in new[] { -0.04f,0f,0.04f })
                    {
                        RaycastHit hit;
                        if(!Physics.Raycast(ground+new Vector3(x,0.6f,z),Vector3.down,out hit,1f,~0,QueryTriggerInteraction.Ignore) ||
                            !hit.transform.IsChildOf(temporary.transform) || hit.normal.y<0.65f || Mathf.Abs(hit.point.y-ground.y)>0.05f)
                            stable=false;
                    }
                    if(!stable) continue;
                    var route=geometry.RouteToExit(ground);
                    if(route.Count<2) continue;
                    route.Reverse(); result.Add(route);
                }
            }
            finally
            {
                temporary.SetActive(false);
                UnityEngine.Object.Destroy(temporary);
                Physics.SyncTransforms();
            }
        }
    }
}
