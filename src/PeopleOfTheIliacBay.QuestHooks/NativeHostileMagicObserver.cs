using System.Runtime.CompilerServices;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;

namespace PeopleOfTheIliacBay.QuestHooks
{
    // Native PlayerAggro() intentionally returns for non-player casters. Keep this
    // selection exact: each accepted effect is direct hostile evidence, not a school-wide rule.
    public static class NativeHostileMagicObserver
    {
        private static readonly ConditionalWeakTable<BaseEntityEffect,object> observed=new ConditionalWeakTable<BaseEntityEffect,object>();
        private static readonly object marker=new object();
        [System.ThreadStatic] private static BaseEntityEffect activeEffectSource;
        public static bool IsHostileEffect(string effectKey,bool drainEffect)
        {
            if(drainEffect) return true;
            switch(effectKey)
            {
                case "Paralyze":
                case "Silence":
                case "Damage-Fatigue":
                case "ContinuousDamage-Fatigue":
                case "Damage-Health":
                case "ContinuousDamage-Health":
                case "Damage-SpellPoints":
                case "ContinuousDamage-SpellPoints":
                    return true;
                default:
                    return false;
            }
        }

        public static bool CanObserve(BaseEntityEffect effect,DaggerfallEntityBehaviour victim,DaggerfallEntityBehaviour caster)
        {
            var game=GameManager.Instance;
            return effect!=null && victim && caster &&
                victim!=caster && (game==null || caster!=game.PlayerEntityBehaviour) &&
                IsHostileEffect(effect.Key,effect is DrainEffect);
        }

        // PlayerAggro can run more than once for one incumbent effect. Weak instance
        // keys suppress only that duplicate and do not create saved combat state.
        public static bool TryObserveAttempt(BaseEntityEffect effect,DaggerfallEntityBehaviour victim,DaggerfallEntityBehaviour caster)
        {
            if(!CanObserve(effect,victim,caster) || observed.TryGetValue(effect,out _)) return false;
            observed.Add(effect,marker);
            return true;
        }

        public static BaseEntityEffect BeginEffectSource(IEntityEffect sourceEffect)
        {
            var previous=activeEffectSource;
            activeEffectSource=sourceEffect as BaseEntityEffect;
            return previous;
        }

        public static void EndEffectSource(BaseEntityEffect previous)
        { activeEffectSource=previous; }

        public static BaseEntityEffect ActiveEffectSource => activeEffectSource;
    }
}
