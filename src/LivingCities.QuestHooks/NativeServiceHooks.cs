using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    public sealed class NativeServiceHooks : IDisposable
    {
        private const string Owner = "local.livingcities.native-services";
        private static NativeServiceHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<StaticNPC, bool> blocked;
        public NativeServiceHooks(Func<StaticNPC, bool> blocked)
        {
            if (active != null) throw new InvalidOperationException("Native service hooks already installed.");
            this.blocked = blocked ?? throw new ArgumentNullException(nameof(blocked));
            active = this;
            try
            {
                var prefix = new HarmonyMethod(typeof(NativeServiceHooks).GetMethod("Before", BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic), prefix: prefix);
                harmony.Patch(typeof(TalkManager).GetMethod("TalkToStaticNPC"), prefix: prefix);
            }
            catch { Dispose(); throw; }
        }
        private static bool Before(StaticNPC __0) { return active == null || !active.blocked(__0); }
        public void Dispose() { if (active != this) return; harmony.UnpatchAll(Owner); active = null; }
    }
}
