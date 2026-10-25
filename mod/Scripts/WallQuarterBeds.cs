using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class QuarterBed : MonoBehaviour
    {
        public BedAnchor Anchor;
        public List<GuardPoint3> EntranceRoute;
    }

    // Native wall rooms contain no beds. Add classic furniture only where its full
    // footprint and post-placement access have been checked against the interior.
    public static class WallQuarterBeds
    {
        public sealed class SurveyedBed
        {
            public BedAnchor Anchor;
            public List<GuardPoint3> EntranceRoute;
        }
        public static List<SurveyedBed> Survey(Transform owner,StaticDoor door)
        {
            var result=new List<SurveyedBed>();
            var temporary=new GameObject("People of the Iliac Bay isolated wall quarters");
            var offset=Vector3.up*100; var shifted=door; shifted.ownerPosition+=offset;
            try
            {
                var interior=temporary.AddComponent<DaggerfallInterior>();
                var climate=ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType);
                if(!interior.DoLayoutAutomap(owner,shifted,climate)) return result;
                temporary.transform.position=door.ownerPosition+(Vector3)door.buildingMatrix.GetColumn(3)+offset;
                temporary.transform.rotation=GameObjectHelper.QuaternionFromMatrix(door.buildingMatrix);
                foreach(var renderer in temporary.GetComponentsInChildren<Renderer>()) renderer.enabled=false;
                Physics.SyncTransforms();
                foreach(var bed in Furnish(interior))
                    result.Add(new SurveyedBed { Anchor=bed.Anchor,EntranceRoute=bed.EntranceRoute });
                return result;
            }
            finally
            { temporary.SetActive(false); UnityEngine.Object.Destroy(temporary); Physics.SyncTransforms(); }
        }

        public static List<QuarterBed> Furnish(DaggerfallInterior interior,int capacity=2)
        {
            var existing=new List<QuarterBed>(interior.GetComponentsInChildren<QuarterBed>());
            if(existing.Count>0) return existing;
            ModelData model;
            if(!DaggerfallUnity.Instance.MeshReader.GetModelData(41000,out model)) return existing;
            Bounds full=new Bounds(model.Vertices[0],Vector3.zero);
            foreach(var vertex in model.Vertices) full.Encapsulate(vertex);
            var mattress=BedGeometry.Mattress(model);
            var navigation=new InteriorGeometry(interior);
            if(!navigation.EntryGround.HasValue) return existing;
            var candidates=new List<Vector3>(navigation.LocalAnchors);
            // A shifted isolated layout has slightly different float rounding on an
            // otherwise level floor. Do not let those sub-millimetre errors reorder beds.
            candidates.Sort((a,b)=> { int n=Mathf.RoundToInt(b.y*100).CompareTo(Mathf.RoundToInt(a.y*100)); if(n!=0) return n;
                n=Mathf.RoundToInt(a.x*100).CompareTo(Mathf.RoundToInt(b.x*100));
                return n!=0 ? n : Mathf.RoundToInt(a.z*100).CompareTo(Mathf.RoundToInt(b.z*100)); });
            foreach(var floor in candidates)
            foreach(float angle in new[] { 0f,90f,180f,270f })
            {
                if(existing.Count>=capacity) return existing;
                var rotation=Quaternion.Euler(0,angle,0);
                var position=floor-rotation*new Vector3(full.center.x,full.min.y,full.center.z);
                if(!ClearFootprint(interior,full,position,rotation,navigation.EntryGround.Value)) continue;
                var prop=GameObjectHelper.CreateDaggerfallMeshGameObject(41000,interior.transform);
                prop.name="People of the Iliac Bay wall-quarter bed "+existing.Count;
                prop.transform.localPosition=position; prop.transform.localRotation=rotation;
                Physics.SyncTransforms();
                var anchor=new BedAnchor { RecordIndex=1000000+existing.Count,ModelId=41000,
                    LocalCenter=position+rotation*mattress.center,
                    LocalHeadDirection=rotation*Vector3.right,LocalSideDirection=rotation*Vector3.forward,
                    Length=mattress.size.x,Width=mattress.size.z };
                var after=new InteriorGeometry(interior);
                Vector3 bedside;
                bool valid=after.StandBesideBed(anchor,out bedside);
                var route=valid ? after.RouteToExit(bedside) : new List<GuardPoint3>();
                valid=route.Count>=2;
                // Furniture may consume its own footprint, but must not cut off other rooms/stairs.
                foreach(var oldAnchor in navigation.LocalAnchors)
                {
                    var inModel=Quaternion.Inverse(rotation)*(oldAnchor-position);
                    bool covered=inModel.x>=full.min.x-0.3f && inModel.x<=full.max.x+0.3f &&
                        inModel.z>=full.min.z-0.3f && inModel.z<=full.max.z+0.3f &&
                        Math.Abs(inModel.y-full.min.y)<0.2f;
                    if(!covered && after.RouteToExit(interior.transform.TransformPoint(oldAnchor)).Count<2) valid=false;
                }
                if(!valid)
                { prop.SetActive(false); UnityEngine.Object.Destroy(prop); Physics.SyncTransforms(); continue; }
                route.Reverse();
                var bed=prop.AddComponent<QuarterBed>(); bed.Anchor=anchor; bed.EntranceRoute=route;
                existing.Add(bed); navigation=after;
            }
            return existing;
        }

        private static bool ClearFootprint(DaggerfallInterior interior,Bounds bounds,Vector3 position,
            Quaternion rotation,Vector3 entrance)
        {
            var worldRotation=interior.transform.rotation*rotation;
            var floor=interior.transform.TransformPoint(position+rotation*new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
            if(Vector3.Distance(floor,entrance)<3.5f) return false;
            foreach(var owner in interior.GetComponentsInChildren<DaggerfallStaticDoors>())
            for(int i=0;i<(owner.Doors==null ? 0 : owner.Doors.Length);i++)
            {
                var doorway=Quaternion.Inverse(worldRotation)*(owner.GetDoorPosition(i)-floor);
                if(Mathf.Abs(doorway.y)<3 && Mathf.Abs(doorway.x)<bounds.extents.x+0.9f &&
                    Mathf.Abs(doorway.z)<bounds.extents.z+0.9f) return false;
            }
            var half=new Vector3(bounds.extents.x+0.15f,bounds.extents.y+0.1f,bounds.extents.z+0.15f);
            var center=interior.transform.TransformPoint(position+rotation*bounds.center)+Vector3.up*0.12f;
            foreach(var collider in Physics.OverlapBox(center,half,worldRotation,~0,QueryTriggerInteraction.Ignore))
                if(collider.transform.IsChildOf(interior.transform)) return false;
            for(int x=-2;x<=2;x++) for(int z=-2;z<=2;z++)
            {
                var sample=floor+worldRotation*new Vector3(x*(bounds.extents.x+0.1f)/2,0,z*(bounds.extents.z+0.1f)/2);
                RaycastHit hit;
                if(!Physics.Raycast(sample+Vector3.up*0.3f,Vector3.down,out hit,0.5f,~0,QueryTriggerInteraction.Ignore) ||
                    !hit.transform.IsChildOf(interior.transform) || hit.normal.y<0.99f ||
                    Mathf.Abs(hit.point.y-floor.y)>0.05f) return false;
            }
            return true;
        }
    }
}
