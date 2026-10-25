using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcLeadProgress { Invalid, Unobserved, Expired, WaitingForPlayer, Walking, Arrived }

    // Decision for a physically observed leader. The caller must resolve the
    // saved building key through current native geometry before supplying goal.
    public static class NpcLeadMotion
    {
        public const double MaximumPlayerDistance = 8;

        public static NpcLeadProgress Evaluate(NpcActionJob job, NpcActionTarget observed,
            NpcActionTarget resolvedGoal, double playerDistance, double arrivalRadius, double now)
        {
            if (job == null || job.Kind != NpcActionKind.Lead ||
                NpcActionExecutor.IsTerminal(job) || job.Target == null ||
                job.Target.Kind != NpcActionTargetKind.Building || job.Target.BuildingKey < 0 ||
                job.Target.SettlementId < 0 || job.LastPosition == null ||
                !SameExterior(job.LastPosition, job.Target.SettlementId))
                return NpcLeadProgress.Invalid;
            if (!Finite(now) || now < job.UpdatedAt) return NpcLeadProgress.Unobserved;
            if (now >= job.DeadlineAt) return NpcLeadProgress.Expired;
            if (observed == null || resolvedGoal == null || !Finite(playerDistance) ||
                !Finite(arrivalRadius) ||
                !Finite(observed.X) || !Finite(observed.Y) || !Finite(observed.Z) ||
                !Finite(resolvedGoal.X) || !Finite(resolvedGoal.Y) || !Finite(resolvedGoal.Z))
                return NpcLeadProgress.Unobserved;
            if (!SameExterior(observed, job.Target.SettlementId) ||
                !SameExterior(resolvedGoal, job.Target.SettlementId) ||
                playerDistance < 0 || arrivalRadius < 0)
                return NpcLeadProgress.Invalid;
            if (playerDistance > MaximumPlayerDistance) return NpcLeadProgress.WaitingForPlayer;
            double dx = observed.X - resolvedGoal.X;
            double dy = observed.Y - resolvedGoal.Y;
            double dz = observed.Z - resolvedGoal.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= arrivalRadius
                ? NpcLeadProgress.Arrived : NpcLeadProgress.Walking;
        }

        private static bool SameExterior(NpcActionTarget point, int settlement)
        {
            return point != null && point.Kind == NpcActionTargetKind.WorldPoint &&
                point.SettlementId == settlement &&
                point.WorldSpaceId == "settlement:" + settlement.ToString(CultureInfo.InvariantCulture);
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
