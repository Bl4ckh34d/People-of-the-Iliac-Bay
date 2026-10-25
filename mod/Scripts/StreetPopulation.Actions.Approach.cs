using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private List<GuardPoint3> IndoorPersonApproachRoute(Vector3 actual,Vector3 target,Vector3 preferred,float range)
        {
            var origin=interior.transform.InverseTransformPoint(actual);
            var localTarget=interior.transform.InverseTransformPoint(target);
            var localPreferred=interior.transform.InverseTransformPoint(preferred);
            var candidates=new List<GuardPoint3>{ApproachPoint(localPreferred)};
            foreach(var point in indoorGeometry.NearbyApproachPoints(localTarget,range,0.4f,31))
            {
                if((point-localPreferred).sqrMagnitude<0.000001f) continue;
                candidates.Add(ApproachPoint(point));
            }
            int preferredCount=-1;
            bool preferredSight=CivilianResponses.Sees(preferred+Vector3.up,target+Vector3.up,range);
            var route=NpcActionApproach.FindRoute(ApproachPoint(localTarget),range,candidates,point=>{
                var end=new Vector3((float)point.X,(float)point.Y,(float)point.Z);
                var found=shopGeometry!=null?shopGeometry.ActivityRouteBetween(origin,end):indoorGeometry.RouteBetween(origin,end);
                if((end-localPreferred).sqrMagnitude<0.000001f) preferredCount=found==null?0:found.Count;
                return found;
            },point=>{
                var world=interior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                return Vector3.Distance(world,target)<=range && CivilianResponses.Sees(world+Vector3.up,target+Vector3.up,range);
            });
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-probe")>=0)
            {
                var end=route==null?null:route[route.Count-1];
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_APPROACH_ROUTE: from="+origin.ToString("F3")+"; target="+localTarget.ToString("F3")+
                    "; radial="+localPreferred.ToString("F3")+"; radialRoutePoints="+preferredCount+"; radialSight="+preferredSight+
                    "; selected="+(end==null?"none":new Vector3((float)end.X,(float)end.Y,(float)end.Z).ToString("F3"))+
                    "; range="+range+"; routePoints="+(route==null?0:route.Count));
            }
            return route;
        }
        private static GuardPoint3 ApproachPoint(Vector3 point) {return new GuardPoint3{X=point.x,Y=point.y,Z=point.z};}
    }
}
