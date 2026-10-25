using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private NpcLeadProgress ObserveLeadProgress(NpcActionJob job, ResidentActor actor, double now)
        {
            var game = GameManager.Instance;
            if (job == null || job.Target == null || job.Target.Kind != NpcActionTargetKind.Building ||
                population != null && job.Target.SettlementId != population.SettlementId || interior)
                return NpcLeadProgress.Invalid;
            if (now >= job.DeadlineAt) return NpcLeadProgress.Expired;
            if (!actor || population == null || directory == null || geometry == null ||
                game == null || game.PlayerMotor == null)
                return NpcLeadProgress.Unobserved;
            Vector3 goal;
            float radius;
            string reason;
            if (!ResolveActionTarget(job, out goal, out radius, out reason))
                return NpcLeadProgress.Unobserved;
            Vector3 actual = actor.transform.position - Vector3.up;
            Vector3 player = game.PlayerMotor.transform.position - Vector3.up;
            return NpcLeadMotion.Evaluate(job, ActionPosition(actual), ActionPosition(goal),
                Vector3.Distance(actual, player), radius, now);
        }
    }
}
