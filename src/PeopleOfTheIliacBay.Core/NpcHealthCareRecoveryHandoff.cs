using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>
    /// Releases an interrupted care task only after its physical return has a
    /// saved origin. This keeps an external cure from snapping a travelling
    /// resident straight back to the clock-derived routine.
    /// </summary>
    public static class NpcHealthCareRecoveryHandoff
    {
        public static bool TryComplete(WorldStateStore store, string personId, long expectedCareRevision,
            NpcArchetype archetype, double now, NpcActionTarget observedPosition)
        {
            if (store == null || string.IsNullOrWhiteSpace(personId) ||
                double.IsNaN(now) || double.IsInfinity(now)) return false;

            NpcHealthCareRecord care = store.ReadNpcHealthCare(personId);
            if (care == null || care.Revision != expectedCareRevision ||
                care.Phase == NpcHealthCarePhase.Recovered || now < care.UpdatedAt) return false;
            NpcVitalsView vitals;
            try { vitals = store.ReadNpcVitals(personId, archetype); }
            catch (ArgumentException) { return false; }
            if (vitals == null || !vitals.IsPersisted || !vitals.IsAlive ||
                !NpcHealthNeedLifecycle.HasRecoveredForWork(vitals) || vitals.CapturedAt > now) return false;

            NpcTaskPlan task = care.TaskId == null ? null : store.ReadNpcTask(care.TaskId);
            if (care.TaskId != null && (task == null || task.ResidentId != personId)) return false;
            NpcActionJob action = task == null || task.ActiveActionId == null ? null :
                store.ReadNpcAction(task.ActiveActionId);
            NpcActionTarget returnPosition = null;
            if (action != null && !action.ReturningToRoutine &&
                !NpcHealthCare.TryCopyStaticPosition(observedPosition, out returnPosition)) return false;

            if (task != null && !NpcTaskContract.IsTerminal(task))
            {
                if (!store.CancelNpcTask(task.Id, task.Revision, now,
                    "Recovery is already confirmed; further treatment is unnecessary.")) return false;
                task = store.ReadNpcTask(task.Id);
                action = task.ActiveActionId == null ? null : store.ReadNpcAction(task.ActiveActionId);
            }

            if (action != null)
            {
                if (!NpcActionExecutor.IsTerminal(action)) return false;
                if (!action.ReturningToRoutine)
                {
                    if (returnPosition == null &&
                        !NpcHealthCare.TryCopyStaticPosition(observedPosition, out returnPosition)) return false;
                    if (!store.SaveNpcActionPosition(action.Id, action.Revision, returnPosition, true, now)) return false;
                }
            }

            // A failed position save leaves terminal task/action state recoverable:
            // a later call can attach the observed return before completing care.
            return store.TryCompleteNpcHealthCare(personId, expectedCareRevision, archetype, now);
        }
    }
}
