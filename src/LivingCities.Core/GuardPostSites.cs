using System;
namespace LivingCities.Core
{
    public static class GuardPostSites
    {
        public static bool IsJunction(int x,int z,Func<int,int,int> weight,int reach)
        {
            if(weight==null || reach<1) throw new ArgumentException("Invalid post survey.");
            if(!Paved(weight(x,z))) return false;
            int branches=0;
            foreach(var direction in new[] { new[] { 1,0 },new[] { -1,0 },new[] { 0,1 },new[] { 0,-1 } })
            {
                bool clear=true;
                for(int step=1;step<=reach;step++)
                    if(!Paved(weight(x+direction[0]*step,z+direction[1]*step))) { clear=false; break; }
                if(clear) branches++;
            }
            return branches>=3;
        }
        private static bool Paved(int weight) { return weight==15 || weight==4; }
    }
    public sealed partial class GuardRoster
    {
        public void MovePost(string id,System.Collections.Generic.List<SavedRoutePoint> route,double now,double speed,double timeScale)
        {
            var points=CopyRoute(route);
            Require(points.Count>=2 && Finite(speed) && speed>0 && Finite(timeScale) && timeScale>0,"Invalid post relocation.");
            var guard=Find(id);
            Require(Distance(points[0],guard.Post)<0.05,"Post relocation must start at the old post.");
            double arrival=now+Length(points)/speed*timeScale;
            Require(Time(now) && Time(arrival),"Invalid post relocation time.");
            string report="post:"+id+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            Require(!Reports.Exists(r=>r.Id==report),"Post relocation already exists.");
            ReassignPost(id,points[points.Count-1],now);
            Reports.Add(new GuardReport { Id=report,GuardIds=new System.Collections.Generic.List<string> { id } });
            guard.Duty=GuardDuty.Returning; guard.IncidentId=report; guard.Route=points;
            guard.DepartedAt=guard.SearchUntil=now; guard.ArrivesAt=arrival;
        }
        // Apply on settlement reconstruction only. Active journeys keep their old
        // destination until a later safe reconstruction; no identity is regenerated.
        public void ReassignPost(string id,SavedRoutePoint destination,double now)
        {
            var point=CopyPoint(destination);
            var preview=CopyValidated(this); preview.Advance(now);
            var candidate=preview.Find(id);
            Require(candidate.IsAlive && candidate.Duty==GuardDuty.Posted && candidate.Interior==null &&
                candidate.Refuge==null && !preview.IsOnPatrol(id),"Guard is unavailable for post reassignment.");
            Require(!Guards.Exists(g=>g.Id!=id && g.IsAlive && Distance(g.Post,point)<0.8),"Guard post is occupied.");
            Advance(now); Find(id).Post=point;
            // Cached routes and completed patrol plans originate at the old post.
            BuildingApproaches.Clear();
        }
    }
}
