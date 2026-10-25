using System;
using System.Reflection;
using DaggerfallWorkshop.Game.Formulas;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    public sealed class NativeTheftHooks : IDisposable
    {
        private const string Owner = "local.peopleoftheiliacbay.theft-witnesses";
        private static NativeTheftHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<bool?> witnesses;
        public NativeTheftHooks(Func<bool?> witnesses)
        {
            if (active != null) throw new InvalidOperationException("Theft hooks already installed.");
            this.witnesses = witnesses ?? throw new ArgumentNullException(nameof(witnesses));
            active = this;
            try { harmony.Patch(typeof(FormulaHelper).GetMethod("CalculateShopliftingChance"), prefix:
                new HarmonyMethod(typeof(NativeTheftHooks).GetMethod("BeforeChance", BindingFlags.NonPublic | BindingFlags.Static))); }
            catch { Dispose(); throw; }
        }
        private static bool BeforeChance(ref int __result)
        {
            // Unknown coverage or any possible witness retains the native skill check.
            if (active == null || active.witnesses() != false) return true;
            __result = 0; return false;
        }
        public void Dispose()
        { if (active != this) return; harmony.UnpatchAll(Owner); active = null; }
    }
}
