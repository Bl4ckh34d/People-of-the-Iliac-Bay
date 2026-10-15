using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using HarmonyLib;
using UnityEngine;

namespace LivingCities.QuestHooks
{
    public sealed class NativeDefenseHooks : IDisposable
    {
        private const string Owner="local.livingcities.defense-provenance";
        private static NativeDefenseHooks active;
        private readonly Harmony harmony=new Harmony(Owner);
        private static readonly FieldInfo effectManager=typeof(BaseEntityEffect).GetField("manager",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>> begin;
        public NativeDefenseHooks(Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>> begin)
        {
            if(active!=null) throw new InvalidOperationException("Defense hook already installed.");
            this.begin=begin ?? throw new ArgumentNullException(nameof(begin)); active=this;
            try
            {
                var flags=BindingFlags.Static|BindingFlags.NonPublic;
                var finish=new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("Finish",flags));
                harmony.Patch(typeof(WeaponManager).GetMethod("WeaponDamage"),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("Weapon",flags)),finalizer:finish);
                harmony.Patch(typeof(DaggerfallEntityBehaviour).GetMethod("DamageHealthFromSource",new[] {
                    typeof(DaggerfallEntityBehaviour),typeof(int),typeof(bool),typeof(Vector3) }),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("Damage",flags)),finalizer:finish);
                harmony.Patch(typeof(Disintegrate).GetMethod("MagicRound"),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("Disintegrating",flags)),finalizer:finish);
                harmony.Patch(typeof(EnemyAttack).GetMethod("SendDamageToPlayer",BindingFlags.Instance|BindingFlags.NonPublic),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("EnemyHit",flags)),finalizer:finish);
            }
            catch { Dispose(); throw; }
        }
        private static void Weapon(Transform hitTransform,out Action<Exception> __state)
        {
            __state=active!=null && hitTransform ? Observe(hitTransform.GetComponent<DaggerfallEntityBehaviour>(),
                GameManager.Instance.PlayerEntityBehaviour) : null;
        }
        private static void Damage(DaggerfallEntityBehaviour __instance,DaggerfallEntityBehaviour sourceEntityBehaviour,int amount,out Action<Exception> __state)
        { __state=active!=null && amount>0 ? Observe(__instance,sourceEntityBehaviour) : null; }
        private static void EnemyHit(EnemyAttack __instance,int ___damage,out Action<Exception> __state)
        { __state=active!=null && ___damage>0 ? Observe(GameManager.Instance.PlayerEntityBehaviour,__instance.GetComponent<DaggerfallEntityBehaviour>()) : null; }
        private static Action<Exception> Observe(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            try { return active.begin(target,source); }
            catch(Exception error) { Debug.LogWarning("LIVING_CITIES_DEFENSE_OBSERVATION_FAILED: "+error.Message); return null; }
        }
        private static void Disintegrating(Disintegrate __instance,out Action<Exception> __state)
        {
            __state=null;
            try
            {
                var manager=effectManager.GetValue(__instance) as EntityEffectManager;
                if(active!=null && manager) __state=Observe(manager.GetComponent<DaggerfallEntityBehaviour>(),__instance.Caster);
            }
            catch(Exception error) { Debug.LogWarning("LIVING_CITIES_DEFENSE_OBSERVATION_FAILED: "+error.Message); }
        }
        private static Exception Finish(Exception __exception,Action<Exception> __state)
        {
            try { if(__state!=null) __state(__exception); }
            catch(Exception error) { Debug.LogWarning("LIVING_CITIES_DEFENSE_OBSERVATION_FAILED: "+error.Message); }
            return __exception;
        }
        public void Dispose() { if(active!=this) return; harmony.UnpatchAll(Owner); active=null; }
    }
}
