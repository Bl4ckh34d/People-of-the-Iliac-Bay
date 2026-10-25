using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

// Console self-tests for the actual-Core closed-loop bridge. Receipt events are
// fixture inputs; these tests make no claim about a Unity/native effect.
static class ClosedLoopTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Run()
    {
        ClosedLoop.RunSelfTests();
        ExternalJsonResults();
        Console.WriteLine("ClosedLoop bridge self-tests passed (Core lifecycle, receipts, restore, cancellation, revisions).");
    }

    private static void ExternalJsonResults()
    {
        var denied = Proposal("json-no-consent", consent: false);
        JsonObject deniedOutput = Run(denied);
        Require(deniedOutput["accepted"]!.GetValue<bool>() == false && deniedOutput["contractValid"]!.GetValue<bool>(),
            "JSON no-consent response must distinguish valid proposal from unaccepted task.");
        Require(deniedOutput["inventoriesBefore"]!.ToJsonString() == deniedOutput["inventoriesAfter"]!.ToJsonString(),
            "No-consent JSON response must expose unchanged inventories.");

        JsonObject noReceipt = Run(Proposal("json-no-receipt", receipt: false));
        Require(noReceipt["accepted"]!.GetValue<bool>() == false && noReceipt["contractValid"]!.GetValue<bool>() == false,
            "Missing fixture receipts must be rejected before Core task acceptance.");
        Require(noReceipt["nativeEffectObserved"]!.GetValue<bool>() == false,
            "Missing receipt response must not imply a native effect.");
        JsonObject implicitHandover = Run(Proposal("json-implicit-handover", handoverAccept: null));
        Require(!implicitHandover["accepted"]!.GetValue<bool>() && !implicitHandover["contractValid"]!.GetValue<bool>(),
            "A missing handover accept decision must not default to consent.");

        var omittedConsent = JsonNode.Parse(JsonSerializer.Serialize(new[] { Proposal("json-default-consent") }, JsonOptions))!.AsArray();
        omittedConsent[0]!.AsObject().Remove("consent");
        JsonObject defaultConsent = RunRaw(omittedConsent.ToJsonString());
        Require(!defaultConsent["accepted"]!.GetValue<bool>(), "Omitted consent must default to false.");

        var wrongTarget = Proposal("json-wrong-target");
        wrongTarget.Steps![0].Target!.ItemId = "43";
        JsonObject rejectedTarget = Run(wrongTarget);
        Require(!rejectedTarget["accepted"]!.GetValue<bool>() && !rejectedTarget["contractValid"]!.GetValue<bool>(),
            "Unsupported purchase item target must be rejected instead of replaced with fixture stock.");
        var wrongQuantity = Proposal("json-wrong-quantity");
        wrongQuantity.Steps![0].Target!.Quantity = 2;
        JsonObject rejectedQuantity = Run(wrongQuantity);
        Require(!rejectedQuantity["accepted"]!.GetValue<bool>(), "Unsupported quantity must be rejected rather than rewritten to one item.");
        var wrongParticipant = Proposal("json-wrong-participant");
        wrongParticipant.Steps![1].Target!.ParticipantRef = "buyer";
        JsonObject rejectedParticipant = Run(wrongParticipant);
        Require(!rejectedParticipant["accepted"]!.GetValue<bool>(), "Unsupported participant reference must be rejected.");

        var unknownTargetField = JsonNode.Parse(JsonSerializer.Serialize(new[] { Proposal("json-unknown-target-field") }, JsonOptions))!.AsArray();
        unknownTargetField[0]!["steps"]![0]!["target"]!["arbitraryPersonId"] = "person:somebody-else";
        JsonObject rejectedUnknown = RunRaw(unknownTargetField.ToJsonString());
        Require(!rejectedUnknown["accepted"]!.GetValue<bool>(), "Unknown target fields must be rejected.");

        JsonObject otherUid = Run(Proposal("json-alternate-uid", uid: 12345));
        Require(otherUid["acquiredUid"]!.GetValue<ulong>() == 12345 &&
            otherUid["itemOwner"]!.GetValue<string>() == otherUid["recipientId"]!.GetValue<string>(),
            "JSON output must report the actual acquired UID and current owner.");

        JsonObject deniedHandover = Run(Proposal("json-denied-handover", uid: 12345, handoverAccept: false));
        Require(deniedHandover["accepted"]!.GetValue<bool>() && deniedHandover["contractValid"]!.GetValue<bool>() &&
            deniedHandover["task"]!["phase"]!.GetValue<string>() == "Running" &&
            deniedHandover["task"]!["currentStepIndex"]!.GetValue<int>() == 1,
            "Denied handover must report the accepted task's actual partial progress.");
        Require(deniedHandover["fixtureReceiptUsed"]!.GetValue<bool>() && deniedHandover["handoverReceiptUsed"]!.GetValue<bool>() &&
            deniedHandover["itemOwner"]!.GetValue<string>() == deniedHandover["buyerId"]!.GetValue<string>() &&
            deniedHandover["inventoriesAfter"]!.ToJsonString().Contains("12345", StringComparison.Ordinal),
            "Denied handover must preserve and report the purchased item at the buyer.");
        Require(deniedHandover["nativeEffectObserved"]!.GetValue<bool>() == false,
            "Partial JSON result must retain nativeEffectObserved:false.");
    }

    private static JsonObject Run(ClosedLoop.Proposal proposal)
        => RunRaw(JsonSerializer.Serialize(new[] { proposal }, JsonOptions));

    private static JsonObject RunRaw(string json)
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            ClosedLoop.RunInput(json);
        }
        finally { Console.SetOut(previous); }
        return (JsonNode.Parse(output.ToString())!.AsArray()[0] ?? throw new InvalidOperationException("No JSON result." )).AsObject();
    }

    private static ClosedLoop.Proposal Proposal(string id, bool consent = true, bool receipt = true, ulong uid = 9001,
        bool? handoverAccept = true) => new()
    {
        Id = id, Consent = consent,
        Steps = new() {
            new ClosedLoop.StepInput { Kind = "Purchase", DurationSeconds = 0, Target = new ClosedLoop.TargetInput {
                Kind = "IndoorPoint", ParticipantRef = "seller", SettlementId = 100, BuildingKey = 20,
                ContainerId = "7", ItemId = "42", Quantity = 1, QuotedGold = 9, TargetRef = "fixture:seller" } },
            new ClosedLoop.StepInput { Kind = "Transfer", DurationSeconds = 0, ItemSourceStep = 1, Target = new ClosedLoop.TargetInput {
                Kind = "Person", ParticipantRef = "recipient", ItemId = "42", Quantity = 1,
                TransferDirection = "NpcToCounterparty", TargetRef = "fixture:recipient" } }
        },
        Receipt = receipt ? new ClosedLoop.Receipt { BuyerGoldAfter = 21, SellerGoldAfter = 9, ItemUid = uid, NativePayload = "fixture:native-item" } : null,
        HandoverReceipt = receipt ? new ClosedLoop.HandoverReceipt { Accept = handoverAccept } : null
    };

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
