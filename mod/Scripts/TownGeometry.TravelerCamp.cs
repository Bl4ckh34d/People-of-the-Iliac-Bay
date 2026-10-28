using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        // Reuse the surveyed gate/road approach in reverse, then walk clear terrain
        // until the actual native location rectangle is behind the camp point.
        public bool TryTravelerCampRoute(IList<SavedRoutePoint> approach,out List<SavedRoutePoint> route)
        {
            route=new List<SavedRoutePoint>();
            var native=terrain?terrain.GetComponent<DaggerfallTerrain>():null;
            if(approach==null || approach.Count<2 || !terrain || !terrain.terrainData || !native || !native.MapData.hasLocation)return false;
            var rectangle=native.MapData.locationRect;
            var size=terrain.terrainData.size;
            for(int i=approach.Count-1;i>=0;i--)
            {
                var point=approach[i];
                if(route.Count>0)
                {
                    var last=route[route.Count-1];
                    if(!NativeCityGateNavigation.Clear(ScenePoint(new RoutePoint(last.X,last.Z)),ScenePoint(new RoutePoint(point.X,point.Z))))return false;
                }
                route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            }
            var endpoint=ScenePoint(new RoutePoint(approach[0].X,approach[0].Z));
            var local=terrain.transform.InverseTransformPoint(endpoint);
            var center=new Vector3(rectangle.center.x/MapsFile.WorldMapTileDim*size.x,local.y,
                rectangle.center.y/MapsFile.WorldMapTileDim*size.z);
            var direction=(local-center);direction.y=0;
            if(direction.sqrMagnitude<1)return false;direction.Normalize();
            bool outside=false;float outsideMetres=0;
            for(int step=1;step<=128;step++)
            {
                var sample=local+direction*(step*4);
                if(sample.x<0 || sample.z<0 || sample.x>size.x || sample.z>size.z)return false;
                var position=terrain.transform.TransformPoint(sample);
                position.y=terrain.SampleHeight(position)+terrain.transform.position.y;
                var previous=route[route.Count-1];var ground=ScenePoint(new RoutePoint(previous.X,previous.Z));
                if(Math.Abs(position.y-ground.y)>2 || !NativeCityGateNavigation.Clear(ground,position))return false;
                var point=origin.InverseTransformPoint(position);route.Add(new SavedRoutePoint {X=point.x,Z=point.z});
                bool clear=!rectangle.Contains(new Vector2(sample.x/size.x*MapsFile.WorldMapTileDim,sample.z/size.z*MapsFile.WorldMapTileDim));
                if(clear){outside=true;outsideMetres+=4;}else{outside=false;outsideMetres=0;}
                if(outside && outsideMetres>=16)return true;
            }
            route.Clear();return false;
        }
    }
}
