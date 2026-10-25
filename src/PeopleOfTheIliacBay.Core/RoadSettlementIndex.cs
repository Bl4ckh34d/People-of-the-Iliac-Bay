using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    // Metadata candidates only: an inn and its physical connector still need surveying.
    public sealed class RoadSettlementIndex
    {
        public sealed class Candidate {public int RoadNode;public VisitorOriginSite Site;}
        private readonly Dictionary<long,List<VisitorOriginSite>> byPixel=new Dictionary<long,List<VisitorOriginSite>>();
        public RoadSettlementIndex(IEnumerable<VisitorOriginSite> sites)
        {
            if(sites==null) throw new ArgumentNullException(nameof(sites));
            var identities=new HashSet<int>();
            foreach(var site in sites)
            {
                if(site==null || site.Settlement<0 || site.Region<0 || site.LocationIndex<0 ||
                    !Pixel(site.X,1000) || !Pixel(site.Z,500) || !identities.Add(site.Settlement))
                    throw new ArgumentException("Invalid or duplicate road settlement metadata.");
                long key=Key(site.X,site.Z);List<VisitorOriginSite> list;
                if(!byPixel.TryGetValue(key,out list)) byPixel[key]=list=new List<VisitorOriginSite>();
                list.Add(Copy(site));
            }
            foreach(var list in byPixel.Values) list.Sort((a,b)=>a.Settlement.CompareTo(b.Settlement));
        }
        public List<Candidate> Along(IReadOnlyList<RoutePoint> road,int home,int destination)
        {
            if(road==null || road.Count<2 || road.Count>10000 || home<0 || destination<0)
                throw new ArgumentException("Invalid road settlement query.");
            var found=new List<Candidate>();var seen=new HashSet<int>();
            for(int node=1;node<road.Count-1;node++)
            {
                var point=road[node];List<VisitorOriginSite> sites;
                if(!Pixel(point.X,1000) || !Pixel(point.Z,500) || !byPixel.TryGetValue(Key(point.X,point.Z),out sites)) continue;
                foreach(var site in sites)
                    if(site.Settlement!=home && site.Settlement!=destination && seen.Add(site.Settlement))
                        found.Add(new Candidate {RoadNode=node,Site=Copy(site)});
            }
            return found;
        }
        private static bool Pixel(double value,int bound)
        {return !double.IsNaN(value) && !double.IsInfinity(value) && value>=0 && value<bound && value==Math.Floor(value);}
        private static long Key(double x,double z) {return ((long)x<<32)|(uint)z;}
        private static VisitorOriginSite Copy(VisitorOriginSite site)
        {return new VisitorOriginSite {Settlement=site.Settlement,Region=site.Region,LocationIndex=site.LocationIndex,X=site.X,Z=site.Z};}
    }
}
