using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingCities.Core
{
    public sealed class WitnessRoutePlan
    {
        public string GuardId;
        public List<SavedRoutePoint> Route;
        public double Length;
    }

    public sealed partial class GuardRoster
    {
        // Uses only already-surveyed street segments. It remains usable while the
        // player is indoors and the exterior physics scene is unloaded.
        public WitnessRoutePlan PlanWitnessRoute(int buildingKey,double now,SavedRoutePoint from=null,SavedRoutePoint threat=null,bool retreatIfBlocked=false)
        {
            Require(buildingKey>=0 && Time(now),"Invalid witness route query.");
            var danger=threat==null ? null : CopyPoint(threat);
            var approach=BuildingApproaches.Find(a=>a.BuildingKey==buildingKey);
            if(approach==null) return null;
            var graph=new WitnessStreetGraph();
            foreach(var route in approach.Routes) graph.AddRoute(route.Points);
            foreach(var patrol in Patrols)
            {
                graph.AddRoute(patrol.Plan.LeaderJoin); graph.AddRoute(patrol.Plan.FollowerJoin);
                graph.AddRoute(patrol.Plan.Circuit);
            }
            var candidates=new Dictionary<string,SavedRoutePoint>();
            foreach(var guard in Guards)
                if(guard.IsAlive && guard.Duty==GuardDuty.Posted && guard.Interior==null && guard.Refuge==null)
                    candidates.Add(guard.Id,Position(guard.Id,now));
            return graph.Nearest(CopyPoint(from??approach.Entrance),candidates,danger,retreatIfBlocked);
        }
    }

    internal sealed class WitnessStreetGraph
    {
        private struct Edge { public int To; public double Length; }
        private struct Segment { public int A,B; }
        private readonly List<SavedRoutePoint> points=new List<SavedRoutePoint>();
        private readonly List<List<Edge>> edges=new List<List<Edge>>();
        private readonly List<Segment> segments=new List<Segment>();
        private readonly Dictionary<string,int> indices=new Dictionary<string,int>();
        private int Node(SavedRoutePoint p)
        {
            string key=p.X.ToString("F3",CultureInfo.InvariantCulture)+":"+p.Z.ToString("F3",CultureInfo.InvariantCulture);
            int id; if(indices.TryGetValue(key,out id)) return id;
            id=points.Count; points.Add(new SavedRoutePoint{X=p.X,Z=p.Z}); edges.Add(new List<Edge>()); indices.Add(key,id); return id;
        }
        private static double Distance(SavedRoutePoint a,SavedRoutePoint b)
        { double x=a.X-b.X,z=a.Z-b.Z; return Math.Sqrt(x*x+z*z); }
        private void Connect(int a,int b)
        {
            if(a==b) return;
            double distance=Distance(points[a],points[b]);
            edges[a].Add(new Edge{To=b,Length=distance}); edges[b].Add(new Edge{To=a,Length=distance});
        }
        public void AddRoute(List<SavedRoutePoint> route)
        {
            for(int i=1;i<route.Count;i++)
            {
                int a=Node(route[i-1]),b=Node(route[i]);
                if(a==b) continue;
                segments.Add(new Segment{A=a,B=b});
            }
        }
        private bool OnSegment(SavedRoutePoint p,Segment s)
        {
            var a=points[s.A]; var b=points[s.B]; double x=b.X-a.X,z=b.Z-a.Z;
            double t=((p.X-a.X)*x+(p.Z-a.Z)*z)/(x*x+z*z);
            if(t<0 || t>1) return false;
            double dx=p.X-a.X-t*x,dz=p.Z-a.Z-t*z;
            return dx*dx+dz*dz<=0.000001;
        }
        private void BuildConnections()
        {
            // Split surveyed segments at all known junctions and query points.
            // Connecting each attachment only to the original endpoints misses
            // branches and overestimates travel between interior attachments.
            var ordered=new List<int>();
            for(int i=0;i<points.Count;i++) ordered.Add(i);
            ordered.Sort((a,b)=>points[a].X.CompareTo(points[b].X));
            foreach(var segment in segments)
            {
                var a=points[segment.A]; var b=points[segment.B];
                double minX=Math.Min(a.X,b.X)-0.001,maxX=Math.Max(a.X,b.X)+0.001;
                double minZ=Math.Min(a.Z,b.Z)-0.001,maxZ=Math.Max(a.Z,b.Z)+0.001;
                int low=0,high=ordered.Count;
                while(low<high)
                { int mid=(low+high)/2; if(points[ordered[mid]].X<minX) low=mid+1; else high=mid; }
                var chain=new List<int>();
                for(int i=low;i<ordered.Count && points[ordered[i]].X<=maxX;i++)
                {
                    int node=ordered[i]; var p=points[node];
                    if(p.Z>=minZ && p.Z<=maxZ && OnSegment(p,segment)) chain.Add(node);
                }
                bool alongX=Math.Abs(b.X-a.X)>=Math.Abs(b.Z-a.Z);
                chain.Sort((left,right)=>alongX ? points[left].X.CompareTo(points[right].X) : points[left].Z.CompareTo(points[right].Z));
                for(int i=1;i<chain.Count;i++) Connect(chain[i-1],chain[i]);
            }
        }
        public WitnessRoutePlan Nearest(SavedRoutePoint source,Dictionary<string,SavedRoutePoint> candidates,SavedRoutePoint threat,bool retreatIfBlocked)
        {
            int start=Node(source);
            var targets=new Dictionary<int,string>();
            foreach(var candidate in candidates)
            {
                int node=Node(candidate.Value);
                string previous;
                if(!targets.TryGetValue(node,out previous) || string.CompareOrdinal(candidate.Key,previous)<0) targets[node]=candidate.Key;
            }
            BuildConnections();
            var distance=new double[points.Count]; var parent=new int[points.Count];
            for(int i=0;i<distance.Length;i++) { distance[i]=double.MaxValue; parent[i]=-1; }
            var queue=new MinHeap(); distance[start]=0; queue.Push(start,0);
            int found=-1;
            while(queue.Count>0)
            {
                double cost; int node=queue.Pop(out cost);
                if(cost>distance[node]) continue;
                if(targets.ContainsKey(node)) { found=node; break; }
                foreach(var edge in edges[node])
                {
                    // Direction matters: fleeing may traverse a street away from danger
                    // even when the reverse edge would approach or cross the attacker.
                    if(threat!=null && !FlightSafety.SafeStep(points[node].X,points[node].Z,
                        points[edge.To].X,points[edge.To].Z,threat.X,threat.Z)) continue;
                    double next=cost+edge.Length;
                    if(next>=distance[edge.To]) continue;
                    distance[edge.To]=next; parent[edge.To]=node; queue.Push(edge.To,next);
                }
            }
            bool retreat=found<0;
            if(retreat)
            {
                if(threat==null || !retreatIfBlocked) return null;
                // A short reachable retreat stays on the surveyed network so future
                // guard replans can reconnect. A trapped witness holds position.
                found=start; double bestGain=0,bestCost=double.MaxValue;
                for(int node=0;node<points.Count;node++)
                {
                    if(distance[node]==double.MaxValue) continue;
                    double gain=Math.Min(4,Distance(points[node],threat)-Distance(source,threat));
                    if(gain>bestGain || gain>0 && gain==bestGain && distance[node]<bestCost)
                    {found=node;bestGain=gain;bestCost=distance[node];}
                }
            }
            var result=new List<SavedRoutePoint>();
            for(int node=found;node>=0;node=parent[node]) result.Add(new SavedRoutePoint{X=points[node].X,Z=points[node].Z});
            result.Reverse(); if(result.Count==1) result.Add(new SavedRoutePoint{X=source.X,Z=source.Z});
            return new WitnessRoutePlan{GuardId=retreat ? null : targets[found],Route=result,Length=distance[found]};
        }
        private sealed class MinHeap
        {
            private struct Item { public int Node; public double Cost; }
            private readonly List<Item> items=new List<Item>();
            public int Count { get { return items.Count; } }
            public void Push(int node,double cost)
            {
                var item=new Item{Node=node,Cost=cost}; int i=items.Count; items.Add(item);
                while(i>0) { int p=(i-1)/2; if(items[p].Cost<=cost) break; items[i]=items[p]; i=p; }
                items[i]=item;
            }
            public int Pop(out double cost)
            {
                var result=items[0]; var last=items[items.Count-1]; items.RemoveAt(items.Count-1);
                if(items.Count>0)
                {
                    int i=0;
                    while(i*2+1<items.Count)
                    {
                        int child=i*2+1; if(child+1<items.Count && items[child+1].Cost<items[child].Cost) child++;
                        if(items[child].Cost>=last.Cost) break;
                        items[i]=items[child]; i=child;
                    }
                    items[i]=last;
                }
                cost=result.Cost; return result.Node;
            }
        }
    }
}
