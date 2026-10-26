using System;
namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class GuardRoster
    {
        public void TakeRefuge(string id,GuardRefugeJourney journey,double now,System.Collections.Generic.List<SavedRoutePoint> surveyedHome=null)
        {
            Require(Time(now),"Invalid refuge departure time.");
            var plan=GuardRefugeJourney.CopyValidated(journey);
            var preview=CopyValidated(this); var position=preview.Position(id,now);
            var guard=preview.Find(id);
            Require(guard.IsAlive && (guard.Duty==GuardDuty.Posted || guard.Duty==GuardDuty.Engaged) && guard.Refuge==null &&
                guard.Interior==null && plan.StartsAt==now && !plan.ReleasedAt.HasValue &&
                Distance(position,new SavedRoutePoint { X=plan.Exterior[0].X,Z=plan.Exterior[0].Z })<0.05,
                "Refuge departure requires an available guard and a matching surveyed route.");
            var home=guard.Duty==GuardDuty.Engaged ? CopyRoute(surveyedHome) : preview.PatrolReturn(id,now);
            if(guard.Duty==GuardDuty.Engaged)
                Require(home.Count>=2 && Distance(home[0],position)<0.05 && Distance(home[home.Count-1],guard.Post)<0.05,
                    "Combat refuge requires a surveyed return from current position to post.");
            plan.ReturnExterior=plan.Exterior.ConvertAll(p=>new GuardPoint3 { X=p.X,Y=p.Y,Z=p.Z });
            plan.ReturnExterior.Reverse();
            if(home!=null) for(int i=1;i<home.Count;i++)
                plan.ReturnExterior.Add(new GuardPoint3 { X=home[i].X,Z=home[i].Z });
            var last=plan.ReturnExterior[plan.ReturnExterior.Count-1];
            Require(Distance(guard.Post,new SavedRoutePoint { X=last.X,Z=last.Z })<0.05,"Refuge return must reach the post.");
            plan=GuardRefugeJourney.CopyValidated(plan);
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
            Advance(now); InterruptPatrol(id,now);
            var patrol=PatrolFor(id);
            if(patrol!=null)
            {
                if(patrol.Plan.LeaderId==id) patrol.LeaderDetached=true;
                else patrol.FollowerDetached=true;
            }
            guard=Find(id); guard.Refuge=plan; guard.Duty=GuardDuty.Refuge;
            guard.IncidentId=null; guard.DoorPursuit=null; guard.Route.Clear(); guard.ReturnPath=null; guard.ReturnRate=0;
            guard.ResponseOrigin=null; guard.EngagementPosition=null; guard.LastSeenSuspect=null;
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
