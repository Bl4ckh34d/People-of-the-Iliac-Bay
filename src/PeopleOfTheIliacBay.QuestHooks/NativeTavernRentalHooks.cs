using System;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using HarmonyLib;
using UnityEngine;

namespace PeopleOfTheIliacBay.QuestHooks
{
    public sealed class NativeTavernRentalHooks : IDisposable
    {
        private const string Owner="local.peopleoftheiliacbay.tavern-rental";
        private static NativeTavernRentalHooks active;
        private readonly Harmony harmony=new Harmony(Owner);
        private readonly Func<int,int,int,Action,bool> rent;
        private readonly Func<bool,string> restBlocked;
        private readonly Func<DaggerfallInterior,Vector3[],Vector3[]> completeRestMarkers;
        public NativeTavernRentalHooks(Func<int,int,int,Action,bool> rent,Func<bool,string> restBlocked,
            Func<DaggerfallInterior,Vector3[],Vector3[]> completeRestMarkers=null)
        {
            if(active!=null) throw new InvalidOperationException("Tavern rental hooks already installed.");
            this.rent=rent ?? throw new ArgumentNullException(nameof(rent));
            this.restBlocked=restBlocked ?? throw new ArgumentNullException(nameof(restBlocked)); active=this;
            this.completeRestMarkers=completeRestMarkers;
            try
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                harmony.Patch(typeof(DaggerfallTavernWindow).GetMethod("ConfirmRenting_OnButtonClick",flags),
                    prefix:new HarmonyMethod(typeof(NativeTavernRentalHooks).GetMethod("Confirm",BindingFlags.Static|BindingFlags.NonPublic)));
                harmony.Patch(typeof(DaggerfallTavernWindow).GetMethod("RentRoom",flags),
                    prefix:new HarmonyMethod(typeof(NativeTavernRentalHooks).GetMethod("FreeRoom",BindingFlags.Static|BindingFlags.NonPublic)));
                harmony.Patch(typeof(DaggerfallRestWindow).GetMethod("CanRest",flags),
                    prefix:new HarmonyMethod(typeof(NativeTavernRentalHooks).GetMethod("BeforeRest",BindingFlags.Static|BindingFlags.NonPublic)));
                harmony.Patch(typeof(DaggerfallInterior).GetMethod("FindMarkers"),
                    postfix:new HarmonyMethod(typeof(NativeTavernRentalHooks).GetMethod("RestMarkers",BindingFlags.Static|BindingFlags.NonPublic)));
            }
            catch { Dispose(); throw; }
        }
        private static bool Confirm(DaggerfallTavernWindow __instance,DaggerfallMessageBox.MessageBoxButtons __1,
            PlayerGPS.DiscoveredBuilding ___buildingData,int ___daysToRent,int ___tradePrice)
        {
            return active==null || __1!=DaggerfallMessageBox.MessageBoxButtons.Yes ||
                !active.rent(___buildingData.buildingKey,___daysToRent,___tradePrice,__instance.CloseWindow);
        }
        private static bool FreeRoom(PlayerGPS.DiscoveredBuilding ___buildingData,int ___daysToRent)
        { return active==null || !active.rent(___buildingData.buildingKey,___daysToRent,0,()=>{}); }
        private static bool BeforeRest(DaggerfallRestWindow __instance,bool ___ignoreAllocatedBed,ref bool __result)
        {
            string message=active==null ? null : active.restBlocked(___ignoreAllocatedBed);
            if(message==null) return true;
            __result=false; __instance.CloseWindow(); DaggerfallUI.MessageBox(message); return false;
        }
        private static void RestMarkers(DaggerfallInterior __instance,DaggerfallInterior.InteriorMarkerTypes type,ref Vector3[] __result)
        {
            if(active!=null && active.completeRestMarkers!=null && type==DaggerfallInterior.InteriorMarkerTypes.Rest)
                __result=active.completeRestMarkers(__instance,__result);
        }
        public void Dispose() { if(active!=this) return; harmony.UnpatchAll(Owner); active=null; }
    }
}
