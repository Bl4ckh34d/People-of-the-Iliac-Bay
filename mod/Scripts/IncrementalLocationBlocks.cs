// Building enrichment adapted from Daggerfall Unity RMBLayout.cs.
// Copyright (C) 2009-2023 Daggerfall Workshop.
// https://github.com/Interkarma/daggerfall-unity
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;

namespace PeopleOfTheIliacBay.DFU
{
    // Incremental equivalent of RMBLayout.GetLocationBuildingData. Keep this
    // result operation-scoped: DFU owns variant invalidation for its private cache.
    internal static class IncrementalLocationBlocks
    {
        private const int MaximumSlowDiagnosticsPerRead = 12;

        internal sealed class Result
        {
            public DFBlock[] Blocks;
        }

        private sealed class NamedBuildingPoolItem
        {
            public DFLocation.BuildingData BuildingData;
            public bool Used;
        }

        internal static IEnumerator ReadSteps(DFLocation location, Result result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Blocks = null;

            int width = location.Exterior.ExteriorData.Width;
            int height = location.Exterior.ExteriorData.Height;
            if (width < 1 || width > 8 || height < 1 || height > 8)
                throw new ArgumentException("Invalid settlement dimensions.");

            ContentReader contentReader = DaggerfallUnity.Instance == null ? null : DaggerfallUnity.Instance.ContentReader;
            if (contentReader == null || !contentReader.IsReady || contentReader.BlockFileReader == null || contentReader.MapFileReader == null)
                throw new InvalidOperationException("Incremental location load requires a ready ContentReader.");

            var namedBuildingPool = new List<NamedBuildingPoolItem>();
            for (int i = 0; i < location.Exterior.BuildingCount; i++)
            {
                DFLocation.BuildingData building = location.Exterior.Buildings[i];
                if (!RMBLayout.IsNamedBuilding(building.BuildingType)) continue;
                namedBuildingPool.Add(new NamedBuildingPoolItem { BuildingData = building, Used = false });
            }

            var blocks = new List<DFBlock>(width * height);
            int slowDiagnosticCount = 0;
            int mapId = location.MapTableData.MapId;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    long nameStarted = RuntimeTiming.Start();
                    string blockName = contentReader.BlockFileReader.CheckName(
                        contentReader.MapFileReader.GetRmbBlockName(location, x, y));
                    slowDiagnosticCount = RecordTiming("block-name", mapId, x, y, blockName, -1, nameStarted, slowDiagnosticCount);

                    long blockReadStarted = RuntimeTiming.Start();
                    DFBlock block;
                    if (!contentReader.GetBlock(blockName, out block))
                        throw new InvalidOperationException("Incremental location load could not read block " + blockName);
                    slowDiagnosticCount = RecordTiming("block-read", mapId, x, y, blockName, block.Index, blockReadStarted, slowDiagnosticCount);

                    long enrichmentStarted = RuntimeTiming.Start();
                    DFLocation.BuildingData[] sourceBuildings = block.RmbBlock.FldHeader.BuildingDataList;
                    if (sourceBuildings == null)
                        throw new InvalidOperationException("Incremental location block has no building data: " + blockName);
                    var buildingArray = new DFLocation.BuildingData[sourceBuildings.Length];
                    Array.Copy(sourceBuildings, buildingArray, sourceBuildings.Length);
                    block.RmbBlock.FldHeader.BuildingDataList = buildingArray;

                    BuildingReplacementData buildingReplacementData;
                    for (int i = 0; i < block.RmbBlock.SubRecords.Length; i++)
                    {
                        DFLocation.BuildingData building = block.RmbBlock.FldHeader.BuildingDataList[i];
                        if (!RMBLayout.IsNamedBuilding(building.BuildingType)) continue;

                        NamedBuildingPoolItem item;
                        if (!GetNextBuildingFromPool(namedBuildingPool, building.BuildingType, out item))
                        {
                            UnityEngine.Debug.LogFormat("End of city building list reached without finding building type {0} in location {1}.{2}",
                                building.BuildingType, location.RegionName, location.Name);
                        }

                        building.NameSeed = item.BuildingData.NameSeed;
                        building.FactionId = item.BuildingData.FactionId;
                        building.Sector = item.BuildingData.Sector;
                        building.LocationId = item.BuildingData.LocationId;
                        building.Quality = item.BuildingData.Quality;

                        if (WorldDataReplacement.GetBuildingReplacementData(blockName, block.Index, i, out buildingReplacementData))
                        {
                            if (buildingReplacementData.FactionId != 0)
                            {
                                item.Used = false;
                                building.FactionId = buildingReplacementData.FactionId;
                                building.Quality = buildingReplacementData.Quality;
                                building.NameSeed = (ushort)(buildingReplacementData.NameSeed + location.LocationIndex);
                            }
                            building.BuildingType = (DFLocation.BuildingTypes)buildingReplacementData.BuildingType;
                        }

                        if (block.RmbBlock.FldHeader.OtherNames != null &&
                            block.RmbBlock.FldHeader.OtherNames[i] == "KRAVE01.HS2")
                        {
                            building.BuildingType = DFLocation.BuildingTypes.GuildHall;
                            building.FactionId = 414;
                        }

                        block.RmbBlock.FldHeader.BuildingDataList[i] = building;
                    }

                    blocks.Add(block);
                    slowDiagnosticCount = RecordTiming("building-enrichment", mapId, x, y, blockName, block.Index,
                        enrichmentStarted, slowDiagnosticCount);
                    yield return null;
                }
            }

            // A consumer never observes an incomplete set. Disposing this
            // iterator before this point leaves result.Blocks null.
            result.Blocks = blocks.ToArray();
        }

        private static bool GetNextBuildingFromPool(List<NamedBuildingPoolItem> namedBuildingPool,
            DFLocation.BuildingTypes buildingType, out NamedBuildingPoolItem itemOut)
        {
            itemOut = new NamedBuildingPoolItem();
            for (int i = 0; i < namedBuildingPool.Count; i++)
            {
                if (!namedBuildingPool[i].Used && namedBuildingPool[i].BuildingData.BuildingType == buildingType)
                {
                    itemOut = namedBuildingPool[i];
                    itemOut.Used = true;
                    return true;
                }
            }
            return false;
        }

        private static int RecordTiming(string phase, int mapId, int blockX, int blockY, string blockName,
            int blockIndex, long started, int slowDiagnosticCount)
        {
            if (!RuntimeTiming.Enabled || started == 0) return slowDiagnosticCount;
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            RuntimeTiming.RecordDuration("incremental-location-blocks:" + phase, started);
            if (elapsed < 30 || slowDiagnosticCount >= MaximumSlowDiagnosticsPerRead) return slowDiagnosticCount;
            slowDiagnosticCount++;
            UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INCREMENTAL_BLOCKS_SLOW: phase=" + phase +
                "; mapId=" + mapId.ToString(CultureInfo.InvariantCulture) +
                "; block=" + blockX.ToString(CultureInfo.InvariantCulture) + "," + blockY.ToString(CultureInfo.InvariantCulture) +
                "; blockIndex=" + blockIndex.ToString(CultureInfo.InvariantCulture) +
                "; blockName=" + blockName +
                "; ms=" + elapsed.ToString("F2", CultureInfo.InvariantCulture));
            return slowDiagnosticCount;
        }
    }
}
