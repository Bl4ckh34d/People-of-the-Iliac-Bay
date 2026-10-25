using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private TownGeometry refugeGeometry;
        private readonly Dictionary<int,List<List<GuardPoint3>>> refugeInteriors=new Dictionary<int,List<List<GuardPoint3>>>();
        private bool HauntedNow(double now)
        { return Roster!=null && SettlementHaunting.StreetsDangerous(Roster.SettlementId,
            GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now); }
        private void ReleaseSafeRefuges(double now)
        {
            bool lifted=GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0);
            foreach(var guard in Roster.Guards)
                if(guard.IsAlive && guard.Refuge!=null && !guard.Refuge.WallDuty && !guard.Refuge.ReleasedAt.HasValue)
                {
                    var release=SettlementHaunting.FirstSafeTime(Roster.SettlementId,lifted,guard.Refuge.StartsAt,now);
                    if(release.HasValue) guard.Refuge.Release(release.Value);
                }
        }
        // Kept for the existing defense-pass call sites. A haunting is not a
        // separate guard behavior: hostile ghosts and wraiths follow ordinary
        // enemy defense, with retreat eligibility owned by NativeNpcCombatRoleBinding.
        private bool ObserveHauntingThreat(DaggerfallEntityBehaviour target,double now)
        {
            return false;
        }
    }
}
