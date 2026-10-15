using System;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    public sealed class NativeTavernRentalHooks : IDisposable
    {
        private const string Owner="local.livingcities.tavern-rental";
        private static NativeTavernRentalHooks active;
        private readonly Harmony harmony=new Harmony(Owner);
        private readonly Func<int,int,int,Action,bool> rent;
        public NativeTavernRentalHooks(Func<int,int,int,Action,bool> rent)
        {
            if(active!=null) throw new InvalidOperationException("Tavern rental hooks already installed.");
            this.rent=rent ?? throw new ArgumentNullException(nameof(rent)); active=this;
            try
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                harmony.Patch(typeof(DaggerfallTavernWindow).GetMethod("ConfirmRenting_OnButtonClick",flags),
                    prefix:new HarmonyMethod(typeof(NativeTavernRentalHooks).GetMethod("Confirm",BindingFlags.Static|BindingFlags.NonPublic)));
                harmony.Patch(typeof(DaggerfallTavernWindow).GetMethod("RentRoom",flags),
                    prefix:new HarmonyMethod(typeof(NativeTavernRentalHooks).GetMethod("FreeRoom",BindingFlags.Static|BindingFlags.NonPublic)));
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
        public void Dispose() { if(active!=this) return; harmony.UnpatchAll(Owner); active=null; }
    }
}
