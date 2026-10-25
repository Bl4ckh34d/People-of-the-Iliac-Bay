using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SleepArtCatalogTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Sleeping catalogue distinguishes shopkeeper, scribe and exact duplicate flats", () => {
                var keeper = SleepArtCatalog.Find(182, 0);
                var scribe = SleepArtCatalog.Find(184, 2);
                Check.Equal("sleep-topdown-flat-182-0.png.bytes", keeper.File);
                Check.Equal("sleep-topdown-batch-15.png.bytes", scribe.File);
                Check.Equal(5, scribe.Cell);
                Check.Equal(SleepArtCatalog.Find(182,54).File, SleepArtCatalog.Find(182,55).File);
                Check.Equal(SleepArtCatalog.Find(182,54).Cell, SleepArtCatalog.Find(182,55).Cell);
                Check.True(SleepArtCatalog.Find(199,0)==null && SleepArtCatalog.Find(-1,0)==null);
            }));
            cases.Add(("Every previously missing vanilla stationary appearance has its own sleeper", () => {
                var cells = new HashSet<int>();
                for (int record = 5; record <= 12; record++)
                {
                    var entry = SleepArtCatalog.Find(184, record);
                    Check.True(entry != null);
                    Check.Equal("sleep-topdown-missing-00.png.bytes", entry.File);
                    Check.True(cells.Add(entry.Cell));
                }
            }));
            cases.Add(("Every original mobile appearance has explicit sleeping art", () => {
                foreach (int archive in new[] {381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398,451,452,453,454,455,456})
                    Check.True(SleepArtCatalog.Find(archive,5)!=null);
                Check.True(SleepArtCatalog.Find(399,15)!=null);
            }));
            cases.Add(("Imported sleepers require the actual texture provider and retain overlapping archives", () => {
                const string lively = "8445fd1c-7455-4c18-8850-70e1416c5c4f";
                const string resources = "24b43287-5e37-450f-954a-a1e3bbb3bf9e";
                const string replaces = "b4cd53b2-2517-4ed7-9efc-1be21b2643a7";
                var first = SleepArtCatalog.Find(1200,14,lively);
                var second = SleepArtCatalog.Find(1200,14,resources);
                Check.True(first != null && second != null && first.File != second.File);
                Check.True(SleepArtCatalog.Find(1200,14) == null);
                Check.True(SleepArtCatalog.Find(182,33,"unknown-provider") == null);
                Check.True(SleepArtCatalog.Find(182,33,replaces) != null);
                Check.True(SleepArtCatalog.Find(182,33,replaces).File != SleepArtCatalog.Find(182,33).File);
                Check.True(SleepArtCatalog.Find(10015,0,lively) == null);
                Check.True(object.ReferenceEquals(first, SleepArtCatalog.Find(1200,14,lively.ToUpperInvariant())));
                int vanillaCount = 0, providerCount = 0;
                foreach (var entry in SleepArtCatalog.Entries)
                    if (entry.Provider.Length == 0) vanillaCount++; else providerCount++;
                Check.Equal(223,vanillaCount);
                Check.Equal(103,providerCount);
                for (int record = 0; record < 3; record++)
                    Check.True(SleepArtCatalog.Find(1306,record,"e508f8c4-f153-4725-98d4-9dfc2a1fcf9d") == null);
            }));
            cases.Add(("Resident sleepers match native racial outfit selection and child flats", () => {
                int[][] male={new[]{385,386,391,394},new[]{381,382,383,384},new[]{387,388,389,390}};
                int[][] female={new[]{453,454,455,456},new[]{395,396,397,398},new[]{392,393,451,452}};
                for(int race=1;race<=3;race++) for(int variant=0;variant<4;variant++) foreach(var sex in new[]{ResidentSex.Male,ResidentSex.Female}) {
                    var resident=new Resident {Race=race,Sex=sex,AgeGroup=ResidentAgeGroup.Adult,AppearanceSeed=(uint)(variant*2)};
                    var expected=SleepArtCatalog.Find((sex==ResidentSex.Male?male:female)[race-1][variant],5);
                    Check.True(object.ReferenceEquals(expected,SleepArtCatalog.ForResident(resident)));
                }
                var child=new Resident {AgeGroup=ResidentAgeGroup.Child,Sex=ResidentSex.Female};
                Check.True(object.ReferenceEquals(SleepArtCatalog.Find(182,38),SleepArtCatalog.ForResident(child)));
                child.Sex=ResidentSex.Male;
                Check.True(object.ReferenceEquals(SleepArtCatalog.Find(182,43),SleepArtCatalog.ForResident(child)));
                var native=new Resident {NativeIdentity=new NativePerson {TextureArchive=184,TextureRecord=2}};
                Check.True(object.ReferenceEquals(SleepArtCatalog.Find(184,2),SleepArtCatalog.ForResident(native)));
            }));
        }
    }
}
