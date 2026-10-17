using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    // Map-pixel connectivity supplied by Basic Roads. Coordinates here are map
    // pixels, not Unity metres; runtime terrain/settlement routes refine them.
    public static class OverlandRoads
    {
        private static readonly int[] dx={0,1,1,1,0,-1,-1,-1};
        private static readonly int[] dz={-1,-1,0,1,1,1,0,-1};
        private sealed class Node
        {
            public int Index;
            public double Cost;
        }
        public static List<RoutePoint> Find(byte[] roads,int width,int height,
            int fromX,int fromZ,int toX,int toZ,int maximumExpanded)
        {
            var search=Begin(roads,width,height,fromX,fromZ,toX,toZ,maximumExpanded);
            while(!search.Complete) search.Advance(256);
            return new List<RoutePoint>(search.Result);
        }
        internal static Search Begin(byte[] roads,int width,int height,
            int fromX,int fromZ,int toX,int toZ,int maximumExpanded)
        {
            if(roads==null || width<=0 || height<=0 || (long)width*height!=roads.Length ||
                fromX<0 || fromX>=width || toX<0 || toX>=width || fromZ<0 || fromZ>=height ||
                toZ<0 || toZ>=height || maximumExpanded<=0)
                throw new ArgumentException("Invalid overland road search.");
            var search=new Search();
            search.Work=Solve(roads,width,height,fromX,fromZ,toX,toZ,maximumExpanded,
                result=>search.Publish(result));
            return search;
        }
        public sealed class Search
        {
            internal IEnumerator<int> Work;
            private IReadOnlyList<RoutePoint> result;
            public bool Complete { get; private set; }
            public bool Cancelled { get; private set; }
            public IReadOnlyList<RoutePoint> Result
            {
                get { if(!Complete) throw new InvalidOperationException("Road search is incomplete."); return result; }
            }
            internal void Publish(List<RoutePoint> path) { result=path.AsReadOnly(); }
            public void Advance(int maximumWorkSteps)
            {
                if(maximumWorkSteps<=0) throw new ArgumentOutOfRangeException(nameof(maximumWorkSteps));
                if(Complete) return;
                for(int i=0;i<maximumWorkSteps;i++)
                    if(!Work.MoveNext())
                    {
                        Complete=true; Work.Dispose(); Work=null;
                        return;
                    }
            }
            public void Cancel()
            {
                if(Complete) return;
                Work.Dispose(); Work=null; Cancelled=true; Complete=true;
                result=new List<RoutePoint>().AsReadOnly();
            }
        }
        private static IEnumerator<int> Solve(byte[] roads,int width,int height,
            int fromX,int fromZ,int toX,int toZ,int maximumExpanded,Action<List<RoutePoint>> publish)
        {
            int start=fromZ*width+fromX,goal=toZ*width+toX;
            var open=new SortedSet<Node>(Comparer<Node>.Create((a,b)=>{
                int order=a.Cost.CompareTo(b.Cost);
                return order!=0 ? order : a.Index.CompareTo(b.Index);
            }));
            var costs=new Dictionary<int,double>();
            var previous=new Dictionary<int,int>();
            costs.Add(start,0); open.Add(new Node {Index=start,Cost=0});
            int expanded=0;
            while(open.Count>0 && expanded<maximumExpanded)
            {
                var current=open.Min; open.Remove(current);
                // Count stale queue entries too, so they cannot monopolize a slice.
                yield return 0;
                if(current.Cost!=costs[current.Index]) continue;
                expanded++;
                if(current.Index==goal)
                {
                    var result=new List<RoutePoint>();
                    for(int index=goal;;index=previous[index])
                    {
                        result.Add(new RoutePoint(index%width,index/width));
                        yield return 0;
                        if(index==start) break;
                    }
                    result.Reverse(); publish(result); yield break;
                }
                int x=current.Index%width,z=current.Index/width;
                for(int direction=0;direction<8;direction++)
                {
                    if((roads[current.Index] & (128>>direction))==0) continue;
                    int nx=x+dx[direction],nz=z+dz[direction];
                    if(nx<0 || nx>=width || nz<0 || nz>=height) continue;
                    int next=nz*width+nx,opposite=(direction+4)%8;
                    if((roads[next] & (128>>opposite))==0) continue;
                    double cost=current.Cost+((direction&1)==0 ? 1 : Math.Sqrt(2));
                    double prior;
                    if(costs.TryGetValue(next,out prior) && prior<=cost) continue;
                    costs[next]=cost; previous[next]=current.Index;
                    open.Add(new Node {Index=next,Cost=cost});
                }
            }
            publish(new List<RoutePoint>());
        }
    }
}
