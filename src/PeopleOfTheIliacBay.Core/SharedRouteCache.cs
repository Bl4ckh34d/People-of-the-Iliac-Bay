using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // The owner scopes this to one topology and one movement footprint. Only
    // complete, verified routes are published. Returned mutable lists are copies.
    public sealed class SharedRouteCache
    {
        private readonly int capacity,maximumPoints;
        private readonly Dictionary<string,List<SavedRoutePoint>> routes=new Dictionary<string,List<SavedRoutePoint>>();
        private readonly Queue<string> order=new Queue<string>();
        public long Hits {get;private set;}
        public int Count {get{return routes.Count;}}
        public int PointCount {get;private set;}
        public SharedRouteCache(int capacity,int maximumPoints=65536)
        {if(capacity<1||maximumPoints<2)throw new ArgumentOutOfRangeException();this.capacity=capacity;this.maximumPoints=maximumPoints;}
        public bool TryGet(RoutePoint from,RoutePoint to,out List<SavedRoutePoint> points)
        {
            List<SavedRoutePoint> found;
            bool reverse=false;
            if(!routes.TryGetValue(Key(from,to),out found))
            {reverse=true;if(!routes.TryGetValue(Key(to,from),out found)){points=null;return false;}}
            points=Copy(found);if(reverse)points.Reverse();Hits++;return true;
        }
        public void Put(RoutePoint from,RoutePoint to,IList<SavedRoutePoint> points)
        {
            if(points==null||points.Count<2||points.Count>8192||points.Count>maximumPoints)return;
            string key=Key(from,to);if(routes.ContainsKey(key))return;
            while(routes.Count>=capacity||PointCount+points.Count>maximumPoints)
            {string old=order.Dequeue();PointCount-=routes[old].Count;routes.Remove(old);}
            routes.Add(key,Copy(points));order.Enqueue(key);PointCount+=points.Count;
        }
        public void Clear(){routes.Clear();order.Clear();PointCount=0;}
        public static string Key(RoutePoint from,RoutePoint to)
        {return from.X.ToString("R",CultureInfo.InvariantCulture)+","+from.Z.ToString("R",CultureInfo.InvariantCulture)+":"+
            to.X.ToString("R",CultureInfo.InvariantCulture)+","+to.Z.ToString("R",CultureInfo.InvariantCulture);}
        private static List<SavedRoutePoint> Copy(IList<SavedRoutePoint> source)
        {var result=new List<SavedRoutePoint>(source.Count);foreach(var p in source)result.Add(new SavedRoutePoint{X=p.X,Z=p.Z});return result;}
    }
}
