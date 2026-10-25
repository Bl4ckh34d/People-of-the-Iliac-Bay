using System;
using System.Collections;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        public sealed class HouseholdLaneResult {public List<SavedRoutePoint> First,Second;}
        public IEnumerator PrepareHouseholdLanes(IList<SavedRoutePoint> source,HouseholdLaneResult result,Func<bool> valid)
        {
            if(source==null || source.Count<2 || source.Count>10000)yield break;
            var controls=new List<RoutePoint>();foreach(var point in source)controls.Add(new RoutePoint(point.X,point.Z));
            for(int attempt=0;attempt<7;attempt++)
            {
                double width=attempt==6?0:0.36/Math.Pow(2,attempt);
                var first=new List<SavedRoutePoint>();var second=new List<SavedRoutePoint>();
                for(int i=0;i<controls.Count;i++)
                {
                    if(!valid())yield break;
                    double offset=i==0 || i==controls.Count-1?0:width;
                    var normal=offset==0?new RoutePoint(0,0):CorridorLaneRoutes.NormalAt(controls,i);
                    first.Add(new SavedRoutePoint{X=controls[i].X+normal.X*offset,Z=controls[i].Z+normal.Z*offset});
                    second.Add(new SavedRoutePoint{X=controls[i].X-normal.X*offset,Z=controls[i].Z-normal.Z*offset});
                    if(i%16==0)yield return null;
                }
                bool clear=true;
                for(int i=1;i<first.Count;i++)
                {
                    if(!valid())yield break;
                    if(!LaneSegmentClear(new RoutePoint(first[i-1].X,first[i-1].Z),new RoutePoint(first[i].X,first[i].Z)) ||
                        !LaneSegmentClear(new RoutePoint(second[i-1].X,second[i-1].Z),new RoutePoint(second[i].X,second[i].Z)))
                    {clear=false;break;}
                    if(i%2==0)yield return null;
                }
                if(clear){result.First=first;result.Second=second;yield break;}
                yield return null;
            }
        }
    }
}
