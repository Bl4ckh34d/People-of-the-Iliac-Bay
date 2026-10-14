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
            var destination=plan.Interior[plan.Interior.Count-1];
            foreach (var other in preview.Guards)
            {
                if (other.Id==id || other.Refuge==null || other.Refuge.BuildingKey!=plan.BuildingKey) continue;
                var state=other.Refuge.Evaluate(other.IsAlive ? now : other.DiedAt);
                GuardPoint3 occupied;
                if (other.IsAlive)
                {
                    if (state.Phase==RefugePhase.Returning || state.Phase==RefugePhase.Complete) continue;
                    occupied=other.Refuge.Interior[other.Refuge.Interior.Count-1];
                }
                else
                {
                    if (state.Phase!=RefugePhase.Entering && state.Phase!=RefugePhase.Sheltered && state.Phase!=RefugePhase.Exiting) continue;
                    occupied=state.Position;
                }
                double dx=destination.X-occupied.X,dz=destination.Z-occupied.Z;
                Require(Math.Abs(destination.Y-occupied.Y)>=1.8 || dx*dx+dz*dz>=0.8*0.8,
                    "Refuge standing position is occupied or reserved.");
            }
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
