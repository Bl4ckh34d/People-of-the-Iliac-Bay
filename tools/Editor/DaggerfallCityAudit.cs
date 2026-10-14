using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using LivingCities.DFU;
using UnityEngine;

// Read-only shipped-content census; deliberately does not enable runtime actors.
public static class DaggerfallCityAudit
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var reader = new ContentReader(Path.Combine(root, "GameFiles/arena2"));
        DFLocation location;
        if (!reader.GetLocation("Daggerfall", "Daggerfall", out location))
            throw new InvalidOperationException("Daggerfall city content missing.");
        var counts = new SortedDictionary<string, int>();
        int homeBeds = 0, tavernBeds = 0, homes = 0;
        for (int y = 0; y < location.Exterior.ExteriorData.Height; y++)
        for (int x = 0; x < location.Exterior.ExteriorData.Width; x++)
        {
            string name = reader.BlockFileReader.CheckName(reader.MapFileReader.GetRmbBlockName(location, x, y));
            var block = reader.BlockFileReader.GetBlock(name);
            foreach (var building in RMBLayout.GetBuildingData(block, x, y))
            {
                string kind = building.BuildingType.ToString();
                if (!counts.ContainsKey(kind)) counts[kind] = 0;
                counts[kind]++;
                if (building.BuildingType == DFLocation.BuildingTypes.Town23)
                {
                    int bx, by, record;
                    DaggerfallWorkshop.Game.BuildingDirectory.ReverseBuildingKey(building.buildingKey, out bx, out by, out record);
                    var interior = block.RmbBlock.SubRecords[record].Interior;
                    Debug.Log("LIVING_CITIES_WALL_CANDIDATE: key=" + building.buildingKey + "; block=" + name +
                        "; model=" + building.ModelID + "; interiorObjects=" +
                        (interior.Block3dObjectRecords == null ? 0 : interior.Block3dObjectRecords.Length));
                }
                bool home = building.BuildingType >= DFLocation.BuildingTypes.House1 &&
                    building.BuildingType <= DFLocation.BuildingTypes.House6;
                bool tavern = building.BuildingType == DFLocation.BuildingTypes.Tavern;
                if (!home && !tavern) continue;
                var profile = HousingSurvey.Read(reader, location, new[] { building }, null)[0];
                if (profile.BedCount < 0) throw new InvalidOperationException("Unknown bed census: " + building.buildingKey);
                if (home) { homes++; homeBeds += profile.BedCount; }
                else tavernBeds += profile.BedCount;
            }
        }
        Debug.Log("LIVING_CITIES_DAGGERFALL_CENSUS: map=" + location.MapTableData.MapId +
            "; type=" + location.MapTableData.LocationType + "; blocks=" + location.Exterior.ExteriorData.Width +
            "x" + location.Exterior.ExteriorData.Height + "; homes=" + homes + "; homeBeds=" + homeBeds + "; tavernBeds=" + tavernBeds);
        foreach (var pair in counts) Debug.Log("LIVING_CITIES_DAGGERFALL_BUILDINGS: " + pair.Key + "=" + pair.Value);
    }
}
