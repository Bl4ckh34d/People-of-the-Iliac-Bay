using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public sealed partial class GuardActor
    {
        private static readonly Type bundleArrayType=typeof(EntityEffectManager.EffectBundleSaveData_v1[]);
        private static readonly FieldInfo processedMinute=typeof(EntityEffectBroker).GetField("lastGameMinute",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly MethodInfo magicRound=typeof(EntityEffectManager).GetMethod("DoMagicRound",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly MethodInfo removeEnded=typeof(EntityEffectManager).GetMethod("RemovePendingBundles",BindingFlags.Instance|BindingFlags.NonPublic);

        // Use the broker's processed minute, not wall clock time: a global magic
        // round may still be pending later in this frame after actor recreation.
        private static uint ProcessedMagicMinute()
        { return (uint)processedMinute.GetValue(GameManager.Instance.EntityEffectBroker); }

        public void CaptureEffects()
        {
            var guard=roster.Guards.Find(g=>g.Id==GuardId);
            var manager=GetComponent<EntityEffectManager>();
            guard.Effects=!guard.IsAlive || manager.EffectCount==0 ? null : new GuardEffectSnapshot {
                Json=SaveLoadManager.Serialize(bundleArrayType,manager.GetInstancedBundlesSaveData()),
                MagicMinute=ProcessedMagicMinute() };
        }

        private void RestoreEffects()
        {
            var guard=roster.Guards.Find(g=>g.Id==GuardId);
            if(guard.Effects==null || !guard.IsAlive) return;
            var snapshot=guard.Effects;
            var manager=GetComponent<EntityEffectManager>();
            var bundles=(EntityEffectManager.EffectBundleSaveData_v1[])SaveLoadManager.Deserialize(bundleArrayType,snapshot.Json);
            manager.RestoreInstancedBundleSaveData(bundles);
            uint processed=ProcessedMagicMinute();
            // Match DFU's bounded two-day magic catch-up. Poison/disease retain
            // their own native timestamps and catch up inside their effects.
            uint elapsed=processed>=snapshot.MagicMinute ? processed-snapshot.MagicMinute : 0;
            int rounds=(int)Math.Min(2880u,elapsed);
            for(int i=0;i<rounds && guard.IsAlive && manager.EffectCount>0;i++)
            { magicRound.Invoke(manager,null); removeEnded.Invoke(manager,null); }
            if(guard.IsAlive) guard.HealthFraction=(double)entity.CurrentHealth/entity.MaxHealth;
            CaptureEffects();
        }
    }
}
