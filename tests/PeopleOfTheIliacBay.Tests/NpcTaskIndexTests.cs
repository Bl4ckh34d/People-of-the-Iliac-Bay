using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcTaskIndexTests
    {
        private static NpcTaskPlan NewTask(string id, string residentId, double now)
        {
            var task = new NpcTaskPlan { Id = id, ProposalId = id, ResidentId = residentId,
                AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 300 };
            task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
                Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
            return task;
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Delivering an older failed task receipt preserves newer active task index", () =>
            {
                var store = new WorldStateStore(812);
                string residentId = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
                var failed = NewTask("task:old-failed", residentId, 100);
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, failed, true, out reason), reason);
                Check.True(store.FailNpcTask(failed.Id, failed.Revision, 101, "The first route is blocked."));
                failed = store.ReadNpcTask(failed.Id);

                var active = NewTask("task:new-active", residentId, 102);
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, active, true, out reason), reason);
                Check.Equal(active.Id, store.ActiveNpcTask(residentId).Id);

                NpcTaskFailureReceipt deliveredReceipt;
                Check.True(store.TryDeliverNpcTaskFailureReceipt(failed.Id, failed.Revision,
                    "day 1", "settlement 100", out deliveredReceipt), "Old failed task receipt should deliver.");
                Check.True(deliveredReceipt != null && deliveredReceipt.Delivered,
                    "Delivered failure receipt must be marked delivered.");
                Check.True(store.PendingNpcTaskFailureReceipt(residentId) == null,
                    "Delivered failure receipt must no longer be pending.");

                var stillActive = store.ActiveNpcTask(residentId);
                Check.True(stillActive != null && stillActive.Id == active.Id,
                    "Updating a terminal task must not erase a different active task for the same resident.");
                var storedActive = store.ReadNpcTask(active.Id);
                Check.Equal(active.Revision, storedActive.Revision);
                Check.Equal(active.Phase, storedActive.Phase);
                var pending = store.PendingNpcTasks();
                Check.Equal(1, pending.Count);
                Check.Equal(active.Id, pending[0].Id);

                var third = NewTask("task:third", residentId, 103);
                Check.True(!ClassifiedAdmissionFixture.TryAcceptTask(store, third, true, out reason),
                    "A stale terminal-task update must not let a third task bypass active-task exclusion.");
                Check.Equal(active.Id, store.ActiveNpcTask(residentId).Id);
            }));
        }
    }
}
