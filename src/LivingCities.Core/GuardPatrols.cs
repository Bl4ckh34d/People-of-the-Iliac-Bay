using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class GuardPatrolAssignment
    {
        public PatrolPairPlan Plan;
        public double? InterruptedAt;
        public bool LeaderDetached, FollowerDetached;
    }
    public sealed partial class GuardRoster
    {
        public List<GuardPatrolAssignment> Patrols = new List<GuardPatrolAssignment>();
        public bool IsOnPatrol(string id) { return PatrolFor(id) != null; }
        public bool IsPatrolMoving(string id, double now)
        {
            var guard = Find(id);
            return guard.IsAlive && guard.Duty == GuardDuty.Posted && PatrolFor(id) != null &&
                Distance(PostedPosition(guard, now), PostedPosition(guard, now + 0.05)) > 0.000001;
        }
        public void StopPatrol(string memberId, double now)
        {
            Require(Time(now), "Invalid patrol stop time."); Find(memberId);
            Advance(now); InterruptPatrol(memberId, now);
        }
        public void StartPatrol(PatrolPairPlan plan, double now)
        {
            var copy = PatrolPairPlan.CopyValidated(plan);
            Require(Time(now) && copy.StartsAt >= now, "Patrol cannot start in the past.");
            foreach (var id in new[] { copy.LeaderId, copy.FollowerId })
            {
                var guard = Find(id);
                Require(guard.IsAlive && guard.Duty == GuardDuty.Posted && guard.Interior == null &&
                    Distance(PositionPreview(id, now), guard.Post) < 0.05 &&
                    !Patrols.Exists(p => Member(p, id) && now < PatrolFinishedAt(p)), "Guard is unavailable for patrol.");
                var join = id == copy.LeaderId ? copy.LeaderJoin : copy.FollowerJoin;
                Require(Distance(join[0], guard.Post) < 0.05, "Patrol must join from the assigned post.");
            }
            Advance(now); Patrols.Add(new GuardPatrolAssignment { Plan = copy });
        }
        private SavedRoutePoint PositionPreview(string id, double now)
        { return CopyValidated(this).Position(id, now); }
        private static bool Member(GuardPatrolAssignment patrol, string id)
        { return patrol.Plan.LeaderId == id && !patrol.LeaderDetached || patrol.Plan.FollowerId == id && !patrol.FollowerDetached; }
        private GuardPatrolAssignment PatrolFor(string id)
        { return Patrols.Find(p => Member(p, id)); }
        private static double PatrolFinishedAt(GuardPatrolAssignment patrol)
        {
            double cut = patrol.InterruptedAt ?? patrol.Plan.EndsAt;
            return cut + Math.Max(Length(patrol.Plan.ReturnRoute(patrol.Plan.LeaderId, cut)),
                Length(patrol.Plan.ReturnRoute(patrol.Plan.FollowerId, cut))) / patrol.Plan.Speed * patrol.Plan.TimeScale;
        }
        private SavedRoutePoint PostedPosition(GuardRecord guard, double now)
        {
            var patrol = PatrolFor(guard.Id);
            if (patrol == null) return CopyPoint(guard.Post);
            if (!patrol.InterruptedAt.HasValue) return patrol.Plan.Position(guard.Id, now);
            var route = patrol.Plan.ReturnRoute(guard.Id, patrol.InterruptedAt.Value);
            return Along(route, Math.Max(0, now - patrol.InterruptedAt.Value) * patrol.Plan.Speed / patrol.Plan.TimeScale);
        }
        private List<SavedRoutePoint> PatrolReturn(string id, double now)
        {
            var patrol = PatrolFor(id);
            if (patrol == null) return null;
            if (!patrol.InterruptedAt.HasValue) return patrol.Plan.ReturnRoute(id, now);
            var route = patrol.Plan.ReturnRoute(id, patrol.InterruptedAt.Value);
            return Remaining(route, Math.Max(0, now - patrol.InterruptedAt.Value) * patrol.Plan.Speed / patrol.Plan.TimeScale);
        }
        private void InterruptPatrol(string id, double now)
        {
            var patrol = PatrolFor(id);
            if (patrol != null && now <= patrol.Plan.StartsAt) { Patrols.Remove(patrol); return; }
            if (patrol != null && !patrol.InterruptedAt.HasValue) patrol.InterruptedAt = now;
        }
        private static SavedRoutePoint Along(List<SavedRoutePoint> route, double along)
        {
            for (int i = 1; i < route.Count; i++)
            {
                double distance = Distance(route[i - 1], route[i]);
                if (distance <= 0) continue;
                if (along <= distance) return new SavedRoutePoint { X = route[i - 1].X + (route[i].X - route[i - 1].X) * along / distance,
                    Z = route[i - 1].Z + (route[i].Z - route[i - 1].Z) * along / distance };
                along -= distance;
            }
            return CopyPoint(route[route.Count - 1]);
        }
        private static List<SavedRoutePoint> Remaining(List<SavedRoutePoint> route, double along)
        {
            var result = new List<SavedRoutePoint> { Along(route, along) }; double walked = 0;
            for (int i = 1; i < route.Count; i++)
            { walked += Distance(route[i - 1], route[i]); if (walked > along) result.Add(CopyPoint(route[i])); }
            if (result.Count == 1) result.Add(CopyPoint(result[0])); return result;
        }
        private static List<SavedRoutePoint> ResponseReturn(GuardRecord guard, List<SavedRoutePoint> response,
            List<SavedRoutePoint> patrolReturn)
        {
            var result = CopyRoute(response); result.Reverse();
            if (patrolReturn == null) return result;
            int post = result.FindIndex(p => Distance(p, guard.Post) < 0.05);
            if (post >= 0) result.RemoveRange(post + 1, result.Count - post - 1);
            else for (int i = 1; i < patrolReturn.Count; i++) result.Add(CopyPoint(patrolReturn[i]));
            if (result.Count == 1) result.Add(CopyPoint(result[0])); return result;
        }
        private static void BeginReturn(GuardRecord guard, double now)
        {
            double duration = guard.ArrivesAt - guard.DepartedAt;
            if (guard.ReturnPath != null)
            { guard.Route = CopyRoute(guard.ReturnPath); duration = Length(guard.Route) * guard.ReturnRate; }
            else guard.Route.Reverse();
            guard.DepartedAt = now; guard.ArrivesAt = now + duration;
            guard.SearchUntil = now; guard.Duty = GuardDuty.Returning;
        }
        private void CopyPatrols(List<GuardPatrolAssignment> source)
        {
            if (source == null) return;
            Require(source.Count <= 128, "Too many patrol pairs.");
            var assigned = new HashSet<string>();
            foreach (var patrol in source)
            {
                Require(patrol != null, "Missing patrol assignment.");
                var plan = PatrolPairPlan.CopyValidated(patrol.Plan);
                var copy = new GuardPatrolAssignment { Plan = plan, InterruptedAt = patrol.InterruptedAt,
                    LeaderDetached=patrol.LeaderDetached, FollowerDetached=patrol.FollowerDetached };
                Require(!(copy.LeaderDetached || copy.FollowerDetached) || copy.InterruptedAt.HasValue,"Detached patrol must be interrupted.");
                Require(!copy.InterruptedAt.HasValue || Time(copy.InterruptedAt.Value) && copy.InterruptedAt.Value >= plan.StartsAt &&
                    copy.InterruptedAt.Value <= PatrolFinishedAt(new GuardPatrolAssignment { Plan = plan }), "Invalid patrol interruption time.");
                Require(Time(PatrolFinishedAt(copy)), "Patrol return overflows time.");
                foreach (var id in new[] { plan.LeaderId, plan.FollowerId })
                {
                    var guard = Guards.Find(g => g.Id == id);
                    Require(guard!=null,"Missing patrol member.");
                    if(!Member(copy,id)) continue;
                    var join = id == plan.LeaderId ? plan.LeaderJoin : plan.FollowerJoin;
                    Require(guard != null && assigned.Add(id) && Distance(join[0], guard.Post) < 0.05 &&
                        (guard.IsAlive && guard.Duty == GuardDuty.Posted || copy.InterruptedAt.HasValue), "Invalid patrol member or post.");
                }
                Patrols.Add(copy);
            }
        }
    }
}
