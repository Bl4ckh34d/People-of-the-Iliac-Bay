using System;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class HauntingRoutineDay
    {
        public double Day;
        public bool Restricted;
    }
    public sealed partial class WorldStateStore
    {
        public bool RestrictHauntingRoutines(int settlementId,bool liftedCurse,double now)
        {
            SettlementHaunting.StreetsDangerous(settlementId,liftedCurse,now);
            if(settlementId!=1291010263) return false;
            double day=Math.Floor(now/86400);
            if(state.HauntingDay==null || state.HauntingDay.Day!=day)
                state.HauntingDay=new HauntingRoutineDay { Day=day,Restricted=!liftedCurse };
            return state.HauntingDay.Restricted;
        }
    }
}
