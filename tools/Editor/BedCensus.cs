using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaggerfallConnect;
using DaggerfallWorkshop.Utility;
using UnityEngine;

// Read-only census of original assets. Not included in the runtime mod bundle.
public static class BedCensus
{
    private sealed class Row
    {
        public string Block, Type;
        public int Record, Beds, RestMarkers, Uses, TestTownUses;
    }
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var reader = new ContentReader(Path.Combine(root, "GameFiles/arena2"));
        if (!reader.IsReady) throw new InvalidOperationException("Original assets unavailable.");
        var maps = reader.MapFileReader;
        var blocks = reader.BlockFileReader;
        var cache = new Dictionary<string, List<Row>>(StringComparer.OrdinalIgnoreCase);
        int settlements = 0;
        for (int r = 0; r < maps.RegionCount; r++)
        {
            DFRegion region = maps.GetRegion(r);
            for (int l = 0; l < region.LocationCount; l++)
            {
                var locationType = region.MapTable[l].LocationType;
                if (locationType != DFRegion.LocationTypes.TownCity && locationType != DFRegion.LocationTypes.TownHamlet &&
                    locationType != DFRegion.LocationTypes.TownVillage) continue;
                DFLocation location = maps.GetLocation(r, l);
                settlements++;
                bool testTown = location.RegionName == "Daggerfall" && location.Name == "Gothway Garden";
                for (int y = 0; y < location.Exterior.ExteriorData.Height; y++)
                for (int x = 0; x < location.Exterior.ExteriorData.Width; x++)
                {
                    string name = blocks.CheckName(maps.GetRmbBlockName(location, x, y));
                    List<Row> rows;
                    if (!cache.TryGetValue(name, out rows))
                    {
                        DFBlock block = blocks.GetBlock(name);
                        if (block.Type != DFBlock.BlockTypes.Rmb) throw new InvalidOperationException("Unreadable city block: " + name);
                        rows = new List<Row>();
                        for (int i = 0; i < block.RmbBlock.SubRecords.Length; i++)
                        {
                            var interior = block.RmbBlock.SubRecords[i].Interior;
                            var type = block.RmbBlock.FldHeader.BuildingDataList[i].BuildingType;
                            // Catalog labels these three models as Beds. Count meshes,
                            // not occupants; bed sharing is an independent policy.
                            int beds = interior.Block3dObjectRecords.Count(o => o.ModelIdNum >= 41000 && o.ModelIdNum <= 41002);
                            int rest = interior.BlockFlatObjectRecords.Count(o => o.TextureArchive == 199 && o.TextureRecord == 4);
                            rows.Add(new Row { Block = name, Record = i, Type = type.ToString(), Beds = beds, RestMarkers = rest });
                        }
                        cache.Add(name, rows);
                    }
                    foreach (Row row in rows) { row.Uses++; if (testTown) row.TestTownUses++; }
                }
            }
        }
        string output = Path.Combine(root, "LivingCities/work/bed-census.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var lines = new List<string> { "block,record,type,bedModels,restMarkers,settlementOccurrences,gothwayOccurrences" };
        foreach (Row row in cache.Values.SelectMany(v => v).OrderBy(v => v.Block).ThenBy(v => v.Record))
            lines.Add(string.Join(",", row.Block, row.Record, row.Type, row.Beds, row.RestMarkers, row.Uses, row.TestTownUses));
        File.WriteAllLines(output, lines);
        Debug.Log("LIVING_CITIES_BED_CENSUS_PASS: settlements=" + settlements + "; blocks=" + cache.Count + "; interiors=" + (lines.Count - 1));
    }
}
