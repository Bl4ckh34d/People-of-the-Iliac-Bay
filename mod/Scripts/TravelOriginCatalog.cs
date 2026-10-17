using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using LivingCities.Core;
namespace LivingCities.DFU
{
    // Lightweight map metadata only. No settlement scene or household is created.
    public static class TravelOriginCatalog
    {
        public sealed class Result {public List<VisitorOriginSite> Sites;}
        private static List<VisitorOriginSite> cached;
        public static IEnumerator Read(Result result,Func<bool> valid)
        {
            if(result==null || valid==null) throw new ArgumentNullException();
            result.Sites=null;if(!valid()) yield break;
            if(cached!=null) {result.Sites=cached;yield break;}
            yield return BackgroundWork.Wait(valid);if(!valid()) yield break;
            var reader=DaggerfallUnity.Instance.ContentReader.MapFileReader;var sites=new List<VisitorOriginSite>();
            for(int region=0;region<reader.RegionCount;region++)
            {
                if(!valid()) yield break;var data=reader.GetRegion(region);
                for(int i=0;i<data.LocationCount;i++)
                {
                    var entry=data.MapTable[i];
                    if(SettlementClassification.SupportsPopulation((int)entry.LocationType))
                    {
                        var point=MapsFile.LongitudeLatitudeToMapPixel(entry.Longitude,entry.Latitude);
                        sites.Add(new VisitorOriginSite {Settlement=entry.MapId,Region=region,LocationIndex=i,X=point.X,Z=point.Y});
                    }
                    if(i%256==255) {yield return null;if(!valid()) yield break;}
                }
                yield return null;
            }
            if(valid()) {cached=sites;result.Sites=cached;}
        }
    }
}
