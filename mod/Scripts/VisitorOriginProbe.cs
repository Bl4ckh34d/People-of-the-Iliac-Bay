using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class VisitorOriginProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var cancelled=new TravelOriginCatalog.Result();yield return TravelOriginCatalog.Read(cancelled,()=>false);
            if(cancelled.Sites!=null) throw new InvalidOperationException("Cancelled catalog published sites.");
            var catalog=new TravelOriginCatalog.Result();int frame=Time.frameCount;
            yield return TravelOriginCatalog.Read(catalog,()=>true);
            if(catalog.Sites==null || catalog.Sites.Count<100 || Time.frameCount<=frame || game.StreamingWorld.GetCurrentBuildingDirectory()!=directory)
                throw new InvalidOperationException("World origin metadata was not sliced or changed the scene.");
            var target=catalog.Sites.Find(s=>s.Settlement==directory.MapID);
            if(target==null) throw new InvalidOperationException("Loaded city missing from origin catalog.");
            var pool=new VisitorOriginPool(target,catalog.Sites);var reader=DaggerfallUnity.Instance.ContentReader.MapFileReader;
            for(int kind=0;kind<3;kind++)
            {
                VisitorOriginBand band;
                var site=pool.Pick(123,new VisitorOriginWeights(kind==0?100:0,kind==1?100:0,kind==2?100:0),out band);
                if(site==null || (int)band!=kind || site.Settlement==target.Settlement || (site.Region!=target.Region)!=(kind==2))
                    throw new InvalidOperationException("Native origin band selected the wrong region.");
                var location=reader.GetLocation(site.Region,site.LocationIndex);
                if(location.MapTableData.MapId!=site.Settlement) throw new InvalidOperationException("Origin catalog location reference mismatch.");
                var population=Population.CreateFamilies(1,site.Settlement,new[]{new HomeProfile(10,3,180)});
                RegionalIdentityAdapter.Initialize(population,location);
                foreach(var person in population.Households[0].Residents)
                    if(string.IsNullOrEmpty(person.FullName) || person.Race<0) throw new InvalidOperationException("Origin identity unavailable.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VISITOR_ORIGINS_PASS: sites="+catalog.Sites.Count+"; sliced metadata; nearby/distant/foreign native references; source identities; no scene change.");
        }
    }
}
