using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoadSettlementIndexTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Road settlement candidates follow actual route order and exclude off-road origins",()=>{
                var sites=new[]{new VisitorOriginSite{Settlement=1,X=0,Z=0},new VisitorOriginSite{Settlement=2,X=1,Z=0},
                    new VisitorOriginSite{Settlement=3,X=2,Z=0},new VisitorOriginSite{Settlement=4,X=3,Z=0},
                    new VisitorOriginSite{Settlement=5,X=1,Z=1}};
                var index=new RoadSettlementIndex(sites);sites[1].Settlement=99;
                var road=new List<RoutePoint>{new RoutePoint(.1,0),new RoutePoint(1,0),new RoutePoint(2,0),new RoutePoint(2.9,0)};
                var found=index.Along(road,1,4);Check.Equal(2,found.Count);
                Check.Equal(2,found[0].Site.Settlement);Check.Equal(1,found[0].RoadNode);Check.Equal(3,found[1].Site.Settlement);
                road.Reverse();found=index.Along(road,4,1);Check.Equal(3,found[0].Site.Settlement);Check.Equal(2,found[1].Site.Settlement);
                found[0].Site.Settlement=77;Check.Equal(3,index.Along(road,4,1)[0].Site.Settlement);
            }));
            cases.Add(("Road settlement index rejects ambiguous metadata and never rounds a road point onto a town",()=>{
                var site=new VisitorOriginSite{Settlement=2,X=1,Z=0};var index=new RoadSettlementIndex(new[]{site});
                Check.Equal(0,index.Along(new[]{new RoutePoint(0,0),new RoutePoint(1.1,0),new RoutePoint(2,0)},1,3).Count);
                Check.Throws<ArgumentException>(()=>new RoadSettlementIndex(new[]{site,site}));
                Check.Throws<ArgumentException>(()=>new RoadSettlementIndex(new[]{new VisitorOriginSite{Settlement=1,X=1.5,Z=0}}));
            }));
        }
    }
}
