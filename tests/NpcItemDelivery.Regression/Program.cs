using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Test)> {
            ("NPC delivery projects retained UID to recipient exactly once", ReceiptProjects),
            ("NPC delivery records only both participants and survives restore", ParticipantMemories),
            ("receipt name survives item departure and later memory projection", NameSurvivesDeparture),
            ("native item name is bounded validated and part of receipt identity", InvalidNames),
            ("native display labels normalize without refusing physical transfer", NativeNamesNormalize),
            ("receipt rejects wrong participants and player destination", InvalidParticipants),
            ("receipt rejects wrong item quantity time and direction", InvalidTransfer),
            ("uncommitted transfer cannot create recipient memory", NoPrematureMemory)
        };
        int passed = 0;
        foreach (var test in cases) try { test.Test(); passed++; Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.GetBaseException().Message); }
        Console.WriteLine(passed + "/" + cases.Count + " passed");
        return passed == cases.Count ? 0 : 1;
    }

    private static WorldStateStore Fixture(out NpcActionJob job, out string bystander)
    {
        var store = new WorldStateStore(42);
        var people = store.GetOrCreate(100, new[] { 10, 11, 12 }, 3).Households;
        string source = people[0].Residents[0].Id, recipient = people[1].Residents[0].Id;
        bystander = people[2].Residents[0].Id;
        job = new NpcActionJob { Id = "delivery", ProposalId = "delivery-offer", ResidentId = source,
            Kind = NpcActionKind.Transfer, Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person,
                PersonId = recipient, ItemId = "777", Quantity = 2 },
            Phase = NpcActionPhase.Executing, Revision = 2, AcceptedAt = 10, UpdatedAt = 11, DeadlineAt = 100 };
        return store;
    }

    private static NpcActionTransferReceipt Receipt(NpcActionJob job, string recipient = null, string source = null,
        ulong uid = 777, int quantity = 2, double at = 12, string itemName = "Steel Saber")
    {
        var method = typeof(NpcActionTransferReceipt).GetMethod("CreateNpcToNpc", BindingFlags.Public | BindingFlags.Static);
        Need(method != null, "NPC-to-NPC durable receipt factory is missing.");
        return (NpcActionTransferReceipt)method.Invoke(null, new object[] { job, job.Id + ":transfer", uid,
            778UL, 779UL, quantity, recipient ?? job.Target.PersonId, source ?? job.ResidentId, at,
            NpcExperienceNativeValuationStatus.Unknown, 0L, itemName });
    }

    private static void Complete(WorldStateStore store, NpcActionJob job)
    {
        job.ItemTransferReceipt = Receipt(job);
        job.Phase = NpcActionPhase.Completed; job.Revision = 3; job.UpdatedAt = 12;
        job.CompletedEffectId = job.Id + ":observed";
        var state = store.Snapshot(); state.NpcActions.Add(job); store.Restore(state);
    }

    private static void ReceiptProjects()
    {
        var store = Fixture(out var job, out var bystander); Complete(store, job);
        Need(store.DrainNpcItemTransferReceipts(32) == 1, "Receipt did not enqueue.");
        var entry = store.Snapshot().NpcExperienceInbox.Entries[0];
        Need(entry.ObserverId == job.Target.PersonId && entry.SubjectPersonId == job.Target.PersonId &&
            entry.ResponsibleActorId == job.ResidentId && entry.ReceivedItemUid == 779 && entry.TransferQuantity == 2 &&
            entry.EventKind == NpcExperienceEventKind.ItemReceived && entry.TransferCurrency == 0,
            "Recipient, actual giver, merged UID or neutral transfer facts changed.");
        Need(store.DrainNpcItemTransferReceipts(32) == 0, "Receipt replayed.");
        var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
        Need(restored.ReadNpcAction(job.Id).ItemTransferReceipt.NativeItemName == "Steel Saber" &&
            restored.ReadNpcAction(job.Id).ProjectedItemTransferReceipt.NativeItemName == "Steel Saber",
            "Receipt or projection marker lost actual native name.");
        Need(restored.DrainNpcItemTransferReceipts(32) == 0 && restored.Snapshot().NpcExperienceInbox.Entries.Count == 1,
            "Restored receipt replayed.");
        var corrupt = store.Snapshot(); corrupt.NpcActions[0].ItemTransferReceipt.CounterpartyId = bystander;
        Reject(() => new WorldStateStore(0).Restore(corrupt));
    }

    private static void ParticipantMemories()
    {
        var store = Fixture(out var job, out var bystander); Complete(store, job);
        Need(store.TryRememberNpcActionOutcome(job.Id, 3, "day 1", "shop", "untrusted gift and purchase claim"), "Memory rejected.");
        var journals = store.Snapshot().ConversationMemories;
        Need(journals.Count == 2, "Only actor received memory, or bystander learned unseen transfer.");
        foreach (var person in new[] { job.ResidentId, job.Target.PersonId })
        {
            var entries = journals.Find(x => x.PersonId == person).Entries;
            Need(entries.Count == 1 && entries[0].SourceActionId == job.Id && entries[0].At == 12,
                "Participant receipt memory missing or duplicated.");
            Need(entries[0].Observation.Contains("Steel Saber") && !entries[0].Observation.Contains("untrusted") &&
                !entries[0].Observation.Contains("UID"),
                "Memory must name the actual Steel Saber; UIDs belong in structured evidence.");
            Need(store.RecallConversation(person, "Steel Saber", 13).Contains("Steel Saber"), "Named item absent from participant context.");
        }
        Need(!store.RecallConversation(bystander, "Steel Saber", 13).Contains("Steel Saber"), "Bystander learned unseen receipt.");
        Need(!store.TryRememberNpcActionOutcome(job.Id, 3, "day 1", "shop", "duplicate"), "Memory replayed.");
        var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
        Need(!restored.TryRememberNpcActionOutcome(job.Id, 3, "day 1", "shop", "duplicate"), "Restored memory replayed.");
    }

    private static void NameSurvivesDeparture()
    {
        var store = Fixture(out var job, out _); Complete(store, job);
        Need(store.InitializeNpcInventory(job.Target.PersonId, 0, new List<NpcInventoryItem> {
            new NpcInventoryItem { Uid = 779, NativePayload = "native item fixture" } }), "Inventory setup failed.");
        Need(store.CommitNpcInventoryTransaction("later-departure", new[] { new NpcInventoryUpdate {
            PersonId = job.Target.PersonId, ExpectedRevision = 0, WalletGold = 0, Items = new List<NpcInventoryItem>() } }),
            "Later inventory departure failed.");
        var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
        Need(restored.ReadNpcInventory(job.Target.PersonId).Items.Count == 0, "Item did not leave inventory.");
        Need(restored.TryRememberNpcActionOutcome(job.Id, 3, "day 1", "shop", "invented replacement"), "Later memory projection failed.");
        foreach (var person in new[] { job.ResidentId, job.Target.PersonId })
            Need(restored.RecallConversation(person, "Steel Saber", 20).Contains("Steel Saber"),
                "Receipt lost actual item name after inventory departure and restore.");
        Need(!restored.TryRememberNpcActionOutcome(job.Id, 3, "day 2", "shop", "duplicate"), "Named memory replayed.");
    }

    private static void InvalidNames()
    {
        var store = Fixture(out var job, out _);
        Reject(() => Receipt(job, itemName: null)); Reject(() => Receipt(job, itemName: ""));
        Reject(() => Receipt(job, itemName: " Steel Saber ")); Reject(() => Receipt(job, itemName: "Steel\nSaber"));
        Reject(() => Receipt(job, itemName: new string('x', 241)));
        Complete(store, job); Need(store.DrainNpcItemTransferReceipts(32) == 1, "Projection failed.");
        var corrupt = store.Snapshot(); corrupt.NpcActions[0].ProjectedItemTransferReceipt.NativeItemName = "Iron Dagger";
        Reject(() => new WorldStateStore(0).Restore(corrupt));
        corrupt = store.Snapshot(); corrupt.NpcActions[0].ItemTransferReceipt.NativeItemName = null;
        Reject(() => new WorldStateStore(0).Restore(corrupt));
    }

    private static void NativeNamesNormalize()
    {
        var type = Assembly.GetExecutingAssembly().GetType("PeopleOfTheIliacBay.DFU.NativeItemReceiptLabel");
        Need(type != null, "Native display-name normalization is missing.");
        var capture = type.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        Need(capture != null, "Native display-name capture is missing.");
        foreach (var nativeName in new[] { null, "", " \t\r\n\0", "Steel Saber", "\t Steel\r\n Saber\0 ", new string('x', 500) })
        {
            var name = (string)capture.Invoke(null, new object[] { nativeName });
            Need(!string.IsNullOrWhiteSpace(name) && name.Length <= 240 && name == name.Trim(), "Display name was not bounded/readable.");
            Fixture(out var job, out _); Receipt(job, itemName: name);
            job.Target.TransferDirection = NpcActionTransferDirection.PlayerToNpc; job.Target.PersonId = "player";
            NpcActionTransferReceipt.CreatePlayerToNpc(job, job.Id + ":transfer", 777, 778, 779, 2,
                job.ResidentId, "player", 12, NpcExperienceNativeValuationStatus.Unknown, 0, name);
            if (nativeName == "Steel Saber" || nativeName == "\t Steel\r\n Saber\0 ")
                Need(name == "Steel Saber", "Native label identity changed during whitespace cleanup.");
            if (string.IsNullOrWhiteSpace(nativeName) || nativeName == " \t\r\n\0")
                Need(name == "unidentified item", "Missing name fabricated a specific item.");
        }
    }

    private static void InvalidParticipants()
    {
        Fixture(out var job, out var bystander);
        Reject(() => Receipt(job, bystander)); Reject(() => Receipt(job, source: bystander));
        job.Target.PersonId = "player"; Reject(() => Receipt(job));
        job.Target.PersonId = job.ResidentId; Reject(() => Receipt(job));
    }

    private static void InvalidTransfer()
    {
        Fixture(out var job, out _);
        Reject(() => Receipt(job, uid: 778)); Reject(() => Receipt(job, quantity: 1));
        Reject(() => Receipt(job, at: 10)); Reject(() => Receipt(job, at: 100));
        job.Target.TransferDirection = NpcActionTransferDirection.PlayerToNpc; Reject(() => Receipt(job));
    }

    private static void NoPrematureMemory()
    {
        var store = Fixture(out var job, out _);
        var state = store.Snapshot(); state.NpcActions.Add(job); store.Restore(state);
        Need(!store.TryRememberNpcActionOutcome(job.Id, 2, "day 1", "shop", "received item"), "Uncommitted action made memory.");
        Need(store.DrainNpcItemTransferReceipts(32) == 0 && store.Snapshot().ConversationMemories.Count == 0,
            "Uncommitted handoff created evidence.");
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error.GetBaseException() is ArgumentException) { return; }
        throw new InvalidOperationException("Invalid receipt was accepted.");
    }
}
