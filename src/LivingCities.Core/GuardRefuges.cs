using System;
namespace LivingCities.Core
{
    public sealed partial class GuardRoster
    {
        public void TakeRefuge(string id,GuardRefugeJourney journey,double now)
        {
            Require(Time(now),"Invalid refuge departure time.");
            var plan=GuardRefugeJourney.CopyValidated(journey);
            var preview=CopyValidated(this); var position=preview.Position(id,now);
            var guard=preview.Find(id);
            Require(guard.IsAlive && guard.Duty==GuardDuty.Posted && guard.Refuge==null &&
                guard.Interior==null && !preview.IsOnPatrol(id) && plan.StartsAt==now && !plan.ReleasedAt.HasValue &&
                Distance(position,guard.Post)<0.05 &&
                Distance(position,new SavedRoutePoint { X=plan.Exterior[0].X,Z=plan.Exterior[0].Z })<0.05,
                "Refuge departure requires a guard at its post and a matching surveyed route.");
            Advance(now); guard=Find(id); guard.Refuge=plan; guard.Duty=GuardDuty.Refuge;
        }
        private static SavedRoutePoint RefugeExteriorPosition(GuardRecord guard,double now)
        {
            var state=guard.Refuge.Evaluate(now);
            bool outside=state.Phase==RefugePhase.Approaching || state.Phase==RefugePhase.Returning || state.Phase==RefugePhase.Complete;
            var point=outside ? state.Position : guard.Refuge.Exterior[guard.Refuge.Exterior.Count-1];
            return new SavedRoutePoint { X=point.X,Z=point.Z };
        }
    }
}
