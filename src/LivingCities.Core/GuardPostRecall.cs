using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class GuardPostRecall
    {
        public List<SavedRoutePoint> Route;
        public double DepartsAt, ArrivesAt, ClosesAt;
        public string OutboundIncidentId;
        public GuardRefugeJourney WallDuty;
    }

    public sealed partial class GuardRoster
    {
        public void ScheduleWallDuty(string id,GuardRefugeJourney journey)
        {
            var guard=Find(id); var recall=guard.PostRecall;
            var plan=GuardRefugeJourney.CopyValidated(journey);
            ValidateWallDuty(recall,plan);
            Require(!Guards.Exists(g=>g.Id!=id && g.IsAlive &&
                (g.PostRecall!=null && g.PostRecall.WallDuty!=null && g.PostRecall.WallDuty.ConflictsWith(plan.BuildingKey,plan.RestBedRecord) ||
                 g.Refuge!=null && g.Refuge.ConflictsWith(plan.BuildingKey,plan.RestBedRecord))),"Wall quarter already reserved.");
            recall.WallDuty=plan;
        }
        private static void ValidateWallDuty(GuardPostRecall recall,GuardRefugeJourney plan)
        {
            Require(recall!=null && plan.WallDuty && plan.ReleasedAt.HasValue &&
                plan.StartsAt==recall.DepartsAt && plan.ReturnExterior!=null &&
                plan.Exterior.Count>=recall.Route.Count,"Wall duty requires a scheduled gate recall.");
            for(int i=0;i<recall.Route.Count;i++)
                Require(Distance(recall.Route[i],new SavedRoutePoint { X=plan.Exterior[i].X,Z=plan.Exterior[i].Z })<0.05,
                    "Wall duty must follow the surveyed gate crossing.");
            var last=plan.ReturnExterior[plan.ReturnExterior.Count-1];
            Require(Distance(recall.Route[recall.Route.Count-1],new SavedRoutePoint {X=last.X,Z=last.Z})<0.05,
                "Wall duty must return to the inner staging point.");
            Require(Math.Abs(Length(recall.Route)*plan.SecondsPerMetre-(recall.ArrivesAt-recall.DepartsAt))<0.01,
                "Wall duty cannot change the gate crossing speed.");
        }
        // A surveyed return is scheduled while the gate is open. Advance needs no loaded geometry.
        public void SchedulePostRecall(string id,List<SavedRoutePoint> route,double departure,
            double closesAt,double speed,double timeScale)
        {
            var guard=Find(id); var points=CopyRoute(route);
            Require(guard.IsAlive && guard.Interior==null && guard.Refuge==null &&
                (guard.Duty==GuardDuty.Posted || guard.Duty==GuardDuty.Returning &&
                guard.IncidentId!=null && guard.IncidentId.StartsWith("post:",StringComparison.Ordinal)),
                "Guard cannot schedule a post recall during an emergency.");
            Require(points.Count>=2 && Distance(points[0],guard.Post)<0.05 && Length(points)>0 &&
                Time(departure) && Time(closesAt) && Finite(speed) && speed>0 && Finite(timeScale) && timeScale>0,
                "Invalid gate recall route or time.");
            double arrival=departure+Length(points)/speed*timeScale;
            Require(Time(arrival) && arrival<closesAt && departure>=guard.ArrivesAt,
                "Gate recall cannot finish before closure.");
            guard.PostRecall=new GuardPostRecall { Route=points,DepartsAt=departure,ArrivesAt=arrival,
                ClosesAt=closesAt,OutboundIncidentId=guard.Duty==GuardDuty.Returning ? guard.IncidentId : null };
        }

        private static GuardPostRecall CopyPostRecall(GuardPostRecall recall)
        {
            if(recall==null) return null;
            var route=CopyRoute(recall.Route);
            if(recall.WallDuty!=null) ValidateWallDuty(recall,GuardRefugeJourney.CopyValidated(recall.WallDuty));
            Require(route.Count>=2 && Length(route)>0 && Time(recall.DepartsAt) && Time(recall.ArrivesAt) &&
                Time(recall.ClosesAt) && recall.DepartsAt<recall.ArrivesAt && recall.ArrivesAt<recall.ClosesAt &&
                (recall.OutboundIncidentId==null || recall.OutboundIncidentId.StartsWith("post:",StringComparison.Ordinal)),
                "Invalid saved gate recall.");
            return new GuardPostRecall { Route=route,DepartsAt=recall.DepartsAt,ArrivesAt=recall.ArrivesAt,
                ClosesAt=recall.ClosesAt,OutboundIncidentId=recall.OutboundIncidentId,
                WallDuty=recall.WallDuty==null ? null : GuardRefugeJourney.CopyValidated(recall.WallDuty) };
        }

        private void CancelInterruptedRecall(GuardRecord guard)
        {
            var recall=guard.PostRecall; if(recall==null) return;
            if(!guard.IsAlive || guard.Interior!=null || guard.Refuge!=null ||
                Distance(guard.Post,recall.Route[0])>=0.05 ||
                !(guard.Duty==GuardDuty.Posted || guard.Duty==GuardDuty.Returning &&
                    guard.IncidentId==recall.OutboundIncidentId)) guard.PostRecall=null;
        }

        private void AdvancePostRecall(GuardRecord guard,double now)
        {
            var recall=guard.PostRecall;
            if(recall==null || now<recall.DepartsAt || guard.Duty!=GuardDuty.Posted) return;
            guard.PostRecall=null;
            // A changed assignment, late arrival or occupied destination cannot be backdated through a closed gate.
            if(guard.ArrivesAt>recall.DepartsAt || IsOnPatrol(guard.Id) ||
                Guards.Exists(g=>g.Id!=guard.Id && g.IsAlive && Distance(g.Post,recall.Route[recall.Route.Count-1])<0.8)) return;
            string report="gate-recall:"+guard.Id+":"+recall.DepartsAt.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            Reports.Add(new GuardReport { Id=report,GuardIds=new List<string> { guard.Id } });
            guard.Post=CopyPoint(recall.Route[recall.Route.Count-1]);
            if(recall.WallDuty!=null)
            {
                guard.Refuge=GuardRefugeJourney.CopyValidated(recall.WallDuty); guard.Duty=GuardDuty.Refuge;
                guard.DepartedAt=recall.DepartsAt; guard.ArrivesAt=recall.ArrivesAt;
                guard.Route.Clear(); guard.IncidentId=null; BuildingApproaches.Clear();
                if(guard.Refuge.Evaluate(now).Phase==RefugePhase.Complete)
                { guard.Refuge=null; guard.Duty=GuardDuty.Posted; }
                return;
            }
            guard.Route=CopyRoute(recall.Route); guard.IncidentId=report; guard.Duty=GuardDuty.Returning;
            guard.DepartedAt=guard.SearchUntil=recall.DepartsAt; guard.ArrivesAt=recall.ArrivesAt;
            BuildingApproaches.Clear();
            if(now>=guard.ArrivesAt)
            { guard.Duty=GuardDuty.Posted; guard.Route.Clear(); guard.IncidentId=null; }
        }
    }
}
