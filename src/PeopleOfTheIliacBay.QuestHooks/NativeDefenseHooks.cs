using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using HarmonyLib;
using UnityEngine;

namespace PeopleOfTheIliacBay.QuestHooks
{
    public sealed class NativeDefenseHooks : IDisposable
    {
        private const string Owner="local.peopleoftheiliacbay.defense-provenance";
        private static NativeDefenseHooks active;
        private readonly Harmony harmony=new Harmony(Owner);
        private static readonly FieldInfo effectManager=typeof(BaseEntityEffect).GetField("manager",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>> begin;
        private readonly Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>> healing;
        private readonly Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,string,Action<Exception>> hostileMagic;
        private readonly Func<EnemyAttack,DaggerfallUnityItem,Vector3,bool,int?> civilianAttack;
        public NativeDefenseHooks(Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>> begin,
            Func<EnemyAttack,DaggerfallUnityItem,Vector3,bool,int?> civilianAttack,
            Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>> healing,
            Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,string,Action<Exception>> hostileMagic)
        {
            if(active!=null) throw new InvalidOperationException("Defense hook already installed.");
            this.begin=begin ?? throw new ArgumentNullException(nameof(begin)); active=this;
            this.civilianAttack=civilianAttack ?? throw new ArgumentNullException(nameof(civilianAttack));
            this.healing=healing ?? throw new ArgumentNullException(nameof(healing));
            this.hostileMagic=hostileMagic ?? throw new ArgumentNullException(nameof(hostileMagic));
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
                harmony.Patch(typeof(HealHealth).GetMethod("MagicRound"),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("Healing",flags)),finalizer:finish);
                harmony.Patch(typeof(BaseEntityEffect).GetMethod("PlayerAggro",BindingFlags.Instance|BindingFlags.NonPublic),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("HostileMagicAttempt",flags)));
                var effectSource=new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("NonHealthEffectSource",flags));
                var endEffectSource=new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("EndNonHealthEffectSource",flags));
                harmony.Patch(typeof(DaggerfallEntityBehaviour).GetMethod("DamageFatigueFromSource",new[] {
                    typeof(IEntityEffect),typeof(int),typeof(bool)}),prefix:effectSource,finalizer:endEffectSource);
                harmony.Patch(typeof(DaggerfallEntityBehaviour).GetMethod("DamageMagickaFromSource",new[] {
                    typeof(IEntityEffect),typeof(int)}),prefix:effectSource,finalizer:endEffectSource);
                harmony.Patch(typeof(DaggerfallEntityBehaviour).GetMethod("DamageHealthFromSource",new[] {
                    typeof(IEntityEffect),typeof(int),typeof(bool),typeof(Vector3)}),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("ZeroHealthEffectSource",flags)),finalizer:endEffectSource);
                harmony.Patch(typeof(DaggerfallEntityBehaviour).GetMethod("HandleAttackFromSource",new[] {
                    typeof(DaggerfallEntityBehaviour)}),prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("NonHealthMagicAttack",flags)));
                harmony.Patch(typeof(EnemyAttack).GetMethod("SendDamageToPlayer",BindingFlags.Instance|BindingFlags.NonPublic),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("EnemyHit",flags)),finalizer:finish);
                harmony.Patch(typeof(EnemyAttack).GetMethod("ApplyDamageToNonPlayer",BindingFlags.Instance|BindingFlags.NonPublic),
                    prefix:new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("EnemyHitNonPlayer",flags)),finalizer:finish);
                var civilianFormula=new HarmonyMethod(typeof(NativeDefenseHooks).GetMethod("CivilianFormula",flags));
                harmony.Patch(typeof(FormulaHelper).GetMethod("CalculateAdjustmentsToHit"),transpiler:civilianFormula);
                harmony.Patch(typeof(FormulaHelper).GetMethod("CalculateWeaponAttackDamage"),transpiler:civilianFormula);
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
        private static bool EnemyHitNonPlayer(EnemyAttack __instance,DaggerfallUnityItem weapon,Vector3 direction,
            bool bowAttack,ref int __result,out Action<Exception> __state)
        {
            __state=null;
            if(active!=null)
            {
                // Civilian damage uses DamageHealthFromSource, observed by its own hook.
                var damage=active.civilianAttack(__instance,weapon,direction,bowAttack);
                if(damage.HasValue) {__result=damage.Value;return false;}
            }
            var senses=__instance.GetComponent<EnemySenses>();
            __state=active!=null && senses ? Observe(senses.Target,__instance.GetComponent<DaggerfallEntityBehaviour>()) : null;
            return true;
        }
        // Native formulas already accept DaggerfallEntity, but these two human/
        // monster checks dereference a failed EnemyEntity cast for civilians.
        // Keep every calculation and mod override; only make those checks null-safe.
        private static IEnumerable<CodeInstruction> CivilianFormula(IEnumerable<CodeInstruction> instructions)
        {
            int changed=0;
            var type=typeof(EnemyEntity).GetProperty("EntityType").GetGetMethod();
            var career=typeof(EnemyEntity).GetProperty("CareerIndex").GetGetMethod();
            foreach(var instruction in instructions)
            {
                string method=instruction.Calls(type)?"CombatEntityType":instruction.Calls(career)?"CombatCareerIndex":null;
                if(method!=null)
                {
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=typeof(NativeDefenseHooks).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic);
                    changed++;
                }
                yield return instruction;
            }
            if(changed!=1)throw new InvalidOperationException("Native civilian combat formula boundary changed.");
        }
        private static EntityTypes CombatEntityType(EnemyEntity entity)
        {return entity==null?EntityTypes.CivilianNPC:entity.EntityType;}
        private static int CombatCareerIndex(EnemyEntity entity)
        {return entity==null?-1:entity.CareerIndex;}
        private static Action<Exception> Observe(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            try { return active.begin(target,source); }
            catch(Exception error) { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DEFENSE_OBSERVATION_FAILED: "+error.Message); return null; }
        }
        private static void Disintegrating(Disintegrate __instance,out Action<Exception> __state)
        {
            __state=null;
            try
            {
                var manager=effectManager.GetValue(__instance) as EntityEffectManager;
                if(active!=null && manager) __state=Observe(manager.GetComponent<DaggerfallEntityBehaviour>(),__instance.Caster);
            }
            catch(Exception error) { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DEFENSE_OBSERVATION_FAILED: "+error.Message); }
        }
        private static void Healing(HealHealth __instance,out Action<Exception> __state)
        {
            __state=null;
            try
            {
                var manager=effectManager.GetValue(__instance) as EntityEffectManager;
                if(active!=null && manager)__state=active.healing(manager.GetComponent<DaggerfallEntityBehaviour>(),__instance.Caster);
            }
            catch(Exception error) {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HEALING_OBSERVATION_FAILED: "+error.Message);}
        }
        // PlayerAggro is native's own hostile-attempt boundary. Prefix before its
        // player-only early return preserves the same NPC-caster attempt semantics.
        private static void HostileMagicAttempt(BaseEntityEffect __instance)
        {
            try
            {
                var manager=effectManager.GetValue(__instance) as EntityEffectManager;
                ObserveHostileMagic(__instance,manager ? manager.EntityBehaviour : null,__instance.Caster);
            }
            catch(Exception error) {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOSTILE_MAGIC_OBSERVATION_FAILED: "+error.Message);}
        }
        // Continuous fatigue/spell-point effects do not call PlayerAggro. Carry the
        // exact live effect only across its native source wrapper, then observe the
        // real HandleAttackFromSource call after the resource impact was accepted.
        private static void NonHealthEffectSource(IEntityEffect sourceEffect,out BaseEntityEffect __state)
        { __state=NativeHostileMagicObserver.BeginEffectSource(sourceEffect); }
        // Positive health damage already completes through DamageHealthFromSource's
        // damage finalizer. Scope only zero damage, where native still enters
        // HandleAttackFromSource but that finalizer deliberately does not observe.
        private static void ZeroHealthEffectSource(IEntityEffect sourceEffect,int amount,out BaseEntityEffect __state)
        { __state=NativeHostileMagicObserver.BeginEffectSource(amount==0 ? sourceEffect : null); }
        private static Exception EndNonHealthEffectSource(Exception __exception,BaseEntityEffect __state)
        {
            NativeHostileMagicObserver.EndEffectSource(__state);
            return __exception;
        }
        private static void NonHealthMagicAttack(DaggerfallEntityBehaviour __instance,DaggerfallEntityBehaviour sourceEntityBehaviour)
        {
            try { ObserveHostileMagic(NativeHostileMagicObserver.ActiveEffectSource,__instance,sourceEntityBehaviour); }
            catch(Exception error) {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOSTILE_MAGIC_OBSERVATION_FAILED: "+error.Message);}
        }
        private static void ObserveHostileMagic(BaseEntityEffect effect,DaggerfallEntityBehaviour victim,DaggerfallEntityBehaviour caster)
        {
            if(active==null || !NativeHostileMagicObserver.TryObserveAttempt(effect,victim,caster)) return;
            var complete=active.hostileMagic(victim,caster,effect.Key);
            if(complete!=null) complete(null);
        }

        private static Exception Finish(Exception __exception,Action<Exception> __state)
        {
            try { if(__state!=null) __state(__exception); }
            catch(Exception error) { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DEFENSE_OBSERVATION_FAILED: "+error.Message); }
            return __exception;
        }
        public void Dispose() { if(active!=this) return; harmony.UnpatchAll(Owner); active=null; }
    }
}
