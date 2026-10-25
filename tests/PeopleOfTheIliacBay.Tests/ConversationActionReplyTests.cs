using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationActionReplyTests
    {
        private static WorldStateStore Store(out string residentId)
        {
            var store = new WorldStateStore(58);
            residentId = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
            return store;
        }

        private static NpcActionJob WaitAction(string id, string residentId, double now)
        {
            var position = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 100, BuildingKey = 10 };
            return new NpcActionJob
            {
                Id = id,
                ProposalId = id,
                ResidentId = residentId,
                Kind = NpcActionKind.Wait,
                Target = position,
                Phase = NpcActionPhase.Accepted,
                Revision = 1,
                AcceptedAt = now,
                UpdatedAt = now,
                DeadlineAt = now + 100,
                FinishAt = now + 10
            };
        }

        private static NpcTaskPlan TwoStepTask(string residentId)
        {
            var target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 100, BuildingKey = 10 };
            var task = new NpcTaskPlan
            {
                Id = "task:reply",
                ProposalId = "turn:reply",
                ResidentId = residentId,
                AcceptedAt = 100,
                UpdatedAt = 100,
                DeadlineAt = 500
            };
            task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.MoveTo, Target = NpcActionExecutor.CopyTarget(target) });
            task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, Target = NpcActionExecutor.CopyTarget(target), DurationSeconds = 10 });
            return task;
        }

        private static NpcActionJob TaskStep(NpcTaskPlan task, double now)
        {
            var step = task.Steps[task.CurrentStepIndex];
            return new NpcActionJob
            {
                Id = NpcTaskContract.ActionId(task),
                ProposalId = NpcTaskContract.ActionId(task),
                ResidentId = task.ResidentId,
                Kind = step.Kind,
                Target = NpcActionExecutor.CopyTarget(step.Target),
                Phase = NpcActionPhase.Accepted,
                Revision = 1,
                AcceptedAt = now,
                UpdatedAt = now,
                DeadlineAt = task.DeadlineAt,
                FinishAt = step.DurationSeconds > 0 ? now + step.DurationSeconds : 0
            };
        }

        private static void CompleteCurrentTaskStep(WorldStateStore store, string taskId, double start, string effectId)
        {
            var task = store.ReadNpcTask(taskId);
            var job = TaskStep(task, start);
            string reason;
            Check.True(store.TryDispatchNpcTaskStep(taskId, task.Revision, start, job, true, true, true, out reason), reason);
            Check.True(store.ObserveNpcAction(job.Id, job.Revision, start + 1, NpcActionObservation.Arrival()));
            var executing = store.ReadNpcAction(job.Id);
            double completeAt = Math.Max(start + 2, executing.FinishAt);
            Check.True(store.ObserveNpcAction(job.Id, executing.Revision, completeAt, NpcActionObservation.Success(effectId)));
            task = store.ReadNpcTask(taskId);
            Check.True(store.ReconcileNpcTask(taskId, task.Revision, completeAt));
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Action reply status comes from saved typed outcomes, not dialogue prose", new Action(() => {
                var noChange = ConversationActionReply.NoChange();
                var rejected = ConversationActionReply.Rejected(null);
                var unverified = ConversationActionReply.Unverified();
                Check.Equal(ConversationActionReplyStatus.NoChange, noChange.Status);
                Check.Equal(ConversationActionReplyStatus.Rejected, rejected.Status);
                Check.Equal(ConversationActionReplyStatus.Unknown, unverified.Status);
                Check.True(noChange.ReceiptKey == null && rejected.ReceiptKey == null);
                Check.True(unverified.Source == ConversationActionReplySource.None && unverified.ReceiptKey == null);
                Check.True(!string.IsNullOrWhiteSpace(noChange.NpcText) && !string.IsNullOrWhiteSpace(rejected.NpcText));
                Check.Equal("I can't confirm that yet.", unverified.NpcText);
                Check.True(noChange.NpcText.Length <= 480 && rejected.NpcText.Length <= 480);
                Check.True(noChange.NpcText.StartsWith("I ", StringComparison.Ordinal));
                Check.True(rejected.NpcText.StartsWith("I ", StringComparison.Ordinal));
                Check.Equal("I haven't agreed to do anything new.", noChange.NpcText);
                Check.Equal("I can't agree to that.", rejected.NpcText);
                Check.True(!noChange.NpcText.Contains("completed", StringComparison.OrdinalIgnoreCase));
                Check.True(!rejected.NpcText.Contains("completed", StringComparison.OrdinalIgnoreCase));
                Check.True(!unverified.NpcText.Contains("agree", StringComparison.OrdinalIgnoreCase));
                Check.True(!unverified.NpcText.Contains("can't agree", StringComparison.OrdinalIgnoreCase));
                Check.True(!unverified.NpcText.Contains("completed", StringComparison.OrdinalIgnoreCase));
            })));

            cases.Add(("Unresolved action reply preserves bounded native reason without action authority", new Action(() => {
                const string reason = "Need a destination before I can do that.";
                var unresolved = ConversationActionReply.Unresolved(reason);
                var noChange = ConversationActionReply.NoChange();
                Check.Equal(ConversationActionReplyStatus.NoChange, unresolved.Status);
                Check.Equal(ConversationActionReplySource.None, unresolved.Source);
                Check.Equal(reason, unresolved.UnresolvedReason);
                Check.True(unresolved.ReceiptKey == null && unresolved.SourceId == null && unresolved.PersonId == null);
                Check.Equal("I haven't agreed to do anything new.", unresolved.NpcText);
                Check.True(noChange.UnresolvedReason == null);
                Check.Equal("I haven't agreed to do anything new.", noChange.NpcText);
                Check.Throws<ArgumentException>(() => ConversationActionReply.Unresolved(null));
                Check.Throws<ArgumentException>(() => ConversationActionReply.Unresolved(" \t"));
                Check.Throws<ArgumentException>(() => ConversationActionReply.Unresolved(new string('x', 513)));
                Check.Throws<ArgumentException>(() => ConversationActionReply.Unresolved("bad\ud800"));
                Check.Equal(512, ConversationActionReply.Unresolved(new string('x', 512)).UnresolvedReason.Length);
            })));

            cases.Add(("Saved action phases yield bounded honest first-person receipts", new Action(() => {
                string residentId;
                var acceptedStore = Store(out residentId);
                var acceptedJob = WaitAction("action:reply", residentId, 100);
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(acceptedStore, acceptedJob, true, true, true, out reason), reason);

                var forgedCopy = acceptedStore.ReadNpcAction(acceptedJob.Id);
                forgedCopy.Phase = NpcActionPhase.Completed;
                forgedCopy.CompletedEffectId = "fabricated:effect";
                ConversationActionReply reply;
                Check.True(ConversationActionReply.TryFromSavedAction(acceptedStore, acceptedJob.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.AcceptedPending, reply.Status);
                Check.Equal(NpcActionKind.Wait, reply.ActionKind.Value);
                Check.Equal(acceptedJob.Id, reply.SourceId);
                Check.Equal(residentId, reply.PersonId);
                Check.True(reply.EffectReceiptId == null);
                Check.Equal("I agree. I'll get started once we've finished talking.", reply.NpcText);
                Check.True(!reply.NpcText.Contains("completed", StringComparison.OrdinalIgnoreCase));
                Check.True(reply.MatchesSavedState(acceptedStore));
                string originalKey = reply.ReceiptKey;
                Check.True(ConversationActionReply.TryFromSavedAction(acceptedStore, acceptedJob.Id, out reply));
                Check.Equal(originalKey, reply.ReceiptKey);

                Check.True(acceptedStore.ObserveNpcAction(acceptedJob.Id, 1, 101, NpcActionObservation.Arrival()));
                Check.True(!reply.MatchesSavedState(acceptedStore), "An old accepted receipt must fail after its saved revision advances.");
                Check.True(ConversationActionReply.TryFromSavedAction(acceptedStore, acceptedJob.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.InProgress, reply.Status);
                Check.True(reply.ReceiptKey != originalKey);
                Check.True(!reply.NpcText.Contains("completed", StringComparison.OrdinalIgnoreCase));

                var completedStore = Store(out residentId);
                var completedJob = WaitAction("action:complete", residentId, 100);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(completedStore, completedJob, true, true, true, out reason), reason);
                Check.True(completedStore.ObserveNpcAction(completedJob.Id, 1, 101, NpcActionObservation.Arrival()));
                var executing = completedStore.ReadNpcAction(completedJob.Id);
                string effectId = completedJob.Id + ":observed";
                Check.True(completedStore.ObserveNpcAction(completedJob.Id, executing.Revision, 110, NpcActionObservation.Success(effectId)));
                Check.True(ConversationActionReply.TryFromSavedAction(completedStore, completedJob.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.Completed, reply.Status);
                Check.Equal(effectId, reply.EffectReceiptId);
                Check.Equal(1, reply.CompletedSteps);
                Check.Equal(1, reply.TotalSteps);
                Check.Equal("I've finished what we agreed.", reply.NpcText);
                Check.True(!ConversationActionReply.TryFromSavedAction(completedStore, "missing", out reply));
            })));

            cases.Add(("Saved action failure and cancellation receipts cannot become accepted promises", new Action(() => {
                foreach (NpcActionPhase terminal in new[] { NpcActionPhase.Failed, NpcActionPhase.Cancelled })
                {
                    string residentId;
                    var store = Store(out residentId);
                    var job = WaitAction("action:" + terminal.ToString().ToLowerInvariant(), residentId, 100);
                    string reason;
                    Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job, true, true, true, out reason), reason);
                    string detail = terminal == NpcActionPhase.Failed ? "The route is blocked." : "The player stopped the action.";
                    Check.True(store.ObserveNpcAction(job.Id, job.Revision, 101,
                        terminal == NpcActionPhase.Failed ? NpcActionObservation.Interrupted(detail) : NpcActionObservation.Cancelled(detail)));
                    ConversationActionReply reply;
                    Check.True(ConversationActionReply.TryFromSavedAction(store, job.Id, out reply));
                    Check.Equal(terminal == NpcActionPhase.Failed ? ConversationActionReplyStatus.Failed : ConversationActionReplyStatus.Cancelled, reply.Status);
                    Check.True(!reply.NpcText.Contains("will ", StringComparison.OrdinalIgnoreCase));
                    Check.True(reply.EffectReceiptId == null);
                }
            })));

            cases.Add(("Saved task reply distinguishes pending progress from all-step completion receipts", new Action(() => {
                string residentId;
                var store = Store(out residentId);
                var task = TwoStepTask(residentId);
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task, true, out reason), reason);

                ConversationActionReply reply;
                Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.AcceptedPending, reply.Status);
                Check.Equal(task.Id, reply.SourceId);
                Check.Equal(residentId, reply.PersonId);
                Check.Equal(0, reply.CompletedSteps);
                Check.Equal(2, reply.TotalSteps);
                Check.True(reply.EffectReceiptId == null);
                Check.True(!reply.NpcText.Contains("completed", StringComparison.OrdinalIgnoreCase));
                Check.True(reply.MatchesSavedState(store));
                string pendingKey = reply.ReceiptKey;

                CompleteCurrentTaskStep(store, task.Id, 100, "effect:move");
                Check.True(!reply.MatchesSavedState(store), "A task reply cannot remain current after a step receipt changes the saved revision.");
                Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.AcceptedPending, reply.Status);
                Check.Equal(1, reply.CompletedSteps);
                Check.Equal(2, reply.TotalSteps);
                Check.True(reply.EffectReceiptId == null);
                Check.True(reply.ReceiptKey != pendingKey);
                Check.Equal("I completed 1 of 2 steps. I haven't started the next step yet.", reply.NpcText);
                Check.True(reply.MatchesSavedState(store));

                CompleteCurrentTaskStep(store, task.Id, 120, "effect:wait");
                Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.Completed, reply.Status);
                Check.Equal(2, reply.CompletedSteps);
                Check.Equal(2, reply.TotalSteps);
                Check.True(!string.IsNullOrWhiteSpace(reply.EffectReceiptId));
                Check.Equal("I've finished what we agreed.", reply.NpcText);
                Check.True(reply.MatchesSavedState(store));
                Check.True(!ConversationActionReply.TryFromSavedTask(store, "missing", out reply));
            })));

            cases.Add(("Saved task failure and cancellation replies reflect source state", new Action(() => {
                foreach (bool cancelled in new[] { false, true })
                {
                    string residentId;
                    var store = Store(out residentId);
                    var task = TwoStepTask(residentId);
                    string reason;
                    Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task, true, out reason), reason);
                    Check.True(cancelled
                        ? store.CancelNpcTask(task.Id, task.Revision, 101, "The player ended the task.")
                        : store.FailNpcTask(task.Id, task.Revision, 101, "No safe route is available."));
                    ConversationActionReply reply;
                    Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out reply));
                    Check.Equal(cancelled ? ConversationActionReplyStatus.Cancelled : ConversationActionReplyStatus.Failed, reply.Status);
                    Check.True(reply.EffectReceiptId == null);
                    Check.True(!reply.NpcText.Contains("will ", StringComparison.OrdinalIgnoreCase));
                    Check.True(reply.MatchesSavedState(store));
                }
            })));

            cases.Add(("Task receipt key follows active action phase when task revision is unchanged", new Action(() => {
                string residentId;
                var store = Store(out residentId);
                var task = TwoStepTask(residentId);
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task, true, out reason), reason);
                var job = TaskStep(task, 100);
                Check.True(store.TryDispatchNpcTaskStep(task.Id, task.Revision, 100, job, true, true, true, out reason), reason);

                ConversationActionReply before;
                Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out before));
                Check.Equal(ConversationActionReplyStatus.AcceptedPending, before.Status);
                long taskRevision = store.ReadNpcTask(task.Id).Revision;
                Check.True(store.ObserveNpcAction(job.Id, job.Revision, 101, NpcActionObservation.Approach()));
                ConversationActionReply approaching;
                Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out approaching));
                Check.Equal(ConversationActionReplyStatus.InProgress, approaching.Status);
                Check.Equal(taskRevision, approaching.SourceRevision);
                string approachingKey = approaching.ReceiptKey;
                Check.True(store.ObserveNpcAction(job.Id, store.ReadNpcAction(job.Id).Revision, 102, NpcActionObservation.Arrival()));
                ConversationActionReply executing;
                Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out executing));
                Check.Equal(ConversationActionReplyStatus.InProgress, executing.Status);
                Check.Equal(taskRevision, executing.SourceRevision);
                Check.True(executing.ReceiptKey != approachingKey);
                Check.True(!approaching.MatchesSavedState(store));
                Check.True(executing.MatchesSavedState(store));
            })));

            cases.Add(("Failed stop reply keeps saved provenance without agreeing to the stop", new Action(() => {
                string residentId;
                var store = Store(out residentId);
                var job = WaitAction("action:failed-stop", residentId, 100);
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job, true, true, true, out reason), reason);
                ConversationActionReply accepted;
                Check.True(ConversationActionReply.TryFromSavedAction(store, job.Id, out accepted));

                var failedStop = accepted.ForUnsuccessfulCancellation();
                Check.Equal(ConversationActionReplyStatus.AcceptedPending, failedStop.Status);
                Check.Equal(accepted.Source, failedStop.Source);
                Check.Equal(accepted.SourceId, failedStop.SourceId);
                Check.Equal(accepted.PersonId, failedStop.PersonId);
                Check.Equal(accepted.SourceRevision, failedStop.SourceRevision);
                Check.Equal(accepted.EffectReceiptId, failedStop.EffectReceiptId);
                Check.Equal("I couldn't stop it. It hasn't started yet.", failedStop.NpcText);
                Check.True(!failedStop.NpcText.Contains("agree", StringComparison.OrdinalIgnoreCase));
                Check.True(failedStop.MatchesSavedState(store));

                var inProgressStore = Store(out residentId);
                var inProgressJob = WaitAction("action:failed-stop-progress", residentId, 100);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(inProgressStore, inProgressJob, true, true, true, out reason), reason);
                Check.True(inProgressStore.ObserveNpcAction(inProgressJob.Id, inProgressJob.Revision, 101, NpcActionObservation.Approach()));
                ConversationActionReply state;
                Check.True(ConversationActionReply.TryFromSavedAction(inProgressStore, inProgressJob.Id, out state));
                Check.Equal("I couldn't stop it. I'm still working on what we agreed.", state.ForUnsuccessfulCancellation().NpcText);

                var completedStore = Store(out residentId);
                var completedJob = WaitAction("action:failed-stop-complete", residentId, 100);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(completedStore, completedJob, true, true, true, out reason), reason);
                Check.True(completedStore.ObserveNpcAction(completedJob.Id, completedJob.Revision, 101, NpcActionObservation.Arrival()));
                var executing = completedStore.ReadNpcAction(completedJob.Id);
                Check.True(completedStore.ObserveNpcAction(executing.Id, executing.Revision, 110,
                    NpcActionObservation.Success(completedJob.Id + ":effect")));
                Check.True(ConversationActionReply.TryFromSavedAction(completedStore, completedJob.Id, out state));
                Check.Equal("That has already finished.", state.ForUnsuccessfulCancellation().NpcText);

                var failedStore = Store(out residentId);
                var failedJob = WaitAction("action:failed-stop-failed", residentId, 100);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(failedStore, failedJob, true, true, true, out reason), reason);
                Check.True(failedStore.ObserveNpcAction(failedJob.Id, failedJob.Revision, 101,
                    NpcActionObservation.Interrupted("The route is blocked.")));
                Check.True(ConversationActionReply.TryFromSavedAction(failedStore, failedJob.Id, out state));
                Check.Equal("That could not be completed.", state.ForUnsuccessfulCancellation().NpcText);

                var cancelledStore = Store(out residentId);
                var cancelledJob = WaitAction("action:failed-stop-cancelled", residentId, 100);
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(cancelledStore, cancelledJob, true, true, true, out reason), reason);
                Check.True(cancelledStore.ObserveNpcAction(cancelledJob.Id, cancelledJob.Revision, 101,
                    NpcActionObservation.Cancelled("The player stopped the action.")));
                Check.True(ConversationActionReply.TryFromSavedAction(cancelledStore, cancelledJob.Id, out state));
                Check.Equal("That has already been stopped.", state.ForUnsuccessfulCancellation().NpcText);

                var noSource = ConversationActionReply.Unverified().ForUnsuccessfulCancellation();
                Check.Equal(ConversationActionReplyStatus.Unknown, noSource.Status);
                Check.Equal("I can't confirm that yet.", noSource.NpcText);
            })));

            cases.Add(("Action receipt wording stays first-person and partial task outcomes report completed counts", new Action(() => {
                string residentId;
                var pendingStore = Store(out residentId);
                var pendingAction = WaitAction("action:wording", residentId, 100);
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(pendingStore, pendingAction, true, true, true, out reason), reason);
                ConversationActionReply reply;
                Check.True(ConversationActionReply.TryFromSavedAction(pendingStore, pendingAction.Id, out reply));
                Check.Equal("I agree. I'll get started once we've finished talking.", reply.NpcText);
                Check.True(reply.NpcText.StartsWith("I ", StringComparison.Ordinal));
                Check.Equal("I'm still working on what we agreed.", RenderInProgress(pendingStore, pendingAction.Id));

                var store = Store(out residentId);
                var task = TwoStepTask(residentId);
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task, true, out reason), reason);
                CompleteCurrentTaskStep(store, task.Id, 100, "effect:first");
                task = store.ReadNpcTask(task.Id);
                Check.True(store.FailNpcTask(task.Id, task.Revision, 120, "The second step cannot be completed."));
                Check.True(ConversationActionReply.TryFromSavedTask(store, task.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.Failed, reply.Status);
                Check.Equal(1, reply.CompletedSteps);
                Check.Equal(2, reply.TotalSteps);
                Check.Equal("I completed 1 of 2 steps, but couldn't finish the task.", reply.NpcText);

                var cancelledStore = Store(out residentId);
                var cancelledTask = TwoStepTask(residentId);
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(cancelledStore, cancelledTask, true, out reason), reason);
                CompleteCurrentTaskStep(cancelledStore, cancelledTask.Id, 100, "effect:cancel-first");
                cancelledTask = cancelledStore.ReadNpcTask(cancelledTask.Id);
                Check.True(cancelledStore.CancelNpcTask(cancelledTask.Id, cancelledTask.Revision, 120, "The player ended the task."));
                Check.True(ConversationActionReply.TryFromSavedTask(cancelledStore, cancelledTask.Id, out reply));
                Check.Equal(ConversationActionReplyStatus.Cancelled, reply.Status);
                Check.Equal(1, reply.CompletedSteps);
                Check.Equal(2, reply.TotalSteps);
                Check.Equal("I stopped after completing 1 of 2 steps.", reply.NpcText);
            })));
        }

        private static string RenderInProgress(WorldStateStore store, string actionId)
        {
            var action = store.ReadNpcAction(actionId);
            Check.True(store.ObserveNpcAction(action.Id, action.Revision, 101, NpcActionObservation.Arrival()));
            ConversationActionReply reply;
            Check.True(ConversationActionReply.TryFromSavedAction(store, actionId, out reply));
            Check.Equal(ConversationActionReplyStatus.InProgress, reply.Status);
            return reply.NpcText;
        }
    }
}
