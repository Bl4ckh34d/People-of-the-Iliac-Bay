using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        var store = new WorldStateStore(12);
        store.RememberConversation("alice", 100, "1 Morning Star 405", "inn", "My sister is Mirabelle.", "My sister is Rowan.", "native");
        store.RememberConversation("alice", 200, "1 Morning Star 405", "inn", "Correction: my sister is Celia.", "I heard your correction.", "model");
        store.RememberConversation("alice", 300, "1 Morning Star 405", "inn", "Another correction: my sister is Elayne.", "I heard the latest statement.", "action");
        store.RememberConversation("alice", 500, "1 Morning Star 405", "inn", "Future secret.", "Future reply.", "model");
        store.RememberConversation("bob", 350, "1 Morning Star 405", "inn", "Foreign secret.", "Foreign reply.", "model");
        string recall = store.RecallConversation("alice", "What is my sister's name?", 400, 2400);
        Check(recall.IndexOf("Mirabelle", StringComparison.Ordinal) < recall.IndexOf("Celia", StringComparison.Ordinal) &&
            recall.IndexOf("Celia", StringComparison.Ordinal) < recall.IndexOf("Elayne", StringComparison.Ordinal), "Two same-day corrections must follow original claim chronologically.");
        Check(recall.Contains("game-second 100") && recall.Contains("game-second 200") && recall.Contains("game-second 300"), "Exact entry times survive formatting.");
        Check(recall.Contains("oldest to newest") && recall.Contains("equal times unordered"), "Selected chronology and tie limits are explicit.");
        Check(recall.Contains("Player said:") && recall.Contains("NPC said (native):") && recall.Contains("NPC said (model):") && recall.Contains("NPC said (action):") && recall.Contains("Rowan"), "Speaker and source provenance retained.");
        Check(!recall.Contains("Future secret") && !recall.Contains("Foreign secret"), "Future and foreign evidence excluded.");
        for (int i = 0; i < 8; i++) store.AdvanceConversationRecallWarmup("alice", 64, 100);
        Check(recall == store.RecallConversation("alice", "What is my sister's name?", 400, 2400), "Warm index and scan produce identical chronology.");
        foreach (int budget in new[] { 256, 400, 600, 700, 1200, 2400 })
            Check(store.RecallConversation("alice", "sister", 400, budget).Length <= budget, "Character budget preserved: " + budget);
        var transcript = store.ConversationTranscript("alice", 600);
        Check(transcript.Count == 4 && transcript[0].At == 100 && transcript[2].PlayerText.Contains("Elayne") && transcript[3].At == 500, "Retrieval does not rewrite archive.");
        var failed = new WorldStateStore(12);
        failed.RememberConversation("alice", 100, "day", "inn", "My horse is Copper.", "Unverified invented horse.", "unavailable");
        failed.RememberObservation("alice", 200, "day", "inn", "The player left the room.");
        string failedRecall = failed.RecallConversation("alice", "horse", 300, 2000);
        Check(failedRecall.Contains("Dialogue result: unavailable") && !failedRecall.Contains("Unverified invented horse"), "Unavailable NPC text never becomes evidence.");
        Check(failedRecall.Contains("Observed event:") && failedRecall.Contains("game-second 200"), "Observed event retains time and provenance.");
        Console.WriteLine("Conversation chronology regressions passed: " + assertions + " assertions.");
        var cases = new System.Collections.Generic.List<(string Name, Action Body)>();
        PeopleOfTheIliacBay.Tests.ConversationMemoryTests.Add(cases);
        foreach (var test in cases) test.Body();
        Console.WriteLine("Existing conversation regressions passed: " + cases.Count + " cases.");
    }
}

namespace PeopleOfTheIliacBay.Tests
{
    internal static class Check
    {
        public static void True(bool condition, string message = "Condition was false")
        { if (!condition) throw new InvalidOperationException(message); }
        public static void Equal<T>(T expected, T actual)
        { True(System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual), "Expected " + expected + "; got " + actual); }
        public static void Near(double expected, double actual, double tolerance = 0.000001)
        { True(!double.IsNaN(actual) && Math.Abs(expected - actual) <= tolerance); }
    }
}
