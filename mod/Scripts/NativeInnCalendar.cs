using System;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static class NativeInnCalendar
    {
        private static readonly ulong ClassicEpoch=ReadEpoch();
        private static ulong ReadEpoch()
        {var date=new DaggerfallDateTime();date.FromClassicDaggerfallTime(0);return date.ToSeconds();}

        public static NpcInnServiceTariff ForServiceAt(int destinationRegion,double serviceAt)
        {
            double seconds=Math.Floor(serviceAt);
            double limit=ClassicEpoch+((double)uint.MaxValue+1)*DaggerfallDateTime.SecondsPerMinute;
            if(destinationRegion<0 || destinationRegion>255 || double.IsNaN(seconds) || double.IsInfinity(seconds) ||
                seconds<ClassicEpoch || seconds>=limit || seconds/86400d>int.MaxValue)
                throw new ArgumentException("Inn service timestamp or destination region exceeds the native calendar domain.");
            var date=new DaggerfallDateTime();date.FromSeconds((ulong)seconds);
            // Only the global Harvest End holiday changes NPC menu prices.
            // Destination-region holiday provenance therefore preserves native
            // tavern prices while avoiding the current player's region/date.
            return new NpcInnServiceTariff{AbsoluteDay=(int)(seconds/86400d),RegionIndex=destinationRegion,
                HolidayId=FormulaHelper.GetHolidayId(date.ToClassicDaggerfallTime(),destinationRegion)};
        }

        public static bool TryServiceHours(int opens,int closes,out int openMinute,out int closeMinute)
        {
            openMinute=closeMinute=0;
            if(opens<0 || opens>=24 || closes<0 || closes>25)return false;
            int end=Math.Min(1440,closes*60);
            if(opens*60>=end)return false;
            openMinute=opens*60;closeMinute=end;return true;
        }
    }
}
