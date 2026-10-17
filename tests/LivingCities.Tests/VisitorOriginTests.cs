using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class VisitorOriginTests
    {
        private static List<VisitorOriginSite> Sites()
        {var sites=new List<VisitorOriginSite>();for(int i=0;i<7;i++) sites.Add(new VisitorOriginSite{Settlement=i,Region=i<5?0:1,LocationIndex=i,X=i,Z=0});return sites;}
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Visitor origins follow 60 35 5 weights and stable distance bands",()=>{
                var sites=Sites();var pool=new VisitorOriginPool(sites[0],sites,2);var counts=new int[3];
                for(uint i=0;i<10000;i++)
                {
                    VisitorOriginBand band;var site=pool.Pick(StableSeed.Combine(i,2,3,4),new VisitorOriginWeights(),out band);
                    Check.True(site!=null);counts[(int)band]++;
                    Check.True(band==VisitorOriginBand.Nearby ? site.Settlement==1 || site.Settlement==2 :
                        band==VisitorOriginBand.Distant ? site.Settlement==3 || site.Settlement==4 : site.Region==1);
                }
                Check.True(counts[0]>5800 && counts[0]<6200);Check.True(counts[1]>3300 && counts[1]<3700);
                Check.True(counts[2]>400 && counts[2]<600);
                sites.Reverse();var reversed=new VisitorOriginPool(sites[6],sites,2);
                for(uint i=0;i<100;i++) {VisitorOriginBand a,b;Check.Equal(pool.Pick(i,new VisitorOriginWeights(),out a).Settlement,reversed.Pick(i,new VisitorOriginWeights(),out b).Settlement);Check.Equal(a,b);}
            }));
            cases.Add(("Missing origin band is skipped without secretly substituting nearby visitors",()=>{
                var sites=Sites();var pool=new VisitorOriginPool(sites[0],sites.GetRange(0,3),2);VisitorOriginBand band;
                Check.True(pool.Pick(1,new VisitorOriginWeights(0,0,100),out band)==null);Check.Equal(VisitorOriginBand.Foreign,band);
                Check.True(pool.Pick(1,new VisitorOriginWeights(0,100,0),out band)==null);
                Check.True(pool.Pick(1,new VisitorOriginWeights(0,0,0),out band)==null);
                Check.True(pool.Pick(1,new VisitorOriginWeights(100,0,0),out band,new HashSet<int>{1,2})==null);
                Check.Equal(2,pool.Pick(1,new VisitorOriginWeights(100,0,0),out band,new HashSet<int>{1}).Settlement);
            }));
        }
    }
}
