using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // Admission reaches this only after source, consent and live-obligation checks.
        // A full sweep is reserved for an actual hard-cap event, never the frame loop.
        private bool TryMakeNpcOperationCapacity(NpcOperationalHistoryPressureTarget target,
            double now, out string reason)
        {
            reason = null;
            bool tasks = target == NpcOperationalHistoryPressureTarget.Tasks;
            int limit = tasks ? 1000 : 10000;
            if ((tasks ? state.NpcTasks.Count : state.NpcActions.Count) < limit) return true;
            int remaining = state.NpcTasks.Count + (tasks ? 0 : state.NpcActions.Count);
            while (remaining > 0)
            {
                var result = MaintainNpcOperationalHistory(now, Math.Min(64, remaining), 1, true, target);
                if ((tasks ? state.NpcTasks.Count : state.NpcActions.Count) < limit) return true;
                if (result.Pressure == NpcOperationalHistoryPressure.ValidationRejected)
                {
                    reason = "The operation history could not be safely validated for cleanup.";
                    return false;
                }
                if (result.Examined == 0) break;
                remaining -= result.Examined;
            }
            reason = tasks
                ? "The task ledger is full: retained work is pinned by unresolved dependencies."
                : "The action ledger is full: retained work is pinned by unresolved dependencies.";
            return false;
        }
    }
}
