using System;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int Main()
    {
        Check(NativeHauntingTargetPolicy.InScope("S0000977", true, false, 19 * 3600), "night ghost");
        Check(NativeHauntingTargetPolicy.InScope("s0000977", true, false, 4 * 3600 + 59 * 60), "predawn wraith");
        Check(!NativeHauntingTargetPolicy.InScope("S0000977", true, false, 5 * 3600), "dawn excludes targets");
        Check(!NativeHauntingTargetPolicy.InScope("S0000977", true, false, 18 * 3600 + 59 * 60), "day excludes targets");
        Check(!NativeHauntingTargetPolicy.InScope("S0000977", true, true, 20 * 3600), "lifted curse");
        Check(!NativeHauntingTargetPolicy.InScope("OTHER", true, false, 20 * 3600), "unrelated quest");
        Check(!NativeHauntingTargetPolicy.InScope(null, true, false, 20 * 3600), "unowned ghost");
        Check(!NativeHauntingTargetPolicy.InScope("S0000977", false, false, 20 * 3600), "other mobile");
        Check(!NativeHauntingTargetPolicy.InScope("S0000977", true, false, double.NaN), "invalid clock");
        Check(NativeHauntingTargetPolicy.InScope("S0000977", true, false, 3 * 86400 + 20 * 3600), "later day");
        Console.WriteLine("PASS native haunting quest, monster, curse and night boundaries (10 cases)");
        return 0;
    }
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); }
}
