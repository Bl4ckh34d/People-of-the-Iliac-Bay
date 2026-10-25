using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using Observer=PeopleOfTheIliacBay.QuestHooks.NativeHostileMagicObserver;

namespace NativeHostileMagicObserver.Regression
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Check.True(Observer.IsHostileEffect("Paralyze",false));
                Check.True(Observer.IsHostileEffect("Silence",false));
                Check.True(Observer.IsHostileEffect("Damage-Fatigue",false));
                Check.True(Observer.IsHostileEffect("ContinuousDamage-Fatigue",false));
                Check.True(Observer.IsHostileEffect("Damage-Health",false));
                Check.True(Observer.IsHostileEffect("ContinuousDamage-Health",false));
                Check.True(Observer.IsHostileEffect("Damage-SpellPoints",false));
                Check.True(Observer.IsHostileEffect("ContinuousDamage-SpellPoints",false));
                Check.True(Observer.IsHostileEffect("Drain-Strength",true));
                Check.False(Observer.IsHostileEffect("Drain-Forged",false));
                Check.False(Observer.IsHostileEffect("Sleep",false));
                Check.False(Observer.IsHostileEffect("Charm",false));
                Check.False(Observer.IsHostileEffect("Pacify-Humanoid",false));
                Check.False(Observer.IsHostileEffect("Heal-Health",false));
                Check.False(Observer.IsHostileEffect("Cure-Poison",false));
                Check.False(Observer.IsHostileEffect("Cure-Disease",false));
                var victim=new DaggerfallEntityBehaviour();
                var caster=new DaggerfallEntityBehaviour();
                var paralyze=new BaseEntityEffect {Key="Paralyze"};
                Check.True(Observer.CanObserve(paralyze,victim,caster));
                Check.True(Observer.TryObserveAttempt(paralyze,victim,caster));
                Check.False(Observer.TryObserveAttempt(paralyze,victim,caster));
                Check.False(Observer.CanObserve(paralyze,victim,victim));
                GameManager.Instance.PlayerEntityBehaviour=caster;
                Check.False(Observer.CanObserve(paralyze,victim,caster));
                Console.WriteLine("Native hostile magic observer regression: PASS");
                return 0;
            }
            catch(Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }
    }

    internal static class Check
    {
        internal static void True(bool value)
        {
            if(!value) throw new InvalidOperationException("Expected true.");
        }
        internal static void False(bool value)
        {
            if(value) throw new InvalidOperationException("Expected false.");
        }
    }
}
