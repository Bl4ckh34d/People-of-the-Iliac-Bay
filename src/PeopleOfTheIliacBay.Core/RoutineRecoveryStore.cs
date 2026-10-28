using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private readonly Dictionary<string, RoutineRecoveryPlan> routineRecoveryIndex =
            new Dictionary<string, RoutineRecoveryPlan>(StringComparer.Ordinal);
        public int RoutineRecoveryRevision { get; private set; }

        public void SaveRoutineRecovery(RoutineRecoveryPlan plan)
        {
            PublishRoutineRecovery(PrepareRoutineRecovery(plan));BumpRoutineRecoveryRevision();
        }

        public void SaveHouseholdRoutineRecoveries(RoutineRecoveryPlan first,RoutineRecoveryPlan second)
        {
            var a=PrepareRoutineRecovery(first);var b=PrepareRoutineRecovery(second);
            Require(a.ResidentId!=b.ResidentId && a.HouseholdDayId!=null && a.HouseholdDayId==b.HouseholdDayId &&
                a.HouseholdDayFingerprint==b.HouseholdDayFingerprint && a.AbsoluteDay==b.AbsoluteDay &&
                a.CreatedAt==b.CreatedAt && a.HouseholdJointSkipped==b.HouseholdJointSkipped,
                "Paired recovery needs both distinct parents, one source, one observation and one joint disposition.");
            var family=MatchingRecoveryHousehold(state,a);
            Require(family!=null && (a.ResidentId==family.First.ResidentId && b.ResidentId==family.Second.ResidentId ||
                a.ResidentId==family.Second.ResidentId && b.ResidentId==family.First.ResidentId),"Paired recovery owners are not the household parents.");
            // Nothing above mutates the ledger. Publish both validated copies and
            // one revision only after the entire paired operation is admissible.
            PublishRoutineRecovery(a);PublishRoutineRecovery(b);BumpRoutineRecoveryRevision();
        }

        private RoutineRecoveryPlan PrepareRoutineRecovery(RoutineRecoveryPlan plan)
        {
            var copy = RoutineRecoveryPlan.Copy(plan);
            ValidateRoutineRecoveryOwner(state, copy, true);
            var routine = FindRoutine(state, copy.SettlementId, copy.ResidentId);
            var family=MatchingRecoveryHousehold(state,copy);
            if(copy.HouseholdDayId!=null)
                Require(family!=null && RecoveryHouseholdSourcesCurrent(state,copy,family),"Household recovery source is stale or unpublished.");
            Require(RoutineRecoveryRoutes.Matches(copy, routine, copy.HomeBuildingKey,state.Appointments,family),
                "Routine recovery contains a route outside the saved routine graph.");
            return copy;
        }
        private void PublishRoutineRecovery(RoutineRecoveryPlan copy)
        {
            state.RoutineRecoveries.RemoveAll(item => item.ResidentId == copy.ResidentId);
            state.RoutineRecoveries.Add(copy);
            routineRecoveryIndex[copy.ResidentId] = copy;
        }

        public bool HasRoutineRecovery(string residentId)
        { return !string.IsNullOrWhiteSpace(residentId) && routineRecoveryIndex.ContainsKey(residentId); }

        public bool TryGetRoutineRecoveryDay(string residentId, out int absoluteDay)
        {
            absoluteDay = -1;
            if (string.IsNullOrWhiteSpace(residentId)) return false;
            RoutineRecoveryPlan plan;
            if (!routineRecoveryIndex.TryGetValue(residentId, out plan)) return false;
            absoluteDay = plan.AbsoluteDay;
            return true;
        }

        public RoutineRecoveryPlan GetRoutineRecovery(string residentId, int absoluteDay,
            string routineFingerprint, string appointmentFingerprint)
        {
            if (string.IsNullOrWhiteSpace(residentId)) return null;
            RoutineRecoveryPlan plan;
            if (!routineRecoveryIndex.TryGetValue(residentId, out plan) || plan.HouseholdDayId!=null || plan.AbsoluteDay != absoluteDay ||
                !string.Equals(plan.RoutineFingerprint, routineFingerprint, StringComparison.Ordinal) ||
                !string.Equals(plan.AppointmentFingerprint, appointmentFingerprint, StringComparison.Ordinal)) return null;
            if(ActiveFamilyOwnsRecoveryDay(plan))return null;
            return RoutineRecoveryPlan.Copy(plan);
        }

        private bool ActiveFamilyOwnsRecoveryDay(RoutineRecoveryPlan recovery)
        {
            var family=state.HouseholdDays.Find(p=>p.Status=="planned" && p.SettlementId==recovery.SettlementId &&
                p.HomeKey==recovery.HomeBuildingKey && p.AbsoluteDay==recovery.AbsoluteDay &&
                (p.First.ResidentId==recovery.ResidentId || p.Second.ResidentId==recovery.ResidentId));
            if(family==null)return false;
            try
            {
                Household home;ResidentRoutine first,second;ResolveHouseholdDaySources(state,family,out home,out first,out second);
                HouseholdDayPlanner.ValidateSources(family,home,first,second,state.WorldSeed);return true;
            }
            catch(ArgumentException){return false;}
        }

        public RoutineRecoveryPlan GetHouseholdRoutineRecovery(string residentId,int absoluteDay)
        {
            RoutineRecoveryPlan plan;
            if(string.IsNullOrWhiteSpace(residentId) || !routineRecoveryIndex.TryGetValue(residentId,out plan) ||
                plan.AbsoluteDay!=absoluteDay || plan.HouseholdDayId==null)return null;
            var family=MatchingRecoveryHousehold(state,plan);
            if(family==null || !RecoveryHouseholdSourcesCurrent(state,plan,family))return null;
            return RoutineRecoveryPlan.Copy(plan);
        }

        private static HouseholdDayPlan MatchingRecoveryHousehold(WorldState world,RoutineRecoveryPlan plan)
        {
            if(plan.HouseholdDayId==null || world.HouseholdDays==null)return null;
            return world.HouseholdDays.Find(p=>p.Id==plan.HouseholdDayId && p.Status=="planned" &&
                p.SettlementId==plan.SettlementId && p.HomeKey==plan.HomeBuildingKey && p.AbsoluteDay==plan.AbsoluteDay &&
                plan.CreatedAt>=p.PlannedAt &&
                p.WakeMinute==plan.WakeMinute && p.SleepMinute==plan.SleepMinute &&
                (p.First.ResidentId==plan.ResidentId || p.Second.ResidentId==plan.ResidentId) &&
                RoutineRecoveryIdentity.HouseholdDay(p)==plan.HouseholdDayFingerprint);
        }
        private static bool RecoveryHouseholdSourcesCurrent(WorldState world,RoutineRecoveryPlan plan,HouseholdDayPlan family)
        {
            try
            {
                Household home;ResidentRoutine first,second;ResolveHouseholdDaySources(world,family,out home,out first,out second);
                HouseholdDayPlanner.ValidateSources(family,home,first,second,world.WorldSeed);
                var routine=plan.ResidentId==family.First.ResidentId?first:second;
                string other=plan.ResidentId==family.First.ResidentId?family.Second.ResidentId:family.First.ResidentId;
                return RoutineRecoveryIdentity.Fingerprint(routine,home.BuildingKey,plan.WakeMinute,plan.SleepMinute)==plan.RoutineFingerprint &&
                    RoutineRecoveryIdentity.Appointments(world.Appointments,plan.ResidentId,plan.AbsoluteDay)==plan.AppointmentFingerprint &&
                    RoutineRecoveryIdentity.HouseholdAppointments(world.Appointments,other,plan.AbsoluteDay)==plan.OtherParentAppointmentFingerprint &&
                    RoutineRecoveryRoutes.PointProofsBelongToPlan(plan,world.Appointments);
            }
            catch(ArgumentException){return false;}
        }

        public bool RemoveRoutineRecovery(string residentId)
        {
            if (string.IsNullOrWhiteSpace(residentId) || !routineRecoveryIndex.Remove(residentId)) return false;
            state.RoutineRecoveries.RemoveAll(item => item.ResidentId == residentId);
            BumpRoutineRecoveryRevision();
            return true;
        }

        private void IndexRoutineRecoveries()
        {
            routineRecoveryIndex.Clear();
            foreach (var plan in state.RoutineRecoveries)
                routineRecoveryIndex.Add(plan.ResidentId, plan);
            BumpRoutineRecoveryRevision();
        }

        private void BumpRoutineRecoveryRevision()
        { if (RoutineRecoveryRevision < int.MaxValue) RoutineRecoveryRevision++; }

        private static List<RoutineRecoveryPlan> CopyRoutineRecoveries(WorldState source,WorldState copied)
        {
            var result = new List<RoutineRecoveryPlan>();
            Require(source.RoutineRecoveries != null, "Missing routine recovery ledger.");
            var residents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in source.RoutineRecoveries)
            {
                var plan = RoutineRecoveryPlan.Copy(raw);
                Require(residents.Add(plan.ResidentId), "Duplicate routine recovery resident.");
                ValidateRoutineRecoveryOwner(copied, plan, false);
                Require(RoutineRecoveryRoutes.PointProofsBelongToPlan(plan, copied.Appointments),
                    "Routine recovery contains a point proof for another appointment.");
                var resident = ResolveResident(copied, plan.SettlementId, plan.ResidentId);
                var routine = FindRoutine(copied, plan.SettlementId, plan.ResidentId);
                var family=MatchingRecoveryHousehold(copied,plan);
                if (resident.HomeKey == plan.HomeBuildingKey && routine != null && string.Equals(
                    RoutineRecoveryIdentity.Fingerprint(routine, resident.HomeKey, plan.WakeMinute, plan.SleepMinute),
                    plan.RoutineFingerprint, StringComparison.Ordinal) && (plan.HouseholdDayId==null || family!=null))
                    Require(RoutineRecoveryRoutes.Matches(plan, routine, resident.HomeKey,copied.Appointments,family),
                        "Routine recovery contains a route outside the saved routine graph.");
                result.Add(plan);
            }
            return result;
        }

        private static void ValidateRoutineRecoveryOwner(WorldState world, RoutineRecoveryPlan plan, bool requireCurrentRoutine)
        {
            Require(world != null && plan != null && world.Settlements != null,
                "Missing routine recovery owner state.");
            Resident resident = ResolveResident(world, plan.SettlementId, plan.ResidentId);
            Require(resident != null, "Unknown routine recovery resident.");
            if (!requireCurrentRoutine) return;
            Require(resident.IsAlive && resident.HomeKey == plan.HomeBuildingKey,
                "Routine recovery resident or home is unavailable.");
            Require(world.Routines != null, "Missing routine recovery source routines.");
            var routine = FindRoutine(world, plan.SettlementId, plan.ResidentId);
            Require(routine != null && string.Equals(
                RoutineRecoveryIdentity.Fingerprint(routine, resident.HomeKey, plan.WakeMinute, plan.SleepMinute),
                plan.RoutineFingerprint, StringComparison.Ordinal),
                "Routine recovery does not match the resident's current routine.");
        }

        private static ResidentRoutine FindRoutine(WorldState world, int settlementId, string residentId)
        {
            if (world.Routines == null) return null;
            var town = world.Routines.Find(item => item != null && item.SettlementId == settlementId);
            return town == null || town.Residents == null ? null :
                town.Residents.Find(item => item != null && item.ResidentId == residentId);
        }
    }
}
