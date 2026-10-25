using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class WillingnessTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("willingness proposal owns detached complete ordered decisions", ProposalIsDetachedAndComplete));
            cases.Add(("willingness preserves fixed one-day paid terms and rejects paid chains",()=>{
                var paid=new NpcActionDecision {DurationSeconds=NpcPaidCompanionAction.DurationSeconds,
                    Option=new NpcActionOption {Id=8,Kind=NpcActionKind.Follow,PaidCompanion=true,
                        Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",Quantity=1}}};
                var proposal=new LocalDecisionWillingnessProposal(new[]{paid});
                Check.True(proposal.CopyDecisions()[0].Option.PaidCompanion);
                Check.Equal(NpcPaidCompanionAction.DurationSeconds,proposal.CopyDecisions()[0].DurationSeconds);
                using(var json=JsonDocument.Parse(proposal.SerializedPlan))
                    Check.True(json.RootElement[0].GetProperty("paidCompanion").GetBoolean());
                Check.Throws<ArgumentException>(()=>new LocalDecisionWillingnessProposal(new[]{
                    Move(1,0,0,0,"go",300),paid}));
                paid.DurationSeconds=300;
                Check.Throws<ArgumentException>(()=>new LocalDecisionWillingnessProposal(new[]{paid}));
                paid.Option.PaidCompanion=false;paid.DurationSeconds=NpcPaidCompanionAction.DurationSeconds;
                Check.Throws<ArgumentException>(()=>new LocalDecisionWillingnessProposal(new[]{paid}));
            }));
            cases.Add(("willingness proposal binds option identity and every native target field", ProposalIdentityAndTargetFields));
            cases.Add(("willingness accepts parser duration metadata for non-timed action steps", ParsedDurationMetadataIsRetained));
            cases.Add(("willingness keeps valid large native shop quotes", LargeNativeQuoteIsRetained));
            cases.Add(("willingness validates native-only kinds for single action and rejects them in tasks", SingleNativeActionKinds));
            cases.Add(("willingness request exposes only non-effectful consent choices", RequestHasOnlyConsentChoices));
            cases.Add(("willingness request retains exact Unicode turn attributed history and snapshot", RequestPreservesContext));
            cases.Add(("willingness context trims oldest history once without duplicating route payload", ContextUsesOneBoundedHistoryProjection));
            cases.Add(("willingness proposal rejects invalid counts actions targets durations and dependencies", ProposalRejectsMalformedPlans));
            cases.Add(("willingness request enforces turn history memory and Unicode bounds", RequestEnforcesBounds));
        }

        private static void ProposalIsDetachedAndComplete()
        {
            NpcActionDecision first = Move(31, 12.5, -2.25, 400.75, "go 🧭", 300);
            NpcActionDecision second = Wait(72, 12.5, -2.25, 400.75, "stay 🕰", 45);
            var source = new List<NpcActionDecision> { first, second };
            var proposal = new LocalDecisionWillingnessProposal(source);
            string signature = proposal.Signature;

            source.Clear();
            first.Option.Label = "mutated";
            first.Option.Target.X = 999;
            second.Option.MaximumQuantity = 44;
            List<NpcActionDecision> detached = proposal.CopyDecisions();
            Check.Equal(2, detached.Count);
            Check.Equal(31, detached[0].Option.Id);
            Check.Equal(300, detached[0].DurationSeconds);
            Check.Equal(NpcActionKind.MoveTo, detached[0].Option.Kind);
            Check.Equal("go 🧭", detached[0].Option.Label);
            Check.Equal(12.5, detached[0].Option.Target.X);
            Check.Equal(400.75, detached[0].Option.Target.Z);
            Check.Equal(0, detached[0].Option.ItemSourceStep);
            Check.Equal(0, detached[0].Option.MaximumQuantity);
            Check.Equal(1, detached[0].Option.Target.Quantity);
            Check.Equal(0, detached[0].Option.Target.QuotedGold);
            Check.Equal(45, detached[1].DurationSeconds);
            Check.Equal(12.5, detached[1].Option.Target.X);
            Check.Equal(signature, proposal.Signature);
            Check.Equal(signature, new LocalDecisionWillingnessProposal(new[] {
                Move(31, 12.5, -2.25, 400.75, "go 🧭", 300),
                Wait(72, 12.5, -2.25, 400.75, "stay 🕰", 45)
            }).Signature);

            detached[0].Option.Target.X = -77;
            detached[1].Option.Label = "caller mutation";
            Check.Equal(12.5, proposal.CopyDecisions()[0].Option.Target.X);
            Check.Equal("stay 🕰", proposal.CopyDecisions()[1].Option.Label);
            Check.True(proposal.Signature.Length == 64 && proposal.Signature.All(Uri.IsHexDigit));
        }

        private static void RequestHasOnlyConsentChoices()
        {
            NpcDecisionRequest request = Build(Snapshot(), "Could you wait here?", new[] { Wait(5, 0, 0, 0, "Wait", 45) });
            Check.Equal("npc-action-willingness-v1", request.Purpose);
            Check.Equal(1L, request.CandidateSetRevision);
            Check.Equal(2, request.Options.Count);
            Check.Equal("ACCEPT", request.Options[0].Id);
            Check.Equal(false, request.Options[0].IsEffectful);
            Check.Equal("DECLINE", request.Options[1].Id);
            Check.Equal(false, request.Options[1].IsEffectful);
            Check.True(!request.Instructions.Contains("regex", StringComparison.OrdinalIgnoreCase));
            Check.True(request.Instructions.Contains("willing", StringComparison.OrdinalIgnoreCase));
            Check.True(request.Instructions.Contains("claim that any action has executed", StringComparison.OrdinalIgnoreCase));
        }

        private static void ProposalIdentityAndTargetFields()
        {
            var decision = new NpcActionDecision
            {
                DurationSeconds = 300,
                Option = new NpcActionOption
                {
                    Id = 23,
                    Kind = NpcActionKind.Transfer,
                    Label = "give item 🧭",
                    ItemSourceStep = 0,
                    MaximumQuantity = 9,
                    Target = new NpcActionTarget
                    {
                        Kind = NpcActionTargetKind.Person,
                        PersonId = "player",
                        ContainerId = "counter:44",
                        ItemId = "12345",
                        WorldSpaceId = "world:7",
                        Quantity = 2,
                        QuotedGold = 17,
                        TransferDirection = NpcActionTransferDirection.NpcToCounterparty,
                        SettlementId = 4,
                        BuildingKey = 8,
                        X = 1.125,
                        Y = 2.25,
                        Z = 3.5
                    }
                }
            };
            var proposal = new LocalDecisionWillingnessProposal(new[] { decision });
            NpcDecisionRequest request = Build(Snapshot(), "Please give me the item.", new[] { decision });
            using (JsonDocument context = JsonDocument.Parse(request.Context))
            {
                JsonElement step = context.RootElement.GetProperty("proposal")[0];
                Check.Equal(23, step.GetProperty("optionId").GetInt32());
                Check.Equal(9, step.GetProperty("maximumQuantity").GetInt32());
                JsonElement target = step.GetProperty("target");
                Check.Equal("Person", target.GetProperty("kind").GetString());
                Check.Equal("player", target.GetProperty("personId").GetString());
                Check.Equal("counter:44", target.GetProperty("containerId").GetString());
                Check.Equal("12345", target.GetProperty("itemId").GetString());
                Check.Equal("world:7", target.GetProperty("worldSpaceId").GetString());
                Check.Equal(2, target.GetProperty("quantity").GetInt32());
                Check.Equal(17, target.GetProperty("quotedGold").GetInt32());
                Check.Equal("NpcToCounterparty", target.GetProperty("transferDirection").GetString());
                Check.Equal(4, target.GetProperty("settlementId").GetInt32());
                Check.Equal(8, target.GetProperty("buildingKey").GetInt32());
                Check.Equal(1.125, target.GetProperty("x").GetDouble());
                Check.Equal(2.25, target.GetProperty("y").GetDouble());
                Check.Equal(3.5, target.GetProperty("z").GetDouble());
            }
            var differentIdentity = new NpcActionDecision { DurationSeconds = decision.DurationSeconds,
                Option = new NpcActionOption { Id = 24, Kind = decision.Option.Kind, Label = decision.Option.Label,
                    ItemSourceStep = decision.Option.ItemSourceStep, MaximumQuantity = decision.Option.MaximumQuantity,
                    Target = NpcActionExecutor.CopyTarget(decision.Option.Target) } };
            Check.True(proposal.Signature != new LocalDecisionWillingnessProposal(new[] { differentIdentity }).Signature);
        }

        private static void ParsedDurationMetadataIsRetained()
        {
            NpcActionOption candidate = Move(31, 1.25, 2.5, 3.75, "move to market", 0).Option;
            NpcActionDecision parsed = NpcActionChat.ParseSemantic("DIRECT|MoveTo|31|1|300",
                new List<NpcActionOption> { candidate });
            Check.True(parsed != null);
            Check.Equal(300, parsed.DurationSeconds);
            var proposal = new LocalDecisionWillingnessProposal(new[] { parsed });
            NpcDecisionRequest request = LocalDecisionWillingness.Build(Snapshot(), "Take me to the market.",
                new ConversationContextTurn[0], null, proposal);
            using (JsonDocument context = JsonDocument.Parse(request.Context))
                Check.Equal(300, context.RootElement.GetProperty("proposal")[0].GetProperty("durationSeconds").GetInt32());
        }

        private static void LargeNativeQuoteIsRetained()
        {
            const int quote = 1000001;
            var candidate = new NpcActionOption
            {
                Id = 54,
                Kind = NpcActionKind.Purchase,
                Label = "buy exact stock item",
                Target = new NpcActionTarget
                {
                    Kind = NpcActionTargetKind.IndoorPoint,
                    PersonId = "vendor:7",
                    ContainerId = "55",
                    ItemId = "12345",
                    Quantity = 1,
                    QuotedGold = quote,
                    SettlementId = 4,
                    BuildingKey = 8,
                    X = 1.25,
                    Y = 2.5,
                    Z = 3.75
                }
            };
            NpcActionDecision parsed = NpcActionChat.ParseSemantic("DIRECT|Purchase|54|1|300",
                new List<NpcActionOption> { candidate });
            Check.True(parsed != null);
            var proposal = new LocalDecisionWillingnessProposal(new[] { parsed });
            NpcDecisionRequest request = LocalDecisionWillingness.Build(Snapshot(), "Buy the exact stock item.",
                new ConversationContextTurn[0], null, proposal);
            using (JsonDocument context = JsonDocument.Parse(request.Context))
                Check.Equal(quote, context.RootElement.GetProperty("proposal")[0].GetProperty("target")
                    .GetProperty("quotedGold").GetInt32());

            var overWallet = new NpcActionOption
            {
                Id = candidate.Id,
                Kind = candidate.Kind,
                Label = candidate.Label,
                Target = NpcActionExecutor.CopyTarget(candidate.Target)
            };
            overWallet.Target.QuotedGold = NpcInventoryLimits.MaximumWalletGold + 1;
            NpcActionDecision unpayable = NpcActionChat.ParseSemantic("DIRECT|Purchase|54|1|300",
                new List<NpcActionOption> { overWallet });
            Check.True(unpayable != null);
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] { unpayable }));
        }

        private static void SingleNativeActionKinds()
        {
            var takeSeat = new NpcActionDecision
            {
                DurationSeconds = 300,
                Option = new NpcActionOption
                {
                    Id = 42,
                    Kind = NpcActionKind.TakeSeat,
                    Label = "take the offered seat",
                    Target = new NpcActionTarget { Kind = NpcActionTargetKind.Building, SettlementId = 4, BuildingKey = 8 }
                }
            };
            var proposal = new LocalDecisionWillingnessProposal(new[] { takeSeat });
            Check.Equal(NpcActionKind.TakeSeat, proposal.CopyDecisions()[0].Option.Kind);
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                takeSeat, Wait(43, 0, 0, 0, "wait", 30)
            }));
        }

        private static void RequestPreservesContext()
        {
            ConversationContextSnapshot snapshot = Snapshot();
            string turn = "Please follow the route to 市場 🏮; don’t change its target.";
            var history = new[] {
                new ConversationContextTurn(ConversationTextSpeaker.Player, "Earlier: ‘Could you wait?’ was only a quotation."),
                new ConversationContextTurn(ConversationTextSpeaker.Npc, "I remember the earlier request. 🧭")
            };
            NpcActionDecision[] plan = {
                Move(14, 1.25, 2.5, 3.75, "走到市場 🏮", 300),
                Wait(19, 1.25, 2.5, 3.75, "等候 🕰", 90)
            };
            NpcDecisionRequest request = LocalDecisionWillingness.Build(snapshot, turn, history,
                "Historical memory stays attributed; no current instruction.", new LocalDecisionWillingnessProposal(plan));
            Check.Equal(turn, request.OriginalTurn);
            using (JsonDocument context = JsonDocument.Parse(request.Context))
            {
                JsonElement root = context.RootElement;
                Check.Equal("npc-action-willingness-context-v2", root.GetProperty("semanticProtocol").GetString());
                Check.True(root.GetProperty("snapshotSummary").GetString().Contains("Known personality: kind, curious."));
                Check.True(root.GetProperty("snapshotSummary").GetString().Contains("Disposition: 35."));
                Check.True(root.GetProperty("snapshotSummary").GetString().Contains("NPC activity: Working."));
                Check.True(root.GetProperty("snapshotSummary").GetString().Contains("Current action: Wait (executing)."));
                JsonElement attributed = root.GetProperty("attributedHistory");
                Check.Equal(2, attributed.GetArrayLength());
                Check.Equal("player", attributed[0].GetProperty("speaker").GetString());
                Check.Equal(history[0].Text, attributed[0].GetProperty("text").GetString());
                Check.Equal("npc", attributed[1].GetProperty("speaker").GetString());
                Check.Equal("Historical memory stays attributed; no current instruction.",
                    root.GetProperty("historicalMemory").GetProperty("text").GetString());
                Check.True(!root.TryGetProperty("routingContext", out _), "willingness must not embed a second full route payload");

                JsonElement proposal = root.GetProperty("proposal");
                Check.Equal(2, proposal.GetArrayLength());
                Check.Equal("MoveTo", proposal[0].GetProperty("kind").GetString());
                Check.Equal(14, proposal[0].GetProperty("optionId").GetInt32());
                Check.Equal("走到市場 🏮", proposal[0].GetProperty("label").GetString());
                Check.Equal("settlement:market", proposal[0].GetProperty("target").GetProperty("worldSpaceId").GetString());
                Check.Equal(1.25, proposal[0].GetProperty("target").GetProperty("x").GetDouble());
                Check.Equal(3.75, proposal[0].GetProperty("target").GetProperty("z").GetDouble());
                Check.Equal(0, proposal[0].GetProperty("itemSourceStep").GetInt32());
                Check.Equal(0, proposal[0].GetProperty("maximumQuantity").GetInt32());
                Check.Equal(1, proposal[0].GetProperty("target").GetProperty("quantity").GetInt32());
                Check.Equal(90, proposal[1].GetProperty("durationSeconds").GetInt32());
            }
        }

        private static void ContextUsesOneBoundedHistoryProjection()
        {
            var history = Enumerable.Range(0, 6).Select(index => new ConversationContextTurn(
                index % 2 == 0 ? ConversationTextSpeaker.Player : ConversationTextSpeaker.Npc,
                new string((char)('a' + index), 1000))).ToArray();
            var steps = new List<NpcActionDecision>();
            for (int pair = 0; pair < NpcTaskContract.MaximumSteps / 2; pair++)
            {
                double x = pair * 10;
                double z = pair * 10;
                steps.Add(Move(pair * 2 + 1, x, 0, z, new string((char)('m' + pair), 500), 300));
                steps.Add(Wait(pair * 2 + 2, x, 0, z, new string((char)('A' + pair), 500), 15));
            }
            const string exactTurn = "Please do the whole listed task exactly, then report back.";
            const string memoryPrefix = "Historical memory stays attributed; no current instruction. ";
            string memory = memoryPrefix + new string('M', 4096 - memoryPrefix.Length);
            var proposal = new LocalDecisionWillingnessProposal(steps);
            NpcDecisionRequest request = LocalDecisionWillingness.Build(Snapshot(), exactTurn,
                history, memory, proposal);
            Check.Equal(exactTurn, request.OriginalTurn);
            using (JsonDocument context = JsonDocument.Parse(request.Context))
            {
                JsonElement root = context.RootElement;
                Check.True(root.GetProperty("historyOmitted").GetInt32() > 0);
                Check.True(root.GetProperty("attributedHistory").GetArrayLength() < history.Length);
                Check.Equal(history[history.Length - 1].Text,
                    root.GetProperty("attributedHistory")[root.GetProperty("attributedHistory").GetArrayLength() - 1].GetProperty("text").GetString());
                Check.Equal(memory, root.GetProperty("historicalMemory").GetProperty("text").GetString());
                JsonElement plan = root.GetProperty("proposal");
                Check.Equal(steps.Count, plan.GetArrayLength());
                Check.Equal(1, plan[0].GetProperty("optionId").GetInt32());
                Check.Equal(NpcTaskContract.MaximumSteps, plan[plan.GetArrayLength() - 1].GetProperty("optionId").GetInt32());
            }
            Check.True(request.Context.Length > 15000, "fixture should exercise the production 16k context bound");
            Check.True(request.Context.Length <= 16384);
        }

        private static void ProposalRejectsMalformedPlans()
        {
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(null));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new NpcActionDecision[0]));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(Enumerable.Range(0,
                NpcTaskContract.MaximumSteps + 1).Select(index => Wait(index + 1, 0, 0, 0, "wait", 10))));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                new NpcActionDecision { Option = new NpcActionOption { Id = 1, Kind = (NpcActionKind)999,
                    Target = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = "town", X = 1, Z = 2 } } }
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                new NpcActionDecision { Option = new NpcActionOption { Id = 1, Kind = NpcActionKind.MoveTo,
                    Target = new NpcActionTarget { Kind = (NpcActionTargetKind)999, WorldSpaceId = "town", X = 1, Z = 2 } } }
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                new NpcActionDecision { Option = new NpcActionOption { Id = 1, Kind = NpcActionKind.MoveTo } }
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                Move(1, double.NaN, 0, 2, "move", 300)
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                Move(1, 1, 0, 2, "move", 9)
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                Wait(1, 1, 0, 2, "wait", -1)
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                Move(1, 1, 0, 2, "move", 300),
                Wait(2, 9, 0, 9, "wrong destination", 15)
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                Move(1, 1, 0, 2, "move", 300),
                Wait(2, 1, 0, 2, "bad dependency", 15, sourceStep: 2)
            }));
            Check.Throws<ArgumentException>(() => new LocalDecisionWillingnessProposal(new[] {
                Move(1, 1, 0, 2, "bad \ud800 unicode", 300)
            }));
        }

        private static void RequestEnforcesBounds()
        {
            var proposal = new LocalDecisionWillingnessProposal(new[] { Wait(1, 0, 0, 0, "wait", 15) });
            Check.Throws<ArgumentException>(() => LocalDecisionWillingness.Build(Snapshot(), " ", new ConversationContextTurn[0], null, proposal));
            Check.Throws<ArgumentException>(() => LocalDecisionWillingness.Build(Snapshot(), new string('x', 8193),
                new ConversationContextTurn[0], null, proposal));
            Check.Throws<ArgumentException>(() => LocalDecisionWillingness.Build(Snapshot(), "bad \ud800 turn",
                new ConversationContextTurn[0], null, proposal));
            Check.Throws<ArgumentException>(() => LocalDecisionWillingness.Build(Snapshot(), "wait",
                new ConversationContextTurn[0], new string('m', 4097), proposal));
            Check.Throws<ArgumentException>(() => LocalDecisionWillingness.Build(Snapshot(), "wait", null, null, proposal));
            Check.Throws<ArgumentException>(() => LocalDecisionWillingness.Build(Snapshot(), "wait",
                new[] { new ConversationContextTurn(ConversationTextSpeaker.Player, new string('h', 8193)) }, null, proposal));
        }

        private static NpcDecisionRequest Build(ConversationContextSnapshot snapshot, string turn,
            IEnumerable<NpcActionDecision> decisions)
            => LocalDecisionWillingness.Build(snapshot, turn, new ConversationContextTurn[0], null,
                new LocalDecisionWillingnessProposal(decisions));

        private static NpcActionDecision Move(int id, double x, double y, double z, string label, int duration,
            int sourceStep = 0, int maxQuantity = 0)
            => new NpcActionDecision { DurationSeconds = duration, Option = new NpcActionOption { Id = id,
                Kind = NpcActionKind.MoveTo, Label = label, ItemSourceStep = sourceStep, MaximumQuantity = maxQuantity,
                Target = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = "settlement:market",
                    X = x, Y = y, Z = z } } };

        private static NpcActionDecision Wait(int id, double x, double y, double z, string label, int duration,
            int sourceStep = 0, int maxQuantity = 0)
            => new NpcActionDecision { DurationSeconds = duration, Option = new NpcActionOption { Id = id,
                Kind = NpcActionKind.Wait, Label = label, ItemSourceStep = sourceStep, MaximumQuantity = maxQuantity,
                Target = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = "settlement:market",
                    X = x, Y = y, Z = z } } };

        private static ConversationContextSnapshot Snapshot()
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, "resident:42", null,
                "session-willingness", 7, 2, new string('c', 32), new string('d', 64), "scene-willingness", 4);
            ConversationContextFact<string> empty = ConversationContextFact<string>.Unknown();
            var commitments = new[] {
                new ConversationContextCommitment("active-wait", "action-task", "executing", 3, NpcActionKind.Wait)
            };
            return new ConversationContextSnapshot(binding, 1000,
                ConversationContextFact<string>.Verified("Middas, 10:00", ConversationContextFactSource.Derived, 1000),
                ConversationContextFact<string>.Verified("Ari", ConversationContextFactSource.NativeActor, 1000),
                ConversationContextFact<ConversationDialogueRole>.Verified(ConversationDialogueRole.ManagedResident,
                    ConversationContextFactSource.NativeActor, 1000), empty,
                ConversationContextFact<string>.Verified("Daggerfall", ConversationContextFactSource.NpcSceneObservation, 1000),
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Verified(ConversationNpcActivity.Working,
                    ConversationContextFactSource.LiveObservation, 1000),
                ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
                ConversationContextFact<string>.Verified("kind, curious", ConversationContextFactSource.Derived, 1000),
                35, 17, null, commitments, new ConversationContextSourceHandle[0]);
        }
    }
}
