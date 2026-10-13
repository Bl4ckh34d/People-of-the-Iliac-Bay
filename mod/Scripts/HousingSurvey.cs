using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class HousingSurvey
    {
        public static List<HomeProfile> Read(BuildingDirectory directory)
        {
            var homes = new List<BuildingSummary>();
            for (int kind = (int)DFLocation.BuildingTypes.House1; kind <= (int)DFLocation.BuildingTypes.House6; kind++)
                homes.AddRange(directory.GetBuildingsOfType((DFLocation.BuildingTypes)kind));
            var dfu = DaggerfallUnity.Instance;
            return Read(dfu.ContentReader, directory.LocationData, homes, dfu.MeshReader);
        }
        public static List<HomeProfile> Read(ContentReader reader, DFLocation location,
            IEnumerable<BuildingSummary> homes, MeshReader meshes)
        {
            if (reader == null || !reader.IsReady) throw new ArgumentException("Housing content reader is not ready.");
            if (homes == null) throw new ArgumentNullException(nameof(homes));
            var ordered = new SortedDictionary<int, BuildingSummary>();
            foreach (var home in homes) ordered.Add(home.buildingKey, home);
            var blocks = new Dictionary<int, DFBlock>();
            // The shipped mod compiler exposes DFU's readonly `in DFLocation`
            // argument as ref, unlike the Unity editor compiler. Resolve this API
            // once and invoke only once per block so both runtimes read the same data.
            var blockNameMethod = reader.MapFileReader.GetType().GetMethod("GetRmbBlockName");
            if (blockNameMethod == null) throw new MissingMethodException("DFU block-name API changed.");
            var areas = new Dictionary<uint, double>();
            var result = new List<HomeProfile>();
            foreach (var pair in ordered)
            {
                int x, y, record;
                BuildingDirectory.ReverseBuildingKey(pair.Key, out x, out y, out record);
                int beds = -1;
                if (x >= 0 && y >= 0 && x < location.Exterior.ExteriorData.Width && y < location.Exterior.ExteriorData.Height)
                {
                    DFBlock block;
                    int blockKey = y * location.Exterior.ExteriorData.Width + x;
                    if (!blocks.TryGetValue(blockKey, out block))
                    {
                        string name = reader.BlockFileReader.CheckName((string)blockNameMethod.Invoke(reader.MapFileReader, new object[] { location, x, y }));
                        reader.GetBlock(name, out block); blocks.Add(blockKey, block);
                    }
                    if (block.Type == DFBlock.BlockTypes.Rmb && block.RmbBlock.SubRecords != null &&
                        record >= 0 && record < block.RmbBlock.SubRecords.Length)
                    {
                        var objects = block.RmbBlock.SubRecords[record].Interior.Block3dObjectRecords;
                        if (objects != null)
                        {
                            beds = 0;
                            foreach (var item in objects)
                                if (item.ModelIdNum >= 41000 && item.ModelIdNum <= 41002) beds++;
                        }
                    }
                }
                uint model = pair.Value.ModelID;
                double area;
                if (!areas.TryGetValue(model, out area))
                {
                    ModelData data;
                    area = meshes && meshes.GetModelData(model, out data) ? Footprint(data.Vertices) : 0;
                    areas.Add(model, area);
                }
                result.Add(new HomeProfile(pair.Key, beds, area));
            }
            return result;
        }

        public static double Footprint(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0) return 0;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (var point in vertices)
            {
                if (float.IsNaN(point.x) || float.IsInfinity(point.x) || float.IsNaN(point.z) || float.IsInfinity(point.z)) return 0;
                minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x);
                minZ = Math.Min(minZ, point.z); maxZ = Math.Max(maxZ, point.z);
            }
            return (maxX - minX) * (maxZ - minZ);
        }
    }
}
