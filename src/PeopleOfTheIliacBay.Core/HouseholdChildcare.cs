using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class HouseholdChildcare
    {
        // Applies only to discretionary outings. Essential work, urgent health
        // and danger retain their own priority and interruption policies.
        public static bool CanTakeOptionalOuting(Household household,string parentId,double startsAt,double endsAt,
            Func<Resident,DailySchedule> effectiveSchedule)
        {
            if(household==null || household.Residents==null || effectiveSchedule==null ||
                string.IsNullOrWhiteSpace(parentId))throw new ArgumentException("Missing household childcare context.");
            if(double.IsNaN(startsAt) || double.IsInfinity(startsAt) || double.IsNaN(endsAt) || double.IsInfinity(endsAt) ||
                startsAt<0 || endsAt<=startsAt || endsAt-startsAt>86400)throw new ArgumentOutOfRangeException(nameof(endsAt));
            var parent=household.Residents.Find(p=>p.Id==parentId);
            if(parent==null || !parent.IsAlive || parent.AgeGroup!=ResidentAgeGroup.Adult || parent.HomeKey!=household.BuildingKey)return false;
            foreach(var child in household.Residents)
            {
                if(!child.IsAlive || child.AgeGroup!=ResidentAgeGroup.Child || child.HomeKey!=household.BuildingKey)continue;
                string otherId=child.MotherId==parentId?child.FatherId:child.FatherId==parentId?child.MotherId:null;
                if(child.MotherId!=parentId && child.FatherId!=parentId)continue;
                var other=household.Residents.Find(p=>p.Id==otherId && p.Id!=parentId);
                if(other==null || !other.IsAlive || other.AgeGroup!=ResidentAgeGroup.Adult || other.HomeKey!=household.BuildingKey)return false;
                var schedule=effectiveSchedule(other);
                if(schedule==null || schedule.HomeBuildingKey!=household.BuildingKey ||
                    !schedule.IsHomeThroughout(startsAt,endsAt))return false;
            }
            return true;
        }
    }
}
