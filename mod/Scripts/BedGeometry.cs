using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class BedAnchor
    {
        public int RecordIndex;
        public uint ModelId;
        public Vector3 LocalCenter, LocalHeadDirection, LocalSideDirection;
        public float Length, Width;
    }

    public static class BedGeometry
    {
        // Returns the model-space mattress plane, excluding pillows, frames and canopies.
        public static Bounds Mattress(ModelData data)
        {
            if (data.Vertices == null || data.Normals == null || data.Vertices.Length != data.Normals.Length)
                throw new ArgumentException("Bed mesh lacks matching vertices and normals.");
            // On the three supported classic beds, the lowest upward-facing plane
            // is the mattress; higher planes are pillow, frame or canopy surfaces.
            float height = float.PositiveInfinity;
            for (int i = 0; i < data.Vertices.Length; i++)
                if (data.Normals[i].y > 0.99f) height = Mathf.Min(height, data.Vertices[i].y);
            if (float.IsInfinity(height)) throw new ArgumentException("Bed has no horizontal mattress plane.");
            Bounds bounds = new Bounds(); bool found = false;
            for (int i = 0; i < data.Vertices.Length; i++)
                if (data.Normals[i].y > 0.99f && Mathf.Abs(data.Vertices[i].y - height) < 0.0001f)
                {
                    if (!found) { bounds = new Bounds(data.Vertices[i], Vector3.zero); found = true; }
                    else bounds.Encapsulate(data.Vertices[i]);
                }
            if (bounds.size.x < 1.5f || bounds.size.z < 0.7f) throw new ArgumentException("Unsupported mattress dimensions.");
            return bounds;
        }

        public static BedAnchor FromRecord(DFBlock.RmbBlock3dObjectRecord record, int index, ModelData model)
        {
            if (record.ModelIdNum < 41000 || record.ModelIdNum > 41002 || index < 0)
                throw new ArgumentOutOfRangeException(nameof(record));
            Bounds mattress = Mattress(model);
            Vector3 position = new Vector3(record.XPos, -record.YPos, record.ZPos) * MeshReader.GlobalScale;
            if (record.ObjectType == 3)
            {
                float bottom = float.PositiveInfinity;
                foreach (var vertex in model.Vertices) bottom = Mathf.Min(bottom, vertex.y);
                position = new Vector3(record.XPos, record.YPos, record.ZPos) * MeshReader.GlobalScale - Vector3.up * bottom;
            }
            var rotation = Quaternion.Euler(-record.XRotation / BlocksFile.RotationDivisor,
                -record.YRotation / BlocksFile.RotationDivisor, -record.ZRotation / BlocksFile.RotationDivisor);
            var scale = RMBLayout.GetModelScaleVector(record);
            var matrix = Matrix4x4.TRS(position, rotation, scale);
            Vector3 head = matrix.MultiplyVector(Vector3.right), side = matrix.MultiplyVector(Vector3.forward);
            return new BedAnchor { RecordIndex = index, ModelId = record.ModelIdNum,
                LocalCenter = matrix.MultiplyPoint3x4(mattress.center),
                LocalHeadDirection = head.normalized, LocalSideDirection = side.normalized,
                Length = mattress.size.x * head.magnitude, Width = mattress.size.z * side.magnitude };
        }

        public static List<BedAnchor> Read(DaggerfallInterior interior)
        {
            var result = new List<BedAnchor>();
            var dfu = DaggerfallUnity.Instance;
            var door = interior.EntryDoor;
            var block = dfu.ContentReader.BlockFileReader.GetBlock(door.blockIndex);
            var records = block.RmbBlock.SubRecords[door.recordIndex].Interior.Block3dObjectRecords;
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record.ModelIdNum < 41000 || record.ModelIdNum > 41002) continue;
                ModelData model;
                if (!dfu.MeshReader.GetModelData(record.ModelIdNum, out model)) continue;
                result.Add(FromRecord(record, i, model));
            }
            return result;
        }
    }
}
