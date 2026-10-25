using System;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Read-only native observation point for a disposable-player Lead fixture.
    // Repeated calls can verify pause, resume and exact destination without
    // moving either actor or replacing the real TalkManager target source.
    public sealed class NpcLeadRuntimeSnapshot
    {
        public string ActionId, ResidentId;
        public int SettlementId, BuildingKey;
        public long Revision;
        public NpcActionPhase Phase;
        public NpcLeadProgress Progress;
        public double PlayerDistance, GoalDistance, DeadlineAt;
        public Vector3 ActorGround, GoalGround;
    }

    public sealed partial class StreetPopulation
    {
        public NpcLeadRuntimeSnapshot CaptureLeadRuntimeSnapshot(string residentId)
        {
            var game = GameManager.Instance;
            if (!Ready || worldStore == null || population == null || directory == null || geometry == null || interior ||
                game == null || game.PlayerEntity == null || game.PlayerMotor == null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-", StringComparison.Ordinal) ||
                (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-lead-probe") < 0 &&
                    Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-knowledge-browse-only") < 0 &&
                    Environment.GetEnvironmentVariable("POTIB_QUEST_PERSON_LOCATION_PROBE") != "1"))
                throw new InvalidOperationException("Lead probe requires a loaded exterior and owned disposable player.");
            var actor = FindActor(residentId);
            var job = worldStore.ActiveNpcAction(residentId);
            if (!actor || !actor.gameObject.activeInHierarchy || job == null ||
                job.Kind != NpcActionKind.Lead || job.Target == null ||
                job.Target.Kind != NpcActionTargetKind.Building)
                throw new InvalidOperationException("No live managed Lead actor with an exact building target.");
            Vector3 goal;
            float radius;
            string reason;
            if (!ResolveActionTarget(job, out goal, out radius, out reason))
                throw new InvalidOperationException("The saved Lead building entrance is no longer resolved.");
            var actual = actor.transform.position - Vector3.up;
            var player = game.PlayerMotor.transform.position - Vector3.up;
            double now = ActionNow();
            var progress = NpcLeadMotion.Evaluate(job, ActionPosition(actual), ActionPosition(goal),
                Vector3.Distance(actual, player), radius, now);
            return new NpcLeadRuntimeSnapshot { ActionId = job.Id, ResidentId = residentId,
                SettlementId = job.Target.SettlementId, BuildingKey = job.Target.BuildingKey,
                Revision = job.Revision, Phase = job.Phase, Progress = progress,
                PlayerDistance = Vector3.Distance(actual, player),
                GoalDistance = Vector3.Distance(actual, goal), DeadlineAt = job.DeadlineAt,
                ActorGround = actual, GoalGround = goal };
        }
    }
}
