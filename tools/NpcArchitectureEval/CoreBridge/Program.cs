using System.Text.Json;
using System.Text.Json.Serialization;
using PeopleOfTheIliacBay.Core;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
if (args.Length == 1 && args[0] == "--self-test")
{
    SelfTest();
    return;
}
if (args.Length == 1 && args[0] == "--closed-loop-self-test")
{
    ClosedLoopTests.Run();
    return;
}
if (args.Length == 1 && args[0] == "--closed-loop")
{
    ClosedLoop.RunInput(Console.In.ReadToEnd());
    return;
}
if (args.Length == 1 && args[0] == "--session-self-test")
{
    SessionBridgeTests.Run();
    return;
}
if (args.Length == 1 && args[0] == "--session")
{
    SessionBridge.Run();
    return;
}

string input = Console.In.ReadToEnd();
List<ProposalInput>? proposals;
try
{
    proposals = JsonSerializer.Deserialize<List<ProposalInput>>(input, options);
}
catch (JsonException ex)
{
    Console.Error.WriteLine("Invalid input JSON: " + ex.Message);
    Environment.ExitCode = 2;
    return;
}

if (proposals == null)
{
    Console.Error.WriteLine("Input must be a JSON array.");
    Environment.ExitCode = 2;
    return;
}

Console.WriteLine(JsonSerializer.Serialize(proposals.Select(ValidateProposal), options));

static ProposalResult ValidateProposal(ProposalInput proposal)
{
    var result = new ProposalResult { Id = proposal.Id ?? "" };
    try
    {
        var store = new WorldStateStore(42);
        var resident = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
        var steps = new List<NpcTaskStep>();
        double durationTotal = 0;
        foreach (StepInput input in proposal.Steps ?? throw new ArgumentException("Proposal steps are required."))
        {
            double duration = input.DurationSeconds;
            durationTotal += duration;
            steps.Add(new NpcTaskStep
            {
                Kind = ParseEnum<NpcActionKind>(input.Kind),
                ItemSourceStep = input.ItemSourceStep,
                DurationSeconds = duration,
                Target = MapTarget(input.Target)
            });
        }

        double deadline = 100 + Math.Min(durationTotal + 600, 86400);
        var task = new NpcTaskPlan
        {
            Id = "npc-task:" + (proposal.Id ?? ""),
            ProposalId = "npc-proposal:" + (proposal.Id ?? ""),
            ResidentId = resident.Id,
            Steps = steps,
            CurrentStepIndex = 0,
            Phase = NpcTaskPhase.Ready,
            Revision = 1,
            AcceptedAt = 100,
            UpdatedAt = 100,
            DeadlineAt = deadline
        };

        NpcTaskContract.Validate(task);
        result.ContractValid = true;
        result.ContractError = null;
        result.RejectsNoConsent = !store.TryAcceptNpcTask(task, false, out _);
        result.AcceptWithConsent = store.TryAcceptNpcTask(task, true, out _);
        if (result.AcceptWithConsent)
            store.FailNpcTask(task.Id, task.Revision, task.AcceptedAt + 1, "Self-test terminal receipt.");
        result.RejectsDuplicate = !store.TryAcceptNpcTask(task, true, out _);
    }
    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
    {
        result.ContractValid = false;
        result.ContractError = ex.Message;
    }
    return result;
}

static NpcActionTarget MapTarget(TargetInput? input)
{
    if (input == null) throw new ArgumentException("Step target is required.");
    return new NpcActionTarget
    {
        Kind = ParseEnum<NpcActionTargetKind>(input.Kind),
        PersonId = input.PersonId,
        WorldSpaceId = input.WorldSpaceId,
        SettlementId = input.SettlementId,
        BuildingKey = input.BuildingKey,
        X = input.X,
        Y = input.Y,
        Z = input.Z,
        ItemId = input.ItemId,
        ContainerId = input.ContainerId,
        Quantity = input.Quantity,
        QuotedGold = input.QuotedGold,
        TransferDirection = input.TransferDirection == null
            ? NpcActionTransferDirection.NpcToCounterparty
            : ParseEnum<NpcActionTransferDirection>(input.TransferDirection)
    };
}

static T ParseEnum<T>(string? value) where T : struct, Enum
{
    if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Enum value is required.");
    try { return Enum.Parse<T>(value, ignoreCase: true); }
    catch (ArgumentException) { throw new ArgumentException("Invalid " + typeof(T).Name + ": " + value + "."); }
}

