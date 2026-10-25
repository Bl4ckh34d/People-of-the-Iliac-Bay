using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class HouseholdAccess
    {
        public static bool HasLivingOccupant(int building, IEnumerable<Resident> people,
            Func<Resident, ActivitySnapshot> state)
        {
            foreach (var person in people)
            {
                if (!person.IsAlive) continue;
                var current = state(person);
                if (current.BuildingKey == building && current.Activity != Activity.Travel) return true;
            }
            return false;
        }
        public static ActivitySnapshot EffectiveState(ActivitySnapshot scheduled, AlarmPerson alarm, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (alarm == null || !alarm.HasPosition) return scheduled;
            var position = alarm;
            if (alarm.Escape != null)
            {
                // Queries must not advance the saved actor or depend on render order.
                position = new AlarmPerson { HasPosition = true, BuildingKey = alarm.BuildingKey,
                    X = alarm.X, Y = alarm.Y, Z = alarm.Z };
                alarm.Escape.Advance(position, now);
            }
            return new ActivitySnapshot(position.BuildingKey < 0 ? Activity.Travel : Activity.Home,
                position.BuildingKey, new RoutePoint(position.X, position.Z), 0);
        }
        public static bool RoutineLocked(Household household, Func<Resident, ActivitySnapshot> state)
        {
            if (household == null) throw new ArgumentNullException(nameof(household));
            if (state == null) throw new ArgumentNullException(nameof(state));
            foreach (var resident in household.Residents)
            {
                if (!resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult) continue;
                var current = state(resident);
                if (current.BuildingKey == household.BuildingKey &&
                    current.Activity != Activity.Travel && current.Activity != Activity.Sleep)
                    return false;
            }
            return true;
        }
    }
}
