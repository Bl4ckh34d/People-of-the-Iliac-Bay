using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Separate from the cold performance run: native parity reads may warm DFU caches.
    internal static class IncrementalLocationBlocksProbe
    {
        internal static IEnumerator Run(string character)
        {
            if (character != "Hakon Ironhand" || GameManager.Instance.PlayerEntity.Name != character ||
                (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-location-block-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-verify-playtest") < 0))
                throw new InvalidOperationException("Location block parity requires the explicitly owned fixture.");
            var reader = DaggerfallUnity.Instance.ContentReader;
            var directory = GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory) throw new InvalidOperationException("Location block probe requires a loaded city.");
            var locations = new List<DFLocation> { directory.LocationData };
            DFLocation city;
            if (!reader.GetLocation("Daggerfall", "Daggerfall", out city))
                throw new InvalidOperationException("Daggerfall city parity fixture is unavailable.");
            if (city.MapTableData.MapId != directory.MapID) locations.Add(city);
            bool foundStallLocation = false;
            for (int region = 0; region < reader.MapFileReader.RegionCount && !foundStallLocation; region++)
            {
                var metadata = reader.MapFileReader.GetRegion(region);
                for (int index = 0; index < metadata.LocationCount; index++)
                    if (metadata.MapTable[index].MapId == 414355624)
                    {
                        locations.Add(reader.MapFileReader.GetLocation(region, index));
                        foundStallLocation = true;
                        break;
                    }
                yield return null;
            }
            if (!foundStallLocation) throw new InvalidOperationException("Measured slow location is missing from parity corpus.");
            int checkedBlocks = 0;
            foreach (DFLocation location in locations)
            {
                int expectedCount = location.Exterior.ExteriorData.Width * location.Exterior.ExteriorData.Height;
                var cancelled = new IncrementalLocationBlocks.Result();
                var interrupted = IncrementalLocationBlocks.ReadSteps(location, cancelled);
                if (!interrupted.MoveNext() || cancelled.Blocks != null)
                    throw new InvalidOperationException("Incremental loader publishes a partial array or does not yield.");
                (interrupted as IDisposable)?.Dispose();
                if (cancelled.Blocks != null)
                    throw new InvalidOperationException("Disposing incomplete location work publishes partial results.");

                // Retain one source array to detect accidental writes through shared block data.
                string firstName = reader.BlockFileReader.CheckName(reader.MapFileReader.GetRmbBlockName(location, 0, 0));
                DFBlock source;
                if (!reader.GetBlock(firstName, out source)) throw new InvalidOperationException("Source block is unavailable.");
                var sourceArray = source.RmbBlock.FldHeader.BuildingDataList;
                var sourceCopy = (DFLocation.BuildingData[])sourceArray.Clone();
                var actual = new IncrementalLocationBlocks.Result();
                var iterator = IncrementalLocationBlocks.ReadSteps(location, actual);
                int yielded = 0;
                while (iterator.MoveNext())
                {
                    if (iterator.Current != null || actual.Blocks != null || ++yielded > expectedCount)
                        throw new InvalidOperationException("Location loader violates one-block yield or complete-only publication.");
                    yield return null;
                }
                if (yielded != expectedCount || actual.Blocks == null || actual.Blocks.Length != expectedCount)
                    throw new InvalidOperationException("Location loader omits blocks or does not complete after its yields.");
                for (int index = 0; index < sourceCopy.Length; index++)
                    if (!sourceCopy[index].Equals(sourceArray[index]))
                        throw new InvalidOperationException("Location loader modified a shared source building array.");

                // The synchronous reference is used only in this parity process, never the performance sample.
                DFBlock[] expected = RMBLayout.GetLocationBuildingData(location);
                for (int blockIndex = 0; blockIndex < expectedCount; blockIndex++)
                {
                    DFBlock left = expected[blockIndex], right = actual.Blocks[blockIndex];
                    var leftBuildings = left.RmbBlock.FldHeader.BuildingDataList;
                    var rightBuildings = right.RmbBlock.FldHeader.BuildingDataList;
                    if (left.Index != right.Index || left.RmbBlock.SubRecords.Length != right.RmbBlock.SubRecords.Length ||
                        leftBuildings.Length != rightBuildings.Length)
                        throw new InvalidOperationException("Incremental blocks differ from native block order or building counts.");
                    for (int building = 0; building < leftBuildings.Length; building++)
                        if (!leftBuildings[building].Equals(rightBuildings[building]))
                            throw new InvalidOperationException("Incremental building enrichment differs from native data at map " +
                                location.MapTableData.MapId + ", block " + blockIndex + ", building " + building + ".");
                }
                checkedBlocks += expectedCount;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LOCATION_BLOCK_PARITY: map=" + location.MapTableData.MapId +
                    "; blocks=" + expectedCount + "; yields=" + yielded + "; cancellation-discarded=true; source-array-unchanged=true.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LOCATION_BLOCK_PASS: locations=" + locations.Count + "; blocks=" + checkedBlocks +
                "; native-building-parity=true; complete-only-publication=true; cancellation-discarded=true.");
        }
    }
}
