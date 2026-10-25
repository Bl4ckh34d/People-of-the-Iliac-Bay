using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native save data preserves condition progression and a Shield's actual strength across scene changes.
    public sealed class NativeNpcConditionBinding : MonoBehaviour
    {
        [Serializable] public sealed class ConditionPacket
        {
            public double CapturedAt;
            public EntityEffectManager.EffectBundleSaveData_v1[] Bundles;
        }

        private WorldStateStore store;
        private Resident resident;
        private object epoch;
        private EntityEffectManager effects;
        private string lastPacket;

        public static void BindTo(GameObject actor,WorldStateStore world,Resident person)
        {
            var effects=actor.GetComponent<EntityEffectManager>();if(!effects)return;
            var binding=actor.GetComponent<NativeNpcConditionBinding>();
            if(!binding)binding=actor.AddComponent<NativeNpcConditionBinding>();
            binding.store=world;binding.resident=person;binding.effects=effects;
            binding.epoch=world.CaptureNpcVitalsWriteEpoch();
            var progress=world.ReadNpcProgress(person.Id);
            binding.lastPacket=progress==null?null:progress.NativeConditions;
            if(binding.lastPacket!=null)
                effects.RestoreInstancedBundleSaveData(ReadRestorableBundles(binding.lastPacket,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()));
        }

        public void Capture()
        {
            if(store==null || !effects || resident==null || !resident.IsAlive ||
                !store.IsNpcVitalsWriteEpochCurrent(epoch))return;
            if(effects.PoisonCount==0 && effects.DiseaseCount==0 && lastPacket==null &&
                effects.FindIncumbentEffect<Shield>()==null)return;
            var conditions=new List<EntityEffectManager.EffectBundleSaveData_v1>();
            foreach(var bundle in effects.GetInstancedBundlesSaveData())
                if(bundle.bundleType==BundleTypes.Poison || bundle.bundleType==BundleTypes.Disease || IsShield(bundle))conditions.Add(bundle);
            string packet=conditions.Count==0?null:SaveLoadManager.Serialize(
                typeof(ConditionPacket),new ConditionPacket {CapturedAt=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                    Bundles=conditions.ToArray()},false);
            if(store.TryCaptureNpcConditions(epoch,resident.Id,packet))
            {
                lastPacket=packet;
                var vitals=GetComponent<NativeNpcVitalsBinding>();if(vitals)vitals.RebaselineAfterExternalCommit();
            }
        }

        private static bool IsShield(EntityEffectManager.EffectBundleSaveData_v1 bundle)
        {
            // Companion casts admit one exact native effect. Preserve that complete native bundle unchanged.
            return bundle.bundleType==BundleTypes.Spell && bundle.liveEffects!=null && bundle.liveEffects.Length==1 &&
                bundle.liveEffects[0].key==Shield.EffectKey && !bundle.liveEffects[0].effectEnded &&
                bundle.liveEffects[0].roundsRemaining>0;
        }

        private static EntityEffectManager.EffectBundleSaveData_v1[] ReadRestorableBundles(string serialized,double now)
        {
            // Prior condition packets were native poison/disease arrays; their native effects own their clocks.
            if(serialized.TrimStart().StartsWith("[",StringComparison.Ordinal))
                return (EntityEffectManager.EffectBundleSaveData_v1[])SaveLoadManager.Deserialize(
                    typeof(EntityEffectManager.EffectBundleSaveData_v1[]),serialized);
            var packet=(ConditionPacket)SaveLoadManager.Deserialize(typeof(ConditionPacket),serialized);
            if(packet==null || packet.Bundles==null)return null;
            // DFU emits a magic round for every crossed game-minute boundary, including time spent offscreen.
            double roundsPassed=Math.Max(0,Math.Floor(now/60)-Math.Floor(packet.CapturedAt/60));
            var restored=new List<EntityEffectManager.EffectBundleSaveData_v1>();
            foreach(var saved in packet.Bundles)
            {
                var bundle=saved;
                if(IsShield(bundle))
                {
                    if(roundsPassed>=bundle.liveEffects[0].roundsRemaining)continue;
                    bundle.liveEffects[0].roundsRemaining-=(int)roundsPassed;
                }
                else if(bundle.bundleType!=BundleTypes.Poison && bundle.bundleType!=BundleTypes.Disease)continue;
                restored.Add(bundle);
            }
            return restored.ToArray();
        }
    }
}
