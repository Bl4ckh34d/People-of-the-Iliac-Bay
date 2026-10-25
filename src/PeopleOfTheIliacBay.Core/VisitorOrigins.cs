using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public enum VisitorOriginBand { Nearby, Distant, Foreign }
    public sealed class VisitorOriginSite
    {
        public int Settlement,Region,LocationIndex;
        public double X,Z;
    }
    public sealed class VisitorOriginWeights
    {
        public readonly int Nearby,Distant,Foreign;
        public VisitorOriginWeights(int nearby=60,int distant=35,int foreign=5)
        {
            if(nearby<0 || nearby>100 || distant<0 || distant>100 || foreign<0 || foreign>100) throw new ArgumentException("Invalid origin weights.");
            Nearby=nearby;Distant=distant;Foreign=foreign;
        }
    }
    public sealed class VisitorOriginPool
    {
        private readonly List<VisitorOriginSite>[] groups={new List<VisitorOriginSite>(),new List<VisitorOriginSite>(),new List<VisitorOriginSite>()};
        private static VisitorOriginSite Copy(VisitorOriginSite site)
        {
            if(site==null || site.Settlement<0 || site.Region<0 || site.LocationIndex<0 || double.IsNaN(site.X) || double.IsInfinity(site.X) ||
                double.IsNaN(site.Z) || double.IsInfinity(site.Z)) throw new ArgumentException("Invalid origin site.");
            return new VisitorOriginSite {Settlement=site.Settlement,Region=site.Region,LocationIndex=site.LocationIndex,X=site.X,Z=site.Z};
        }
        public VisitorOriginPool(VisitorOriginSite destination,IEnumerable<VisitorOriginSite> sites,int nearbyCount=16)
        {
            var target=Copy(destination);
            if(sites==null || nearbyCount<1) throw new ArgumentException("Invalid origin catalog.");
            var local=new List<VisitorOriginSite>();var ids=new HashSet<int>();
            foreach(var candidate in sites)
            {
                var site=Copy(candidate);if(!ids.Add(site.Settlement)) throw new ArgumentException("Duplicate origin site.");
                if(site.Settlement==target.Settlement) continue;
                if(site.Region==target.Region) local.Add(site);else groups[2].Add(site);
            }
            local.Sort((a,b)=>{
                double ax=a.X-target.X,az=a.Z-target.Z,bx=b.X-target.X,bz=b.Z-target.Z;
                int order=(ax*ax+az*az).CompareTo(bx*bx+bz*bz);return order!=0 ? order : a.Settlement.CompareTo(b.Settlement);
            });
            for(int i=0;i<local.Count;i++) groups[i<nearbyCount?0:1].Add(local[i]);
            groups[2].Sort((a,b)=>a.Settlement.CompareTo(b.Settlement));
        }
        public VisitorOriginSite Pick(uint seed,VisitorOriginWeights weights,out VisitorOriginBand band,ISet<int> excluded=null)
        {
            if(weights==null) throw new ArgumentNullException(nameof(weights));
            band=VisitorOriginBand.Nearby;int total=weights.Nearby+weights.Distant+weights.Foreign;if(total==0) return null;
            int roll=(int)(seed%(uint)total);
            band=roll<weights.Nearby ? VisitorOriginBand.Nearby : roll<weights.Nearby+weights.Distant ? VisitorOriginBand.Distant : VisitorOriginBand.Foreign;
            var pool=groups[(int)band];int count=0;
            foreach(var site in pool) if(excluded==null || !excluded.Contains(site.Settlement)) count++;
            if(count==0) return null;
            int chosen=(int)(StableSeed.Combine(seed,0x6f726967,0,0)%(uint)count);
            foreach(var site in pool) if(excluded==null || !excluded.Contains(site.Settlement))
                if(chosen--==0) return Copy(site);
            return null;
        }
    }
}