static void SelfTest()
{
    var cases = new List<ProposalInput>
    {
        new() { Id = "wait-follow", Steps = new() {
            new() { Kind = "Wait", DurationSeconds = 30, Target = new() { Kind = "Person", PersonId = "player" } },
            new() { Kind = "Follow", DurationSeconds = 60, Target = new() { Kind = "Person", PersonId = "player" } }
        }},
        new() { Id = "invalid-target", Steps = new() {
            new() { Kind = "Interact", Target = new() { Kind = "Person" } }
        }},
        new() { Id = "too-many", Steps = Enumerable.Range(0, 9).Select(_ =>
            new StepInput { Kind = "Wait", DurationSeconds = 1, Target = new() { Kind = "Person", PersonId = "player" } }).ToList() },
        new() { Id = "wrong-timed-data", Steps = new() {
            new() { Kind = "MoveTo", DurationSeconds = 1, Target = new() { Kind = "WorldPoint", WorldSpaceId = "world", X = 1, Y = 2, Z = 3 } }
        }},
        new() { Id = "purchase-handover", Steps = new() {
            new() { Kind = "Purchase", Target = new() { Kind = "IndoorPoint", SettlementId = 100, BuildingKey = 10,
                PersonId = "person:seller", ContainerId = "7", ItemId = "42", Quantity = 2, QuotedGold = 9 } },
            new() { Kind = "Transfer", ItemSourceStep = 1, Target = new() { Kind = "Person", PersonId = "player",
                ItemId = "42", Quantity = 2 } }
        }},
        new() { Id = "mismatched-handover", Steps = new() {
            new() { Kind = "Purchase", Target = new() { Kind = "IndoorPoint", SettlementId = 100, BuildingKey = 10,
                PersonId = "person:seller", ContainerId = "7", ItemId = "42", Quantity = 2, QuotedGold = 9 } },
            new() { Kind = "Transfer", ItemSourceStep = 1, Target = new() { Kind = "Person", PersonId = "player",
                ItemId = "43", Quantity = 2 } }
        }}
    };

    var results = cases.Select(ValidateProposal).ToDictionary(r => r.Id);
    Require(results["wait-follow"].ContractValid, "Wait/Follow proposal must validate.");
    Require(results["wait-follow"].RejectsNoConsent, "No-consent acceptance must fail.");
    Require(results["wait-follow"].AcceptWithConsent, "Consent acceptance must succeed.");
    Require(results["wait-follow"].RejectsDuplicate, "Duplicate acceptance must fail.");
    Require(!results["invalid-target"].ContractValid, "Incomplete target must fail validation.");
    Require(!results["too-many"].ContractValid, "More than eight steps must fail validation.");
    Require(!results["wrong-timed-data"].ContractValid, "Timed MoveTo data must fail validation.");
    Require(results["purchase-handover"].ContractValid && results["purchase-handover"].AcceptWithConsent,
        "Matching purchase/handover dependency must validate and persist as a task.");
    Require(!results["mismatched-handover"].ContractValid, "Mismatched acquired-item target must fail validation.");

    var residentStore = NewStore(out string residentId);
    var acceptedKinds = new[]
    {
        new NpcTaskStep { Kind = NpcActionKind.MoveTo, Target = WorldPoint() },
        new NpcTaskStep { Kind = NpcActionKind.Follow, DurationSeconds = 1, Target = Person("player") },
        new NpcTaskStep { Kind = NpcActionKind.Interact, Target = Person("person:other") },
        new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 1, Target = Person("player") },
        new NpcTaskStep { Kind = NpcActionKind.Transfer, Target = new NpcActionTarget {
            Kind = NpcActionTargetKind.Person, PersonId = "player", ItemId = "42", Quantity = 1 } },
        new NpcTaskStep { Kind = NpcActionKind.Purchase, Target = new NpcActionTarget {
            Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 100, BuildingKey = 10,
            PersonId = "person:seller", ContainerId = "7", ItemId = "42", QuotedGold = 9 } }
    };
    foreach (var step in acceptedKinds)
        NpcTaskContract.Validate(Plan(residentId, "typed-" + step.Kind, new[] { step }, 100 + 600 + step.DurationSeconds));

    var eightSteps = Enumerable.Range(0, NpcTaskContract.MaximumSteps)
        .Select(_ => new NpcTaskStep { Kind = NpcActionKind.MoveTo, Target = WorldPoint() }).ToList();
    NpcTaskContract.Validate(Plan(residentId, "eight", eightSteps, 708));
    ExpectInvalid(Plan(residentId, "nine", eightSteps.Concat(new[] { eightSteps[0] }).ToList(), 709));
    ExpectInvalid(Plan(residentId, "unsupported", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Attack, Target = Person("person:enemy") } }, 700));
    ExpectInvalid(Plan(residentId, "zero-wait", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Wait, Target = Person("player") } }, 700));

    NpcTaskContract.Validate(Plan(residentId, "deadline-24h", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Wait, DurationSeconds = 86399, Target = Person("player") } }, 86500));
    ExpectInvalid(Plan(residentId, "deadline-over-24h", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Wait, DurationSeconds = 30, Target = Person("player") } }, 86500.01));
    // Core contract has no 10–900 second Wait limit. The Unity task binder owns that rule.
    NpcTaskContract.Validate(Plan(residentId, "core-wait-lower-than-binder", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Wait, DurationSeconds = 1, Target = Person("player") } }, 700));
    NpcTaskContract.Validate(Plan(residentId, "core-wait-upper-than-binder", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Follow, DurationSeconds = 901, Target = Person("player") } }, 1600));

    var duplicateStore = NewStore(out string duplicateResidentId);
    var first = Plan(duplicateResidentId, "duplicate-first", new[] { acceptedKinds[0] }, 700);
    first.ProposalId = "same-proposal";
    Require(duplicateStore.TryAcceptNpcTask(first, true, out _), "First proposal must be accepted.");
    Require(duplicateStore.FailNpcTask(first.Id, first.Revision, 101, "Self-test terminal receipt."),
        "Task must become terminal before duplicate-ledger check.");
    var sameProposal = NpcTaskContract.Copy(first);
    sameProposal.Id = "different-task-id";
    Require(!duplicateStore.TryAcceptNpcTask(sameProposal, true, out _), "Repeated proposal ID must be rejected.");
    Require(duplicateStore.Snapshot().NpcActions.Count == 0, "Task acceptance must not fabricate action effects.");

    var dependencyStore = NewStore(out string dependencyResidentId);
    var purchaseTarget = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint,
        SettlementId = 100, BuildingKey = 10, PersonId = "person:seller", ContainerId = "7", ItemId = "42",
        Quantity = 2, QuotedGold = 9 };
    var handoverTarget = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player",
        ItemId = "42", Quantity = 2 };
    var dependencyPlan = Plan(dependencyResidentId, "purchase-handover",
        new[] { new NpcTaskStep { Kind = NpcActionKind.Purchase, Target = purchaseTarget },
            new NpcTaskStep { Kind = NpcActionKind.Transfer, ItemSourceStep = 1, Target = handoverTarget } }, 800);
    Require(dependencyStore.TryAcceptNpcTask(dependencyPlan, true, out _), "Valid dependency must be accepted.");
    Require(dependencyStore.Snapshot().NpcActions.Count == 0, "Accepted dependency must not create native effects.");

    var movedWait = Plan(residentId, "bound-wait", new[] {
        new NpcTaskStep { Kind = NpcActionKind.MoveTo, Target = WorldPoint() },
        new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 30, Target = WorldPoint() }
    }, 730);
    NpcTaskContract.Validate(movedWait);
    movedWait.Steps[1].Target.X += 1;
    ExpectInvalid(movedWait);

    var badClock = Plan(residentId, "bad-clock", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Wait, DurationSeconds = 30, Target = Person("player") } }, 700);
    badClock.UpdatedAt = 99;
    ExpectInvalid(badClock);
    var badFinite = Plan(residentId, "bad-finite-time", new[] { new NpcTaskStep {
        Kind = NpcActionKind.Wait, DurationSeconds = 30, Target = Person("player") } }, 700);
    badFinite.DeadlineAt = double.NaN;
    ExpectInvalid(badFinite);
    Console.WriteLine("CoreBridge self-test passed (6 JSON cases plus direct Core boundary checks).");
}

