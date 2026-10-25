using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DaggerfallConnect;
using DaggerfallWorkshop;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // The loaded block, rather than a fresh RMB lookup, preserves the native
    // location-specific world-data replacement chosen during interior layout.
    internal static class NativeInteriorNavigationModules
    {
        private static readonly FieldInfo BlockData=typeof(DaggerfallInterior).GetField(
            "blockData",BindingFlags.Instance|BindingFlags.NonPublic);

        public static string Variant()
        {
            string basis=NativeNavigationWorld.Variant();
            var df=DaggerfallUnity.Instance;
            if(string.IsNullOrEmpty(basis) || !df)return null;
            return basis+";meshColliders="+(df.Option_AddMeshColliders?"1":"0")+
                ";combineRMB="+(df.Option_CombineRMB?"1":"0");
        }

        public static bool TryGetBlock(DaggerfallInterior interior,out DFBlock block)
        {
            block=default(DFBlock);
            if(!interior || BlockData==null)return false;
            block=(DFBlock)BlockData.GetValue(interior);
            return block.Type==DFBlock.BlockTypes.Rmb && block.Index==interior.EntryDoor.blockIndex &&
                InteriorKey(block,interior.EntryDoor.recordIndex)!=null;
        }

        // AddModels uses these native fields to place static collision models.
        // Block index also selects native corrections in IsBadInteriorModel.
        public static string InteriorKey(DFBlock block,int recordIndex)
        {
            if(block.Type!=DFBlock.BlockTypes.Rmb || string.IsNullOrEmpty(block.Name) ||
                !block.Name.EndsWith(".RMB",StringComparison.OrdinalIgnoreCase) || recordIndex<0 ||
                block.RmbBlock.SubRecords==null || recordIndex>=block.RmbBlock.SubRecords.Length)return null;
            var models=block.RmbBlock.SubRecords[recordIndex].Interior.Block3dObjectRecords;
            if(models==null || models.Length==0)return null;
            byte[] bytes;
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream))
                {
                    writer.Write(1);writer.Write(block.Index);writer.Write(models.Length);
                    foreach(var model in models)
                    {
                        writer.Write(model.ModelIdNum);writer.Write(model.ObjectType);
                        writer.Write(model.XPos);writer.Write(model.YPos);writer.Write(model.ZPos);
                        writer.Write(model.XScale);writer.Write(model.YScale);writer.Write(model.ZScale);
                        writer.Write(model.XRotation);writer.Write(model.YRotation);writer.Write(model.ZRotation);
                    }
                    writer.Flush();bytes=stream.ToArray();
                }
            }
            byte[] digest;
            using(var sha=SHA256.Create())digest=sha.ComputeHash(bytes);
            return "interior-template:"+block.Name.ToUpperInvariant()+":"+
                recordIndex.ToString(CultureInfo.InvariantCulture)+"#"+
                BitConverter.ToString(digest).Replace("-","").ToLowerInvariant();
        }

        // The world loader calls this only when no exact scene capture matches.
        // There is intentionally no fallback to a matching RMB name alone.
        public static bool TrySelect(Transform root,string scene,NativeNavigationManifest manifest,
            out List<NativeNavigationModule> selected,out string reason)
        {
            selected=new List<NativeNavigationModule>();reason=null;
            if(!root || string.IsNullOrEmpty(scene) || !scene.StartsWith("interior-",StringComparison.Ordinal) ||
                manifest==null || manifest.modules==null || manifest.profile!=NativeNavigationWorld.Profile())
            {reason="scene-or-profile";return false;}
            if((root.lossyScale-Vector3.one).sqrMagnitude>0.000001f)
            {reason="interior-transform";return false;}
            var interior=root.GetComponent<DaggerfallInterior>();
            DFBlock block;
            if(!TryGetBlock(interior,out block))
            {reason="loaded-interior-record-missing";return false;}
            string key=InteriorKey(block,interior.EntryDoor.recordIndex);
            string variant=Variant();
            if(string.IsNullOrEmpty(variant)){reason="variant-unavailable";return false;}
            NativeNavigationModule template=null;
            foreach(var module in manifest.modules)
            {
                if(module==null || module.scene!=key || module.variant!=variant)continue;
                if(template!=null){reason="duplicate-interior-template:"+key;return false;}
                template=module;
            }
            if(template==null){reason="missing-interior-record-variant:"+key;return false;}
            selected.Add(new NativeNavigationModule {scene=scene,variant=variant,asset=template.asset,
                geometry=template.geometry,position=template.position,size=template.size});
            return true;
        }
    }
}
