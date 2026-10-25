using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int Main()
    {
        int passed = 0;
        try { AcceptedCommitReturnsPreparedReceipt(); passed++; Console.WriteLine("PASS committed receipt"); }
        catch (Exception error) { Console.WriteLine("FAIL committed receipt: " + error); }
        try { RejectedCommitReturnsNoReceipt(); passed++; Console.WriteLine("PASS rejected commit"); }
        catch (Exception error) { Console.WriteLine("FAIL rejected commit: " + error); }
        try { ThrownCommitReportsUnknownOutcome(); passed++; Console.WriteLine("PASS unknown outcome"); }
        catch (Exception error) { Console.WriteLine("FAIL unknown outcome: " + error); }
        try { MissingReceiptNeverCallsCommit(); passed++; Console.WriteLine("PASS missing preflight"); }
        catch (Exception error) { Console.WriteLine("FAIL missing preflight: " + error); }
        try { FullStackRollbackRestoresObjectAndCoins(); passed++; Console.WriteLine("PASS full stack rollback"); }
        catch (Exception error) { Console.WriteLine("FAIL full stack rollback: " + error); }
        try { PartialStackRollbackPreservesCollision(); passed++; Console.WriteLine("PASS partial collision rollback"); }
        catch (Exception error) { Console.WriteLine("FAIL partial collision rollback: " + error); }
        try { FirstBasketMoveRestoresWhenSecondFails(); passed++; Console.WriteLine("PASS basket second-line rollback"); }
        catch (Exception error) { Console.WriteLine("FAIL basket second-line rollback: " + error); }
        try { SourceIdsAreStableAndSeparated(); passed++; Console.WriteLine("PASS source-bound identities"); }
        catch (Exception error) { Console.WriteLine("FAIL source-bound identities: " + error); }
        Console.WriteLine(passed + "/8 passed");
        return passed == 8 ? 0 : 1;
    }

    private static void AcceptedCommitReturnsPreparedReceipt()
    {
        object prepared = new object(), received;
        int commits = 0;
        bool accepted = PersonalTradeCommitBoundary.TryCommit(() => { commits++; return true; },
            prepared, out received);
        Need(accepted && ReferenceEquals(received, prepared) && commits == 1,
            "Successful store commit lost prepared observed receipt.");
    }

    private static void RejectedCommitReturnsNoReceipt()
    {
        object received;
        bool accepted = PersonalTradeCommitBoundary.TryCommit(() => false, new object(), out received);
        Need(!accepted && received == null, "Rejected store commit published receipt.");
    }

    private static void ThrownCommitReportsUnknownOutcome()
    {
        object received;
        try
        {
            PersonalTradeCommitBoundary.TryCommit<object>(
                () => { throw new InvalidOperationException("store failed"); }, new object(), out received);
            throw new InvalidOperationException("Thrown store commit was accepted.");
        }
        catch (PersonalTradeCommitOutcomeUnknownException error)
        {
            Need(error.InnerException != null && error.InnerException.Message == "store failed",
                "Fatal store exception lost cause.");
        }
    }

    private static void MissingReceiptNeverCallsCommit()
    {
        int commits = 0;
        object received;
        try
        {
            PersonalTradeCommitBoundary.TryCommit<object>(() => { commits++; return true; },
                null, out received);
            throw new InvalidOperationException("Missing observed receipt accepted.");
        }
        catch (ArgumentNullException)
        {
            Need(commits == 0, "Store committed before receipt preflight.");
        }
    }

    private static void FullStackRollbackRestoresObjectAndCoins()
    {
        var original = new FakeItem { Uid = 41, Count = 1 };
        var source = new FakeCollection();
        var target = new FakeCollection();
        target.Add(original);
        int gold = 7;
        Restore(original, original, 1, source, target, () => gold = 10, () => gold == 10);
        Need(ReferenceEquals(source.Get(41), original) && target.Get(41) == null &&
            original.Count == 1 && gold == 10, "Full-stack rollback lost source or coins.");
    }

    private static void PartialStackRollbackPreservesCollision()
    {
        var original = new FakeItem { Uid = 42, Count = 3 };
        var moved = new FakeItem { Uid = 43, Count = 2 };
        var unrelated = new FakeItem { Uid = 43, Count = 9 };
        var source = new FakeCollection();
        var target = new FakeCollection();
        source.Add(original);
        target.Add(unrelated); // Native AddItem would reject moved's colliding UID.
        int gold = 5;
        Restore(original, moved, 5, source, target, () => gold = 8, () => gold == 8);
        Need(ReferenceEquals(source.Get(42), original) && original.Count == 5 &&
            ReferenceEquals(target.Get(43), unrelated) && unrelated.Count == 9 &&
            gold == 8, "Rollback removed unrelated destination UID or lost coins.");
    }

    // Basket bridge arms this same boundary before the first move. Simulate a
    // completed first split/move and a failure before its second line mutates.
    private static void FirstBasketMoveRestoresWhenSecondFails()
    {
        var original = new FakeItem { Uid = 51, Count = 3 };
        var moved = new FakeItem { Uid = 52, Count = 2 };
        var source = new FakeCollection(); source.Add(original);
        var target = new FakeCollection(); target.Add(moved);
        int gold = 14;
        Restore(original, moved, 5, source, target, () => gold = 21, () => gold == 21);
        Need(ReferenceEquals(source.Get(51), original) && original.Count == 5 && target.Get(52) == null &&
            gold == 21, "Second-line failure did not restore the first basket move exactly.");
    }

    private static void SourceIdsAreStableAndSeparated()
    {
        string transaction = new string('x', 240);
        string receipt = PersonalTradeCommitBoundary.SourceBoundId("receipt", transaction);
        string effect = PersonalTradeCommitBoundary.SourceBoundId("effect", transaction);
        Need(receipt == PersonalTradeCommitBoundary.SourceBoundId("receipt", transaction) &&
            effect == PersonalTradeCommitBoundary.SourceBoundId("effect", transaction) &&
            receipt != effect && receipt.Length <= 240 && effect.Length <= 240 &&
            receipt != PersonalTradeCommitBoundary.SourceBoundId("receipt", transaction + "x"),
            "Retry identities were not stable, bounded, distinct and source-bound.");
    }

    private static void Restore(FakeItem original, FakeItem moved, int count,
        FakeCollection source, FakeCollection target, Action restoreGold, Func<bool> goldMatches)
    {
        PersonalTradeCommitBoundary.Restore(original, moved, count,
            item => item.Uid, source.Get, target.Get, source.Add, source.Remove,
            target.Remove, (item, value) => item.Count = value, restoreGold, goldMatches);
    }

    private sealed class FakeItem
    {
        internal ulong Uid;
        internal int Count;
    }

    private sealed class FakeCollection
    {
        private readonly Dictionary<ulong, FakeItem> items = new Dictionary<ulong, FakeItem>();
        internal FakeItem Get(ulong uid) { FakeItem found; return items.TryGetValue(uid, out found) ? found : null; }
        internal void Add(FakeItem item) { if (!items.ContainsKey(item.Uid)) items.Add(item.Uid, item); }
        internal void Remove(FakeItem item) { items.Remove(item.Uid); } // DFU removal is UID-keyed.
    }

    private static void Need(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
