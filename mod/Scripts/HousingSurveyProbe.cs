using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class HousingSurveyProbe
    {
        internal static string Run()
        {
            if(!RuntimeTiming.Enabled)throw new InvalidOperationException("Housing probe requires read counters.");
            var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory)throw new InvalidOperationException("Housing probe requires a loaded native city.");
            var blocks=RMBLayout.GetLocationBuildingData(directory.LocationData);
            var field=typeof(RMBLayout).GetField("locationCache",BindingFlags.Static|BindingFlags.NonPublic);
            var cache=field==null?null:field.GetValue(null) as List<KeyValuePair<int,DFBlock[]>>;
            if(cache==null || !cache.Exists(row=>row.Key==directory.MapID && object.ReferenceEquals(row.Value,blocks)))
                throw new InvalidOperationException("Housing probe requires the native player-equivalent location cache.");
            int before=RuntimeTiming.Calls("housing-block-read");
            var cached=HousingSurvey.Read(directory);
            int repeated=RuntimeTiming.Calls("housing-block-read")-before;
            if(repeated!=0)throw new InvalidOperationException("Housing survey redecoded "+repeated+" blocks already in DFU's cache.");
            if(cached.Count==0)throw new InvalidOperationException("Housing probe has no homes to compare.");
            var savedCache=new List<KeyValuePair<int,DFBlock[]>>(cache);
            List<HomeProfile> uncached;
            try
            {
                RMBLayout.ClearLocationCache();
                before=RuntimeTiming.Calls("housing-block-read");
                uncached=HousingSurvey.Read(directory);
                if(RuntimeTiming.Calls("housing-block-read")<=before)
                    throw new InvalidOperationException("Housing survey retained stale private blocks after native invalidation.");
            }
            finally { cache.Clear();cache.AddRange(savedCache); }
            if(cached.Count!=uncached.Count)throw new InvalidOperationException("Housing cache changed home count.");
            for(int i=0;i<cached.Count;i++)
            {
                var a=cached[i];var b=uncached[i];
                if(a.BuildingKey!=b.BuildingKey || a.BedCount!=b.BedCount || a.FootprintArea!=b.FootprintArea ||
                    a.AvailableForResidents!=b.AvailableForResidents)
                    throw new InvalidOperationException("Cached housing differs from raw native housing at "+a.BuildingKey);
            }
            // Population generation depends on every profile field, so exact
            // profile parity also preserves its seeded inputs. No save is changed.
            return "PEOPLE_OF_THE_ILIAC_BAY_HOUSING_CACHE_PASS: homes="+cached.Count+
                "; repeatedBlockReads=0; rawProfileParity=true; nativeInvalidationFallback=true";
        }
    }
}
