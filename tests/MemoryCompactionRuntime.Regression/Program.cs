using System;
using System.Collections.Generic;
using System.IO;
using PeopleOfTheIliacBay.DFU;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int assertions;

    private static void Main()
    {
        SchedulerWaitsForCalmIdleAndScansOneBatchPerTick();
        SchedulerDrainsPagedCompactionWithoutFinishingEarly();
        ModelBudgetRenewsAfterOneNativeDayWithoutLosingPageProgress();
        ModelBudgetSurvivesIdleJobCancellation();
        FourteenRealCompactionGroupsProgressAcrossRenewableBudget();
        ForegroundDispatchPreemptsWithoutCancellingForeground();
        IdleLossCancelsOnlyTheOwnedTicket();
        FinishedBackgroundTicketCanPublishFromItsCallback();
        StaleSaveOwnerHeadAndIdleCallbacksCannotPublish();
        NearbySpeakerIsDeferredAndCanBeRetried();
        InvalidOutputsAreBoundedAndSkipped();
        InvalidRetriesStayBoundedAcrossIdleCancellation();
        MissingCompactionCacheFallsBackToRawMemory();
        Console.WriteLine("Memory compaction runtime regressions passed: " + assertions + " assertions.");
    }

    private static void SchedulerWaitsForCalmIdleAndScansOneBatchPerTick()
    {
        Harness h = new Harness();
        h.Tick(0);
        h.Tick(9);
        Equal(0, h.Created.Count, "The job waits for a continuous calm-idle interval.");
        h.Tick(10);
        Equal(1, h.Created.Count, "One job starts after the calm-idle interval.");
        h.Tick(11);
        Equal(1, h.Created[0].ScanCalls, "The first eligible tick advances exactly one scan batch.");
        Equal(128, h.Created[0].LargestScanBatch, "Runtime scan batches are capped at 128 records.");
        h.Tick(11.1);
        Equal(1, h.Created[0].ScanCalls, "A second scan batch cannot run in the same tick interval.");
        h.Tick(12);
        Equal(2, h.Created[0].ScanCalls, "A later tick advances at most one additional scan batch.");
    }

    private static void ForegroundDispatchPreemptsWithoutCancellingForeground()
    {
        Harness h = new Harness();
        h.StartRequest();
        int foreground = h.Dialogue.StartForeground();
        Check.True(foreground > 0, "The real dispatch grants the foreground request after background preemption.");
        h.Tick(h.Now + 1);
        Check.True(h.Dialogue.Dispatch.IsCurrent(foreground), "Runtime cleanup leaves the foreground request current.");
        Equal(0, h.Dialogue.BackgroundCancelCalls, "Runtime never calls background cancel after foreground already preempted it.");
        Check.True(h.Created[0].Cancelled, "The compaction job is cancelled after losing its dispatch ticket.");
    }

    private static void SchedulerDrainsPagedCompactionWithoutFinishingEarly()
    {
        Harness h = new Harness(pageCount: 2);
        h.Tick(0);
        h.Tick(10);
        h.Tick(11);
        h.Tick(12);
        h.Tick(13);
        h.Tick(14);
        Equal(1, h.Dialogue.Requests, "The first page produces one bounded selection request.");
        h.Dialogue.Complete(h.Dialogue.LastTicket, "valid");
        Equal(1, h.Created[0].Accepted, "The first page is consumed before paging continues.");

        h.Tick(15);
        Equal(4, h.Created[0].ScanCalls, "The next page begins with exactly one scan batch after the prior page drains.");
        h.Tick(15.2);
        Equal(4, h.Created[0].ScanCalls, "Paging preserves the scan-spacing limit.");
        h.Tick(16);
        h.Tick(17);
        h.Tick(18);
        Equal(2, h.Dialogue.Requests, "The next page receives its own selection request.");
        h.Dialogue.Complete(h.Dialogue.LastTicket, "valid");
        Equal(2, h.Created[0].Accepted, "The second page selection is accepted.");

        h.Tick(19);
        Check.True(h.Created[0].Cancelled, "The job finishes only after the final page drains.");
        Equal(2, h.Created[0].CompletedPages, "The scheduler consumed both archive pages.");
    }

    private static void ModelBudgetRenewsAfterOneNativeDayWithoutLosingPageProgress()
    {
        Harness h = new Harness(pageCount: 14);
        h.Tick(0, 100);
        h.Tick(10, 110);
        h.Tick(11, 111);
        h.Tick(12, 112);
        h.Tick(13, 113);
        h.Tick(14, 114);

        for (int request = 1; request <= 12; request++)
        {
            Equal(request, h.Dialogue.Requests, "The scheduler issues no more than twelve selections in the first native-day budget window.");
            h.Dialogue.Complete(h.Dialogue.LastTicket, "valid");
            if (request < 12)
            {
                h.Tick(h.Now + 1, h.GameNow + 1);
                h.Tick(h.Now + 1, h.GameNow + 1);
                h.Tick(h.Now + 1, h.GameNow + 1);
                h.Tick(h.Now + 1, h.GameNow + 1);
            }
        }

        int scansAtBudgetLimit = h.Created[0].ScanCalls;
        double budgetEndGameTime = h.GameNow + 86400;
        h.Tick(h.Now + 1, h.GameNow + 1);
        h.Tick(h.Now + 1, budgetEndGameTime - 1);
        Equal(12, h.Dialogue.Requests, "The thirteenth selection remains paused until a full native game day has elapsed.");
        Equal(scansAtBudgetLimit, h.Created[0].ScanCalls, "The same paged job retains its scan position during the budget pause.");
        Check.True(!h.Created[0].Cancelled, "The scheduler keeps the existing job alive while its daily budget renews.");

        h.Tick(h.Now + 1, budgetEndGameTime);
        Equal(1, h.Created.Count, "Budget renewal resumes the same job rather than recreating the checkpoint scan.");
        h.Tick(h.Now + 1, h.GameNow + 1);
        h.Tick(h.Now + 1, h.GameNow + 1);
        h.Tick(h.Now + 1, h.GameNow + 1);
        Equal(13, h.Dialogue.Requests, "The same archive head makes progress after its bounded daily budget renews.");
    }

    private static void ModelBudgetSurvivesIdleJobCancellation()
    {
        Harness h = new Harness(pageCount: 14);
        h.Tick(0, 100);
        h.Tick(10, 110);
        h.Tick(11, 111);
        h.Tick(12, 112);
        h.Tick(13, 113);
        h.Tick(14, 114);
        for (int request = 1; request <= 12; request++)
        {
            h.Dialogue.Complete(h.Dialogue.LastTicket, "valid");
            if (request < 12)
            {
                h.Tick(h.Now + 1, h.GameNow + 1);
                h.Tick(h.Now + 1, h.GameNow + 1);
                h.Tick(h.Now + 1, h.GameNow + 1);
                h.Tick(h.Now + 1, h.GameNow + 1);
            }
        }
        Equal(12, h.Dialogue.Requests, "The first job spends exactly its bounded daily attempt budget.");
        double budgetEndGameTime = h.GameNow + 86400;

        h.State.Ready = false;
        h.Tick(h.Now + 1, h.GameNow + 1);
        Check.True(h.Created[0].Cancelled, "Gameplay idle loss cancels its own scan job.");
        h.State.Ready = true;
        h.Tick(h.Now + 1, budgetEndGameTime - 1);
        Equal(12, h.Dialogue.Requests, "Recreating runtime work before the renewal time does not reset the spent attempt budget.");
        Equal(1, h.Created.Count, "The scheduler does not create another checkpoint job during the exhausted budget window.");

        h.Tick(h.Now + 1, budgetEndGameTime);
        h.Tick(h.Now + 10, budgetEndGameTime + 10);
        h.Tick(h.Now + 1, budgetEndGameTime + 11);
        h.Tick(h.Now + 1, budgetEndGameTime + 12);
        h.Tick(h.Now + 1, budgetEndGameTime + 13);
        h.Tick(h.Now + 1, budgetEndGameTime + 14);
        Equal(13, h.Dialogue.Requests, "The same archive checkpoint receives a fresh bounded budget after one native day.");
    }

    private static void FourteenRealCompactionGroupsProgressAcrossRenewableBudget()
    {
        string root = Path.Combine(Path.GetTempPath(), "memory-compaction-runtime-groups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const double nativeMonth = 30d * 86400d;
            double gameTime = nativeMonth * 2 + 5;
            var store = new WorldStateStore(41, root);
            for (int index = 0; index < 14; index++)
            {
                string personId = "person-" + index.ToString("D2");
                store.RememberConversation(personId, 1, "old native date", "the inn",
                    "A remembered player statement " + index + ".", "An attributed NPC response " + index + ".", "native");
            }
            store.SnapshotForSave();
            ConversationArchiveCheckpointView checkpoint = store.CaptureConversationArchiveCheckpoint();
            var cache = new ConversationMemoryCompactionCache(Path.Combine(root, "DerivedCompaction"));
            var state = new MutableState
            {
                Owner = store,
                ArchiveId = checkpoint.ArchiveId,
                Head = checkpoint.Head
            };
            var dialogue = new DispatchDialogue();
            var jobs = new List<CoreRuntimeJobAdapter>();
            double wallTime = 0;
            var scheduler = new ConversationMemoryCompactionRuntimeScheduler(
                () => state.Capture(gameTime, wallTime, dialogue.IsBusy),
                captured =>
                {
                    var job = new ConversationMemoryCompactionJob(new SharedConversationArchive(root),
                        checkpoint, gameTime, cache);
                    var adapter = new CoreRuntimeJobAdapter(job);
                    jobs.Add(adapter);
                    return adapter;
                },
                dialogue,
                personId => false);
            Action<double, double> tick = (wall, game) =>
            {
                wallTime = wall;
                gameTime = game;
                scheduler.Tick();
            };

            tick(0, gameTime);
            tick(10, gameTime);
            tick(11, gameTime);
            tick(12, gameTime);
            Equal(1, dialogue.Requests, "The real Core compaction job begins selecting separate person/month groups.");
            for (int request = 1; request <= 12; request++)
            {
                Equal(request, dialogue.Requests, "The real archive job stays within twelve classifier requests in its first daily window.");
                dialogue.Complete(dialogue.LastTicket, "{\"ids\":[1]}");
                if (request < 12) tick(wallTime + 1, gameTime);
            }
            Check.True(!jobs[0].IsCancelled, "The same real compaction job stays alive at the daily budget limit.");
            Equal(12, jobs[0].Accepted, "Twelve real source groups publish through the Core job before the budget pause.");

            double renewedAt = gameTime + 86400;
            tick(wallTime + 1, renewedAt - 1);
            Equal(12, dialogue.Requests, "The thirteenth real group cannot be requested before the native-day window renews.");
            tick(wallTime + 1, renewedAt);
            Equal(13, dialogue.Requests, "The unchanged archive head continues with group thirteen after renewal.");
            Equal(1, jobs.Count, "Renewal continues the original Core job instead of restarting its archive scan.");
            Check.True(!jobs[0].IsCancelled, "The original Core job remains active while the next group is requested.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void FinishedBackgroundTicketCanPublishFromItsCallback()
    {
        Harness h = new Harness();
        int ticket = h.StartRequest();
        Check.True(h.Dialogue.Dispatch.IsBackgroundCurrent(ticket), "The background ticket is current before delivery.");
        h.Dialogue.Complete(ticket, "valid");
        Check.True(!h.Dialogue.Dispatch.IsBackgroundCurrent(ticket), "Fake client finishes dispatch before invoking its callback, matching LocalDialogueClient.Update.");
        Equal(1, h.Created[0].Accepted, "A valid callback is accepted using owned scheduler stamps after TryFinish.");
    }

    private static void IdleLossCancelsOnlyTheOwnedTicket()
    {
        Harness h = new Harness();
        int ticket = h.StartRequest();
        h.State.Ready = false;
        h.Tick(h.Now + 1);
        Check.True(!h.Dialogue.Dispatch.IsBusy, "Runtime idle loss cancels its current background request.");
        Equal(1, h.Dialogue.BackgroundCancelCalls, "Only the compaction-owned ticket is cancelled.");
        Check.True(h.Created[0].Cancelled, "The associated scan is cancelled when runtime idle ends.");
    }

    private static void StaleSaveOwnerHeadAndIdleCallbacksCannotPublish()
    {
        AssertStaleCallback((state, h) => state.SaveEpoch++, "save epoch change");
        AssertStaleCallback((state, h) => state.Head = "new-head", "archive head change");
        AssertStaleCallback((state, h) => state.Owner = new object(), "store owner replacement");
        AssertStaleCallback((state, h) => state.Ready = false, "runtime idle loss");
    }

    private static void AssertStaleCallback(Action<MutableState, Harness> mutate, string label)
    {
        Harness h = new Harness();
        int ticket = h.StartRequest();
        mutate(h.State, h);
        h.Dialogue.Complete(ticket, "valid");
        Equal(0, h.Created[0].Accepted, "A callback cannot publish after " + label + ".");
        Check.True(h.Created[0].Cancelled, "The stale job is cancelled after " + label + ".");
    }

    private static void NearbySpeakerIsDeferredAndCanBeRetried()
    {
        Harness h = new Harness();
        h.Nearby = true;
        h.Tick(0);
        h.Tick(10);
        h.Tick(11);
        h.Tick(12);
        h.Tick(13);
        h.Tick(14);
        Check.True(h.Created[0].ScanComplete, "The job reaches a complete source scan while the last known speaker is nearby.");
        Equal(0, h.Created[0].Skipped, "A nearby speaker's group remains pending instead of being permanently skipped.");
        Equal(0, h.Dialogue.Requests, "No semantic classifier request runs while the relevant speaker is nearby.");
        h.Nearby = false;
        h.Tick(20);
        Equal(1, h.Dialogue.Requests, "The still-pending group is requested after bounded wall-clock backoff when the speaker leaves.");
    }

    private static void InvalidOutputsAreBoundedAndSkipped()
    {
        Harness h = new Harness();
        int ticket = h.StartRequest();
        h.Dialogue.Complete(ticket, "invalid");
        Equal(0, h.Created[0].Skipped, "The first invalid answer gets a bounded retry.");
        h.Tick(h.Now + 31);
        ticket = h.Dialogue.LastTicket;
        Check.True(ticket > 0, "The bounded retry is dispatched after backoff.");
        h.Dialogue.Complete(ticket, "invalid");
        Equal(0, h.Created[0].Skipped, "The second invalid answer remains within the retry budget.");
        h.Tick(h.Now + 61);
        ticket = h.Dialogue.LastTicket;
        Check.True(ticket > 0, "The final bounded attempt is dispatched after increased backoff.");
        h.Dialogue.Complete(ticket, "invalid");
        Equal(1, h.Created[0].Skipped, "The invalid source group is skipped after the fixed attempt limit.");
        int requests = h.Dialogue.Requests;
        h.Tick(h.Now + 1000);
        Equal(requests, h.Dialogue.Requests, "A skipped invalid group cannot create an unbounded request loop.");
    }

    private static void InvalidRetriesStayBoundedAcrossIdleCancellation()
    {
        Harness h = new Harness();
        int ticket = h.StartRequest();
        h.Dialogue.Complete(ticket, "invalid");
        Equal(0, h.Created[0].Skipped, "The first invalid result remains pending across an idle interruption.");

        h.State.Ready = false;
        h.Tick(15);
        h.State.Ready = true;
        h.Tick(16);
        h.Tick(26);
        h.Tick(27);
        h.Tick(28);
        h.Tick(29);
        h.Tick(44);
        ticket = h.Dialogue.LastTicket;
        Check.True(ticket > 0, "The same checkpoint resumes after calm idle and the first backoff.");
        h.Dialogue.Complete(ticket, "invalid");
        Equal(0, h.Created[1].Skipped, "The second invalid result is not mistaken for a fresh retry budget.");

        h.State.Ready = false;
        h.Tick(45);
        h.State.Ready = true;
        h.Tick(46);
        h.Tick(56);
        h.Tick(57);
        h.Tick(58);
        h.Tick(59);
        h.Tick(104);
        ticket = h.Dialogue.LastTicket;
        Check.True(ticket > 0, "The final bounded retry resumes after a second idle interruption.");
        h.Dialogue.Complete(ticket, "invalid");
        Equal(1, h.Created[2].Skipped, "The third invalid result skips the request after the fixed retry limit.");
        int requests = h.Dialogue.Requests;
        h.Tick(105);
        h.Tick(1000);
        Equal(requests, h.Dialogue.Requests, "Idle interruptions cannot restart an unbounded request loop for one checkpoint.");
    }

    private static void MissingCompactionCacheFallsBackToRawMemory()
    {
        string root = Path.Combine(Path.GetTempPath(), "memory-compaction-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new WorldStateStore(9, root);
            store.RememberConversation("alice", 1, "native date", "the inn",
                "I mentioned the sapphire brooch.", "I remember the sapphire brooch.", "native");
            store.SnapshotForSave();
            var cache = new ConversationMemoryCompactionCache(Path.Combine(root, "DerivedCompaction"));
            string raw = store.RecallConversation("alice", "sapphire", 100, 2000);
            string recalled = store.RecallConversationWithCompaction("alice", "sapphire", 100, 2000, cache);
            Check.True(raw.Contains("sapphire"), "The fixture contains a raw archived memory hit.");
            Check.True(recalled.Contains("sapphire"), "A missing derived cache falls back to the archived raw memory hit.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static class Check
    {
        public static void True(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + " Expected=" + expected + "; actual=" + actual + ".");
    }

    private sealed class Harness
    {
        public readonly MutableState State = new MutableState();
        public readonly DispatchDialogue Dialogue = new DispatchDialogue();
        public readonly List<FakeJob> Created = new List<FakeJob>();
        public ConversationMemoryCompactionRuntimeScheduler Scheduler;
        public double Now;
        public double GameNow;
        public bool Nearby;

        public Harness(int pageCount = 1)
        {
            Scheduler = new ConversationMemoryCompactionRuntimeScheduler(
                () => State.Capture(GameNow, Now, Dialogue.IsBusy),
                state => { var job = new FakeJob(pageCount); Created.Add(job); return job; },
                Dialogue,
                personId => Nearby && personId == "alice");
        }

        public void Tick(double now)
        {
            Now = now;
            GameNow = now;
            Scheduler.Tick();
        }

        public void Tick(double wallTime, double gameTime)
        {
            Now = wallTime;
            GameNow = gameTime;
            Scheduler.Tick();
        }

        public int StartRequest()
        {
            Tick(0);
            Tick(10);
            Tick(11);
            Tick(12);
            Tick(13);
            Tick(14);
            Equal(1, Dialogue.Requests, "Harness starts one classifier request.");
            return Dialogue.LastTicket;
        }
    }

    private sealed class MutableState
    {
        public object Owner = new object();
        public long SaveEpoch = 4;
        public string ArchiveId = "archive";
        public string Head = "head";
        public bool Ready = true;

        public ConversationMemoryCompactionRuntimeState Capture(double gameTime, double wallTime, bool modelBusy)
        {
            return new ConversationMemoryCompactionRuntimeState(Owner, SaveEpoch, ArchiveId, Head,
                gameTime, wallTime, Ready, modelBusy);
        }
    }

    private sealed class FakeJob : IConversationMemoryCompactionRuntimeJob
    {
        private readonly int pageCount;
        private int pageIndex;
        private int scansOnPage;
        private bool pageResolved;
        public int ScanCalls;
        public int LargestScanBatch;
        public int Accepted;
        public int Skipped;
        public bool Cancelled;
        public int CompletedPages { get { return pageIndex + (pageResolved ? 1 : 0); } }
        public bool ScanComplete { get { return scansOnPage >= 3; } }
        public bool HasMorePages { get { return pageIndex + 1 < pageCount; } }
        public bool IsCancelled { get { return Cancelled; } }

        public FakeJob(int pageCount) { this.pageCount = pageCount; }

        public void AdvanceScan(int maxRecords)
        {
            if (ScanComplete)
            {
                pageIndex++;
                scansOnPage = 0;
                pageResolved = false;
            }
            ScanCalls++;
            scansOnPage++;
            LargestScanBatch = Math.Max(LargestScanBatch, maxRecords);
        }

        public bool TryGetSelectionRequest(out ConversationMemoryCompactionRuntimeSelection request)
        {
            request = new ConversationMemoryCompactionRuntimeSelection("request-" + pageIndex, "alice", "prompt", "grammar", 128);
            return ScanComplete && !Cancelled && !pageResolved;
        }

        public bool AcceptSelection(string requestId, string response)
        {
            if (Cancelled || response != "valid" || requestId != "request-" + pageIndex) return false;
            Accepted++;
            pageResolved = true;
            return true;
        }

        public void SkipSelection(string requestId) { Skipped++; pageResolved = true; }
        public void Cancel() { Cancelled = true; }
    }

    private sealed class CoreRuntimeJobAdapter : IConversationMemoryCompactionRuntimeJob
    {
        private readonly ConversationMemoryCompactionJob job;
        public int Accepted;
        public bool ScanComplete { get { return job.ScanComplete; } }
        public bool HasMorePages { get { return job.HasMorePages; } }
        public bool IsCancelled { get { return job.IsCancelled; } }

        public CoreRuntimeJobAdapter(ConversationMemoryCompactionJob job) { this.job = job; }

        public void AdvanceScan(int maximumRecords) { job.AdvanceScan(maximumRecords); }

        public bool TryGetSelectionRequest(out ConversationMemoryCompactionRuntimeSelection request)
        {
            ConversationMemoryCompactionSelectionRequest coreRequest;
            if (!job.TryGetSelectionRequest(out coreRequest)) { request = null; return false; }
            request = new ConversationMemoryCompactionRuntimeSelection(coreRequest.RequestId, coreRequest.PersonId,
                coreRequest.Prompt, coreRequest.Grammar, coreRequest.ResponseTokens);
            return true;
        }

        public bool AcceptSelection(string requestId, string response)
        {
            bool accepted = job.AcceptSelection(requestId, response);
            if (accepted) Accepted++;
            return accepted;
        }

        public void SkipSelection(string requestId) { job.SkipSelection(requestId); }
        public void Cancel() { job.Cancel(); }
    }

    private sealed class DispatchDialogue : IConversationMemoryCompactionDialogue
    {
        private readonly Dictionary<int, Action<string>> callbacks = new Dictionary<int, Action<string>>();
        public readonly LocalDialogueDispatch Dispatch = new LocalDialogueDispatch();
        public int Requests;
        public int LastTicket;
        public int BackgroundCancelCalls;
        public bool IsBusy { get { return Dispatch.IsBusy; } }

        public bool IsBackgroundCurrent(int ticket) { return Dispatch.IsBackgroundCurrent(ticket); }

        public int TryClassifyBackground(string prompt, string grammar, int responseTokens, Action<string> complete)
        {
            int ticket;
            if (!Dispatch.TryBeginBackground(out ticket)) return 0;
            Requests++;
            LastTicket = ticket;
            callbacks[ticket] = complete;
            return ticket;
        }

        public bool CancelBackground(int ticket)
        {
            BackgroundCancelCalls++;
            return Dispatch.CancelBackground(ticket);
        }

        public int StartForeground() { return Dispatch.TryBegin(); }

        public void Complete(int ticket, string response)
        {
            Action<string> callback;
            if (!callbacks.TryGetValue(ticket, out callback)) return;
            if (!Dispatch.TryFinish(ticket)) return;
            callbacks.Remove(ticket);
            callback(response);
        }
    }
}
