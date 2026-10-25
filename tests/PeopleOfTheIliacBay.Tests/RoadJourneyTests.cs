using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoadJourneyTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Road map positions cross terrain edges continuously with north mapped to positive Z",()=>{
                var center=new RoadTerrainPoint(new RoutePoint(207,213));
                Check.Equal(207,center.PixelX); Check.Equal(213,center.PixelY);
                Check.Near(0.5,center.U); Check.Near(0.5,center.V);
                var east=new RoadTerrainPoint(new RoutePoint(207.5,213));
                Check.Equal(208,east.PixelX); Check.Near(0,east.U);
                var north=new RoadTerrainPoint(new RoutePoint(207,212.49));
                Check.Equal(212,north.PixelY); Check.Near(0.01,north.V);
                Check.Throws<ArgumentException>(()=>new RoadTerrainPoint(new RoutePoint(double.NaN,0)));
            }));
            cases.Add(("Incremental road searches publish only complete paths and match synchronous routing",()=>{
                var map=new byte[]{8,0,8,160,34,130,0,0,0};
                var network=new OverlandRoadNetwork(map,new byte[9],3,3);
                var expected=network.Find(0,0,2,0,20);
                var search=network.BeginSearch(0,0,2,0,20);
                Check.Throws<InvalidOperationException>(()=>{ var result=search.Result; });
                int slices=0;
                while(!search.Complete) { search.Advance(1); slices++; Check.True(slices<100,"Search did not finish"); }
                Check.True(slices>expected.Count,"Search was not sliced");
                Check.Equal(expected.Count,search.Result.Count);
                for(int i=0;i<expected.Count;i++) { Check.Equal(expected[i].X,search.Result[i].X); Check.Equal(expected[i].Z,search.Result[i].Z); }
            }));
            cases.Add(("Incremental searches cancel without publishing partial routes",()=>{
                var network=new OverlandRoadNetwork(new byte[]{32,34,2},new byte[3],3,1);
                var search=network.BeginSearch(0,0,2,0,3); search.Advance(1); search.Cancel(); search.Advance(20);
                Check.True(search.Complete && search.Cancelled,"Cancellation state missing");
                Check.Equal(0,search.Result.Count);
                var limited=network.BeginSearch(0,0,2,0,1);
                while(!limited.Complete) limited.Advance(1);
                Check.Equal(0,limited.Result.Count);
                Check.Throws<ArgumentOutOfRangeException>(()=>network.BeginSearch(0,0,2,0,3).Advance(0));
                Check.Equal(3,network.Find(0,0,2,0,3).Count);
            }));
            cases.Add(("Road network merges roads and tracks without retaining mutable provider arrays",()=>{
                var roads=new byte[]{32,2,0}; var tracks=new byte[]{0,32,2};
                var network=new OverlandRoadNetwork(roads,tracks,3,1);
                roads[0]=0; tracks[2]=0;
                Check.Equal(3,network.Find(0,0,2,0,10).Count);
                Check.Throws<ArgumentException>(()=>new OverlandRoadNetwork(new byte[3],new byte[2],3,1));
                Check.Throws<ArgumentException>(()=>new OverlandRoadNetwork(null,new byte[3],3,1));
                Check.Equal(0,new OverlandRoadNetwork(new byte[3],new byte[3],3,1).Find(0,0,2,0,10).Count);
            }));
            cases.Add(("Overland routes follow reciprocal road bends instead of crossing empty terrain",()=>{
                // Basic Roads bits: N=128, E=32, S=8, W=2.
                var map=new byte[9]; map[0]=8; map[3]=128|32; map[4]=2|32; map[5]=2|128; map[2]=8;
                var path=OverlandRoads.Find(map,3,3,0,0,2,0,9);
                Check.Equal(5,path.Count);
                Check.Equal(1d,path[1].Z);
                Check.Equal(2d,path[4].X);
                Check.Equal(0d,path[4].Z);
                var back=OverlandRoads.Find(map,3,3,2,0,0,0,9);
                Check.Equal(path.Count,back.Count);
                for(int i=0;i<path.Count;i++) {
                    Check.Equal(path[i].X,back[path.Count-i-1].X);
                    Check.Equal(path[i].Z,back[path.Count-i-1].Z);
                }
            }));
            cases.Add(("Overland roads reject broken links and obey bounded search",()=>{
                Check.Equal(0,OverlandRoads.Find(new byte[]{32,0},2,1,0,0,1,0,2).Count);
                Check.Equal(0,OverlandRoads.Find(new byte[]{32,34,2},3,1,0,0,2,0,1).Count);
                Check.Equal(3,OverlandRoads.Find(new byte[]{32,34,2},3,1,0,0,2,0,3).Count);
            }));
            cases.Add(("Overland roads support diagonal links without wrapping map edges",()=>{
                var map=new byte[]{16,0,0,1};
                Check.Equal(2,OverlandRoads.Find(map,2,2,0,0,1,1,4).Count);
                Check.Equal(0,OverlandRoads.Find(new byte[]{0,32,2,0},2,2,1,0,0,1,4).Count);
            }));
        }
    }
}
