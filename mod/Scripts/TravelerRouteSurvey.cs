using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class TownGeometry
    {
        public sealed class TravelerAccess
        {
            public bool Walled;
            public List<RoutePoint> Points;
        }
        // Road runs at the navigation boundary are local settlement approaches.
        // Beyond this boundary, regional travel remains abstract until road integration.
        public List<TravelerAccess> SurveyTravelerRoutes(int buildingKey)
        {
            var result=new List<TravelerAccess>(); Entrance inn;
            if(!Entrances.TryGetValue(buildingKey,out inn)) return result;
            var gates=origin.GetComponentsInChildren<DaggerfallCityGate>();
            bool walled=gates.Length>0;
            if(walled)
            {
                var center=SceneCell(new GridCell(Width/2,Height/2));
                var approaches=new List<Entrance>();
                foreach(var gate in gates)
                {
                    var mesh=gate.GetComponent<MeshFilter>();
                    if(!mesh || !mesh.sharedMesh) continue;
                    var bounds=mesh.sharedMesh.bounds;
                    bool alongX=bounds.size.x<bounds.size.z;
                    Vector3 normal=gate.transform.TransformDirection(alongX ? Vector3.right : Vector3.forward);
                    var middle=gate.transform.TransformPoint(bounds.center);
                    if(Vector3.Dot(normal,middle-center)<0) normal=-normal;
                    float extent=(alongX ? bounds.extents.x*gate.transform.lossyScale.x : bounds.extents.z*gate.transform.lossyScale.z)+1;
                    var local=origin.InverseTransformPoint(middle+normal*extent);
                    var ground=ScenePoint(new RoutePoint(local.x,local.z)); GridCell cell;
                    var inside=origin.InverseTransformPoint(middle-normal*extent);
                    var insideGround=ScenePoint(new RoutePoint(inside.x,inside.z));
                    // Native navigation can disconnect the gate tile itself. Connect
                    // to the inner street; RouteBetween still sweeps the full crossing.
                    if(!FindEntranceCell(insideGround,out cell))
                    { Debug.Log("LIVING_CITIES_GATE_APPROACH_UNREACHED: local="+local+"; bounds="+bounds); continue; }
                    approaches.Add(new Entrance { Cell=cell,LocalDoor=origin.InverseTransformPoint(ground) });
                }
                approaches.Sort((a,b)=>Compare(a.LocalDoor,b.LocalDoor));
                foreach(var approach in approaches)
                {
                    var route=RouteBetween(approach,inn);
                    if(route.Count>=2) result.Add(new TravelerAccess { Walled=true,Points=route });
                    if(result.Count>=4) break;
                }
                return result;
            }
            var candidates=new List<GridCell>();
            for(int side=0;side<4;side++)
            {
                int length=side<2 ? Width : Height;
                int begin=-1;
                for(int n=0;n<=length;n++)
                {
                    var cell=BoundaryCell(side,n);
                    bool road=n<length && navigation.GetNavGridWeightLocal(cell.X,cell.Z)==15;
                    if(road && begin<0) begin=n;
                    if(!road && begin>=0)
                    { candidates.Add(BoundaryCell(side,(begin+n-1)/2)); begin=-1; }
                }
            }
            candidates.Sort((a,b)=> {
                long da=(long)(a.X-inn.Cell.X)*(a.X-inn.Cell.X)+(long)(a.Z-inn.Cell.Z)*(a.Z-inn.Cell.Z);
                long db=(long)(b.X-inn.Cell.X)*(b.X-inn.Cell.X)+(long)(b.Z-inn.Cell.Z)*(b.Z-inn.Cell.Z);
                int order=da.CompareTo(db); if(order!=0) return order;
                order=a.X.CompareTo(b.X); return order!=0 ? order : a.Z.CompareTo(b.Z);
            });
            var used=new HashSet<GridCell>(); int attempted=0;
            foreach(var cell in candidates)
            {
                if(!used.Add(cell)) continue;
                if(attempted++>=32 || result.Count>=4) break;
                var local=origin.InverseTransformPoint(SceneCell(cell));
                var route=RouteBetween(new Entrance { Cell=cell,LocalDoor=local },inn);
                if(route.Count<2) continue;
                result.Add(new TravelerAccess { Walled=walled,Points=route });
            }
            return result;
        }
        private GridCell BoundaryCell(int side,int n)
        {
            if(side==0) return new GridCell(n,0);
            if(side==1) return new GridCell(n,Height-1);
            if(side==2) return new GridCell(0,n);
            return new GridCell(Width-1,n);
        }
    }
}
