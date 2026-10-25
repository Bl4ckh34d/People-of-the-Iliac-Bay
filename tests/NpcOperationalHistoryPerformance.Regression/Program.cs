using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const int TaskRows = 165;
    private const int ActionRows = 1650;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };

    private static int Main()
    {
        try
        {
            MeasureWorld(500);
            MeasureWorld(3000);
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL " + error);
            return 1;
        }
    }

    private static void MeasureWorld(int targetResidents)
    {
        var store = new WorldStateStore(31);
        int residents = 0;
        string historyOwner = null;
        var residentRows = new List<Resident>();
        for (int town = 0; residents < targetResidents; town++)
        {
            if (town > 60) throw new Exception("Population target was not reached.");
            var population = store.GetOrCreate(100 + town, Enumerable.Range(1, 100), 3);
            var people = population.Households.SelectMany(home => home.Residents).ToList();
            residentRows.AddRange(people);
            var lastAdult = people.LastOrDefault(person => person.IsAlive && !person.IsQuestProtected &&
                person.AgeGroup == ResidentAgeGroup.Adult);
            if (lastAdult != null) historyOwner = lastAdult.Id;
            residents += people.Count;
        }
        if (historyOwner == null) throw new Exception("No eligible history owner.");
        for (int index = 0; index < residentRows.Count; index++)
        {
            var person = residentRows[index];
            if (index % 10 == 0)
                store.GetOrCreateNpcPersonality(person.Id, person.AppearanceSeed, NpcSocialBackground.Commoner);
            if (index % 25 == 0)
                store.RememberObservation(person.Id, 1, "Benchmark day", "Town", "Observed town activity.");
        }

        var seeded = store.Snapshot();
        for (int ordinal = 1; ordinal <= TaskRows; ordinal++)
            seeded.NpcTasks.Add(TerminalTask(historyOwner, ordinal));
        for (int ordinal = 1; ordinal <= ActionRows; ordinal++)
            seeded.NpcActions.Add(TerminalAction(historyOwner, ordinal));
        store.Restore(seeded);
        var baseline = store.Snapshot();
        if (baseline.NpcTasks.Count != TaskRows || baseline.NpcActions.Count != ActionRows)
            throw new Exception("Benchmark history failed validation.");

        int beforeBytes = JsonSerializer.SerializeToUtf8Bytes(baseline, JsonOptions).Length;
        var idle = new List<double>();
        long inspected = 0;
        for (int sample = 0; sample < 100; sample++)
        {
            var watch = Stopwatch.StartNew();
            var result = store.MaintainNpcOperationalHistory(0, 4, 1);
            watch.Stop();
            if (result.RemovedTaskGroups != 0 || result.RemovedStandaloneActions != 0 ||
                result.FullValidationPasses != 0 || result.Examined > 4)
                throw new Exception("Idle maintenance published or exceeded its candidate budget.");
            idle.Add(watch.Elapsed.TotalMilliseconds);
            inspected += result.ReferenceRowsInspected;
        }
        idle.Sort();

        var publish = new List<double>();
        long publishReferences = 0;
        int removedTasks = 0, removedActions = 0;
        for (int sample = 0; sample < 20; sample++)
        {
            var watch = Stopwatch.StartNew();
            var result = store.MaintainNpcOperationalHistory(100000000, 4, 1);
            watch.Stop();
            if (result.RemovedTaskGroups + result.RemovedStandaloneActions != 1 ||
                result.FullValidationPasses != 1 || result.Examined > 4)
                throw new Exception("Publishing maintenance did not validate one eviction.");
            publish.Add(watch.Elapsed.TotalMilliseconds);
            publishReferences += result.ReferenceRowsInspected;
            removedTasks += result.RemovedTaskGroups;
            removedActions += result.RemovedStandaloneActions;
        }
        publish.Sort();
        var after = store.Snapshot();
        int afterBytes = JsonSerializer.SerializeToUtf8Bytes(after, JsonOptions).Length;
        if (after.NpcTasks.Count + after.NpcActions.Count != TaskRows + ActionRows - 20)
            throw new Exception("Published history count differs from twenty evictions.");

        Console.WriteLine("PASS residents=" + residents.ToString(CultureInfo.InvariantCulture) +
            " personalities=" + baseline.NpcPersonalities.Count.ToString(CultureInfo.InvariantCulture) +
            " memory_journals=" + baseline.ConversationMemories.Count.ToString(CultureInfo.InvariantCulture) +
            " tasks=" + TaskRows + " actions=" + ActionRows +
            " idle4_p50_ms=" + Format(idle[49]) + " idle4_p95_ms=" + Format(idle[94]) +
            " idle4_max_ms=" + Format(idle[99]) +
            " publish20_p50_ms=" + Format(publish[9]) +
            " publish20_p95_ms=" + Format(publish[18]) +
            " publish20_max_ms=" + Format(publish[19]) +
            " removed_tasks=" + removedTasks.ToString(CultureInfo.InvariantCulture) +
            " removed_actions=" + removedActions.ToString(CultureInfo.InvariantCulture) +
            " publish_reference_rows_total=" + publishReferences.ToString(CultureInfo.InvariantCulture) +
            " idle_reference_rows_avg=" + (inspected / 100).ToString(CultureInfo.InvariantCulture) +
            " before_json_bytes=" + beforeBytes.ToString(CultureInfo.InvariantCulture) +
            " after_json_bytes=" + afterBytes.ToString(CultureInfo.InvariantCulture));
    }

    private static string Format(double milliseconds)
    { return milliseconds.ToString("F3", CultureInfo.InvariantCulture); }

    private static NpcTaskPlan TerminalTask(string person, int ordinal)
    {
        string id = "performance-task-" + ordinal.ToString(CultureInfo.InvariantCulture);
        var task = new NpcTaskPlan { Id = id, ProposalId = id, ResidentId = person,
            Phase = NpcTaskPhase.Cancelled, Revision = 2, CurrentStepIndex = 0,
            FailureReason = "Cancelled after observation.", AcceptedAt = ordinal,
            UpdatedAt = ordinal + 1, DeadlineAt = ordinal + 100 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        return task;
    }

    private static NpcActionJob TerminalAction(string person, int ordinal)
    {
        string id = "performance-action-" + ordinal.ToString(CultureInfo.InvariantCulture);
        return new NpcActionJob { Id = id, ProposalId = id, ResidentId = person,
            Kind = NpcActionKind.Wait, Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Cancelled, Revision = 2, FailureReason = "Cancelled after observation.",
            AcceptedAt = ordinal, UpdatedAt = ordinal + 1, DeadlineAt = ordinal + 100,
            FinishAt = ordinal + 10 };
    }
}
