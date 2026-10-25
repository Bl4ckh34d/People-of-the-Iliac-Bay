using System;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    public sealed class NativeHouseholdDoorHooks : IDisposable
    {
        private const string Owner = "local.peopleoftheiliacbay.household-doors";
        private static NativeHouseholdDoorHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<BuildingSummary, bool?> access;
        private readonly Func<int, string> greeting;
        private static int? greetingBuilding;
        public NativeHouseholdDoorHooks(Func<BuildingSummary, bool?> access, Func<int, string> greeting)
        {
            if (active != null) throw new InvalidOperationException("Household door hooks already installed.");
            this.access = access ?? throw new ArgumentNullException(nameof(access));
            this.greeting = greeting ?? throw new ArgumentNullException(nameof(greeting));
            active = this;
            try
            {
                harmony.Patch(typeof(PlayerActivate).GetMethod("BuildingIsUnlocked"), prefix:
                    new HarmonyMethod(typeof(NativeHouseholdDoorHooks).GetMethod("BeforeAccess", BindingFlags.NonPublic | BindingFlags.Static)));
                var door = AccessTools.Method(typeof(PlayerActivate), "ActivateStaticDoor");
                harmony.Patch(door,
                    prefix: new HarmonyMethod(typeof(NativeHouseholdDoorHooks), nameof(BeforeDoor)),
                    finalizer: new HarmonyMethod(typeof(NativeHouseholdDoorHooks), nameof(AfterDoor)));
                harmony.Patch(AccessTools.Method(typeof(TextProvider), "GetRandomText", new[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(NativeHouseholdDoorHooks), nameof(BeforeGreeting)));
            }
            catch { Dispose(); throw; }
        }
        private static void BeforeDoor(bool hitBuilding, StaticBuilding building, out int? __state)
        {
            __state = greetingBuilding;
            greetingBuilding = hitBuilding && !GameManager.Instance.PlayerEnterExit.IsPlayerInside
                ? (int?)building.buildingKey : null;
        }
        private static Exception AfterDoor(Exception __exception, int? __state)
        {
            greetingBuilding = __state;
            return __exception;
        }
        private static bool BeforeGreeting(int __0, ref string __result)
        {
            if (__0 != 256 || active == null || !greetingBuilding.HasValue) return true;
            string replacement = active.greeting(greetingBuilding.Value);
            if (replacement == null) return true;
            __result = replacement;
            return false;
        }
        private static bool BeforeAccess(BuildingSummary __0, ref bool __result)
        {
            bool? value = active == null ? null : active.access(__0);
            if (!value.HasValue) return true;
            __result = value.Value;
            return false;
        }
        public void Dispose()
        {
            if (active != this) return;
            harmony.UnpatchAll(Owner); active = null; greetingBuilding = null;
        }
    }
}
