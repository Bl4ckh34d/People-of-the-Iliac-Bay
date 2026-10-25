using System;

// Runs JSON-lines session tests against the persistent actual-Core bridge.
static class SessionBridgeTests
{
    public static void Run()
    {
        SessionBridge.RunSelfTests();
        SessionBridge.RunWaitSelfTests();
        Console.WriteLine("SessionBridge self-tests passed (persistent Core state, explicit receipts, timed Wait completion, restore, replay, cancellation isolation, error isolation).");
    }
}
