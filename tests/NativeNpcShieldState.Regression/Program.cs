using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int Main()
    {
        // A duration alone cannot establish that incoming native damage is absorbed.
        Check(Read(Shield(6, 0)) == 0, "Zero-strength Shield must not count as protection.");
        Check(Read(Shield(6, -3)) == 0, "Overdrawn Shield must not count as protection.");
        Check(Read(Shield(0, 10)) == 0, "Expired Shield must not count as protection.");
        var ended = Shield(6, 10); ended.HasEnded = true;
        Check(Read(ended) == 0, "Ended Shield must not count as protection.");
        Check(Read(Shield(6, 10)) == 6, "Native positive strength and duration must count.");
        Check(Read(Shield(2, 1)) == 2, "Partly depleted native Shield must remain protection.");
        Check(Read(Shield(9, 0), Shield(3, 4)) == 3, "Empty effect must not hide real protection.");
        Check(Read(new ShieldVariant { RoundsRemaining = 6, Strength = 10 }) == 0,
            "Derived replacement effect must not masquerade as exact supported native Shield.");
        Check(Read((IEntityEffect)null) == 0, "Absent effect must not count as protection.");
        Console.WriteLine("9/9 native Shield state checks passed");
        return 0;
    }

    private static Shield Shield(int rounds, int strength) => new Shield { RoundsRemaining = rounds, Strength = strength };
    private static int Read(params IEntityEffect[] effects) => NativeNpcShieldState.ProtectionRounds(
        new EntityEffectManager { EffectBundles = new[] { new LiveEffectBundle { liveEffects = new List<IEntityEffect>(effects) } } });
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class ShieldVariant : Shield { }
}

// Only the engine/native effect storage boundary is replaced. The production
// reader evaluates native duration, ended state, exact type and save-data strength.
namespace DaggerfallWorkshop.Game.MagicAndEffects
{
    public interface IEntityEffect { bool HasEnded { get; } int RoundsRemaining { get; } object GetSaveData(); }
    public sealed class LiveEffectBundle { public List<IEntityEffect> liveEffects; }
    public sealed class EntityEffectManager { public LiveEffectBundle[] EffectBundles; }
}
namespace DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects
{
    public class Shield : IEntityEffect
    {
        public bool HasEnded { get; set; }
        public int RoundsRemaining { get; set; }
        public int Strength;
        public struct SaveData_v1 { public int startingShield, shieldRemaining; }
        public object GetSaveData() => new SaveData_v1 { startingShield = 10, shieldRemaining = Strength };
    }
}
