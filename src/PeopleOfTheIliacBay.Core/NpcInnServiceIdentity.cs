using System;

namespace PeopleOfTheIliacBay.Core
{
    // Payment identity and validation window, not physical presence or admission.
    // A service contract must separately prove keeper, quote, source and authority.
    [Serializable]
    public sealed class NpcInnServiceIdentity
    {
        public string BuyerId,HouseholdDayId,HouseholdSourceFingerprint;
        public int SettlementId,BuildingKey;
        public double StartsAt,EndsAt;
        public static NpcInnServiceIdentity Copy(NpcInnServiceIdentity source)
        {
            if(source==null || string.IsNullOrWhiteSpace(source.BuyerId) || source.BuyerId.Length>256 ||
                source.SettlementId<0 || source.BuildingKey<0 || !Time(source.StartsAt) || !Time(source.EndsAt))
                throw new ArgumentException("Invalid Inn service identity or validation window.");
            HouseholdInnVisitProvenance.Validate(source.HouseholdDayId,source.HouseholdSourceFingerprint);
            return new NpcInnServiceIdentity{BuyerId=source.BuyerId,SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,
                StartsAt=source.StartsAt,EndsAt=source.EndsAt,HouseholdDayId=source.HouseholdDayId,
                HouseholdSourceFingerprint=source.HouseholdSourceFingerprint};
        }
        internal static NpcInnServiceIdentity FromObserved(ObservedVisit source)
        {
            if(source==null)throw new ArgumentException("An observed visit needs a resident identity.",nameof(source));
            return new NpcInnServiceIdentity{BuyerId=source.ResidentId,SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,
                StartsAt=source.ObservedAt,EndsAt=source.PlannedEndAt,HouseholdDayId=source.HouseholdDayId,
                HouseholdSourceFingerprint=source.HouseholdSourceFingerprint};
        }
        private static bool Time(double time){return !double.IsNaN(time) && !double.IsInfinity(time) && time>=0;}
    }
}
