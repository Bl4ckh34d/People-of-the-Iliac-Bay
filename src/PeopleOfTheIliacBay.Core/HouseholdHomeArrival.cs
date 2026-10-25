using System;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class HouseholdHomeArrival
    {
        public int SettlementId,HomeKey;
        public string FirstResidentId,SecondResidentId;
        public double ObservedAt;
        public GuardPoint3 First,Second;
        internal static HouseholdHomeArrival Copy(HouseholdHomeArrival value,HouseholdJointSimulation owner)
        {
            if(value==null)return null;
            if(owner.Phase!=HouseholdJointPhase.Completed || !owner.ReleasedHomeAt.HasValue || owner.ReleasedHomeAt.Value!=value.ObservedAt ||
                value.SettlementId!=owner.Base.SettlementId || value.HomeKey!=owner.Base.HomeKey || value.FirstResidentId!=owner.Base.FirstResidentId ||
                value.SecondResidentId!=owner.Base.SecondResidentId || !ValidPoint(value.First) || !ValidPoint(value.Second))
                throw new ArgumentException("Home-room arrival needs both correct native bodies in their own home coordinate space.");
            double x=value.First.X-value.Second.X,y=value.First.Y-value.Second.Y,z=value.First.Z-value.Second.Z;
            if(x*x+y*y+z*z<0.27)throw new ArgumentException("Both parents need distinct home floor placements.");
            return new HouseholdHomeArrival{SettlementId=value.SettlementId,HomeKey=value.HomeKey,FirstResidentId=value.FirstResidentId,
                SecondResidentId=value.SecondResidentId,ObservedAt=value.ObservedAt,First=Point(value.First),Second=Point(value.Second)};
        }
        private static GuardPoint3 Point(GuardPoint3 value){return new GuardPoint3{X=value.X,Y=value.Y,Z=value.Z};}
        private static bool ValidPoint(GuardPoint3 value){return value!=null && !double.IsNaN(value.X+value.Y+value.Z) &&
            !double.IsInfinity(value.X+value.Y+value.Z) && Math.Abs(value.X)<=10000000 && Math.Abs(value.Y)<=10000000 && Math.Abs(value.Z)<=10000000;}
    }
    public sealed partial class WorldStateStore
    {
        public bool ReconstructHouseholdHomeSimulation(string dayId,int expectedRevision,HouseholdHomeArrival sampled)
        {
            var saved=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==dayId);
            if(saved==null || !saved.Active || saved.Phase!=HouseholdJointPhase.Completed || saved.Revision!=expectedRevision || sampled==null ||
                !ValidTime(sampled.ObservedAt) || sampled.ObservedAt<saved.AdvancedThrough || !JointSimulationParentsFree(saved.Base,sampled.ObservedAt))return false;
            var staged=HouseholdJointSimulation.Copy(saved);staged.ReleasedHomeAt=sampled.ObservedAt;
            staged.HomeArrival=HouseholdHomeArrival.Copy(sampled,staged);
            // All sampled IDs/positions and detached copies are checked before release.
            if(!ReleaseCompletedHouseholdSimulation(dayId,sampled.ObservedAt))return false;
            var released=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==dayId);released.HomeArrival=staged.HomeArrival;return true;
        }
    }
}
