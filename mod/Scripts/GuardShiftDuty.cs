using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private float nextShiftDutyTick;
        private void UpdateShiftDuty(double now)
        {
            MaterializeDayRest(now);
            if (UnityEngine.Time.unscaledTime < nextShiftDutyTick) return;
            nextShiftDutyTick = UnityEngine.Time.unscaledTime + 1;
            foreach (var patrol in new List<GuardPatrolAssignment>(Roster.Patrols))
            {
                var leader = Roster.Guards.Find(g => g.Id == patrol.Plan.LeaderId);
                if (!patrol.InterruptedAt.HasValue && !GuardShifts.OnDuty(leader, now))
                    Roster.StopPatrol(patrol.Plan.LeaderId, now);
            }
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive) continue;
                // Investigations and combat finish before the ordinary shift resumes.
                if (!GuardShifts.OnDuty(guard, now) && guard.Duty == GuardDuty.Posted &&
                    guard.Interior == null && guard.Refuge == null && guard.IncidentId == null)
                    guard.DayRestUntil = GuardShifts.NextChange(now);
                else if (GuardShifts.OnDuty(guard, now) && guard.Refuge == null)
                    guard.DayRestUntil = 0;
            }
        }

        public string DutyDescription(string id, double now)
        {
            var guard = Roster == null ? null : Roster.Guards.Find(g => g.Id == id);
            if (guard == null) return null;
            string shift = guard.Shift == GuardShift.Night ? "Night shift (18:00–06:00). " : "Day shift (06:00–18:00). ";
            if (!guard.IsAlive) return shift + "Dead.";
            if (guard.Refuge != null)
            {
                var phase = guard.Refuge.Evaluate(now).Phase;
                if(guard.Refuge.HauntingShelter)
                    return shift+(phase==RefugePhase.Sheltered ? "Sheltering inside guard quarters during the unresolved Lysandus haunting." :
                        "Walking to real indoor guard quarters under the haunting shelter order.");
                if (guard.Refuge.WallRoute != null)
                    return shift + (phase==RefugePhase.WallPosted ? "Holding an elevated city-wall post with a bow." :
                        "Walking between the street and an elevated wall post through the wall building's lower and upper doors.");
                if (guard.Refuge.RestBedRecord.HasValue)
                    return shift + (phase == RefugePhase.Sheltered ? "Resting at an assigned real bed in wall quarters." :
                        "Walking between the assigned post and a reserved bed in wall quarters.");
                return shift + "Inside a surveyed refuge or walking to or from it.";
            }
            if (guard.Duty != GuardDuty.Posted) return shift + "Current duty: " + guard.Duty + ".";
            if (!GuardShifts.OnDuty(guard, now))
                return shift + "Off duty; waiting for a reachable quarters entrance. Still available in an emergency.";
            if (Roster.IsOnPatrol(id)) return shift + "Patrolling streets with a partner.";
            return shift + (IsGateGuard(id) ? "Holding a surveyed gate post." : "Holding a fixed town post.");
        }
    }
}