static WorldStateStore NewStore(out string residentId)
{
    var store = new WorldStateStore(42);
    residentId = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
    return store;
}

static NpcTaskPlan Plan(string residentId, string id, IEnumerable<NpcTaskStep> steps, double deadline) => new()
{
    Id = "npc-task:" + id,
    ProposalId = "npc-proposal:" + id,
    ResidentId = residentId,
    Steps = steps.ToList(),
    Phase = NpcTaskPhase.Ready,
    Revision = 1,
    AcceptedAt = 100,
    UpdatedAt = 100,
    DeadlineAt = deadline
};

static NpcActionTarget Person(string id) => new() { Kind = NpcActionTargetKind.Person, PersonId = id };

static NpcActionTarget WorldPoint() => new() { Kind = NpcActionTargetKind.WorldPoint,
    WorldSpaceId = "world", X = 1, Y = 2, Z = 3 };

static void ExpectInvalid(NpcTaskPlan task)
{
    try { NpcTaskContract.Validate(task); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Expected Core to reject task " + task.Id + ".");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ProposalInput
{
    public string? Id { get; set; }
    public List<StepInput>? Steps { get; set; }
}

sealed class StepInput
{
    public string? Kind { get; set; }
    public double DurationSeconds { get; set; }
    public int ItemSourceStep { get; set; }
    public TargetInput? Target { get; set; }
}

sealed class TargetInput
{
    public string? Kind { get; set; }
    public string? PersonId { get; set; }
    public string? WorldSpaceId { get; set; }
    public int SettlementId { get; set; } = -1;
    public int BuildingKey { get; set; } = -1;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string? ItemId { get; set; }
    public string? ContainerId { get; set; }
    public int Quantity { get; set; } = 1;
    public int QuotedGold { get; set; }
    public string? TransferDirection { get; set; }
}

sealed class ProposalResult
{
    public string Id { get; set; } = "";
    public bool ContractValid { get; set; }
    public string? ContractError { get; set; }
    public bool AcceptWithConsent { get; set; }
    public bool RejectsNoConsent { get; set; }
    public bool RejectsDuplicate { get; set; }
}
