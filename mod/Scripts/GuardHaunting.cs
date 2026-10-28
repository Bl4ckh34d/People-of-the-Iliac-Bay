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
        private bool HauntingShelterNow(double now)
        { return Roster!=null && GuardShifts.HauntingShelterRequired(Roster.SettlementId,
            GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now); }
        private void ReleaseSafeRefuges(double now)
        {
            bool lifted=GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0);
            bool shelter=GuardShifts.HauntingShelterRequired(Roster.SettlementId,lifted,now);
            foreach(var guard in Roster.Guards)
            {
                if(!guard.IsAlive || guard.Refuge==null)continue;
                var phase=guard.Refuge.Evaluate(now).Phase;
                if(shelter && guard.Refuge.WallRoute!=null)
                {
                    if(phase!=RefugePhase.WallReturning && phase!=RefugePhase.Exiting && phase!=RefugePhase.Returning)
                    { guard.Refuge.ReleasedAt=null;guard.Refuge.Release(now); }
                    continue;
                }
                if(shelter && phase!=RefugePhase.Exiting && phase!=RefugePhase.Returning && phase!=RefugePhase.Complete)
                { guard.Refuge.HauntingShelter=true;guard.Refuge.ReleasedAt=null;continue; }
                if(guard.Refuge.HauntingShelter)
                {
                    if(shelter)continue;
                    guard.Refuge.HauntingShelter=false;
                    if(GuardShifts.OnDuty(guard,now) || !guard.Refuge.Resting && !guard.Refuge.RestBedRecord.HasValue ||
                        !guard.Refuge.ReleaseToReturnBy(GuardShifts.NextChange(now)))guard.Refuge.Release(now);
                    continue;
                }
                if(!guard.Refuge.WallDuty && !guard.Refuge.ReleasedAt.HasValue)
                {
                    var release=SettlementHaunting.FirstSafeTime(Roster.SettlementId,lifted,guard.Refuge.StartsAt,now);
                    if(release.HasValue)guard.Refuge.Release(release.Value);
                }
            }
        }
        // Guards obey the town-wide shelter order during the unresolved haunting.
        private bool ObserveHauntingThreat(DaggerfallEntityBehaviour target,double now)
        {
            return HauntingShelterNow(now);
        }
    }
}
