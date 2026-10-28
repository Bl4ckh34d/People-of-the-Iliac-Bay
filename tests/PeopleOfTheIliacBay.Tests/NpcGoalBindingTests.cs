using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcGoalBindingTests
    {
        private sealed class StepSpec
        {
            public string Kind, Text, Target;
            public NpcGoalDuration Duration;
            public NpcGoalQuantity Quantity;
            public StepSpec(string kind, string text, string target = "", NpcGoalDuration duration = null,
                NpcGoalQuantity quantity = null)
            { Kind=kind;Text=text;Target=target;Duration=duration;Quantity=quantity; }
        }

        private static ConversationContextBinding Binding()
        {
            return new ConversationContextBinding(ConversationActorCategory.Mobile,"resident:1",null,"session:1",1,1,
                "0123456789abcdef0123456789abcdef","","scene:1",1);
        }

        private static NpcGoalProposal Proposal(params StepSpec[] specs)
        {
            var original=string.Join(" | ",Array.ConvertAll(specs,s=>s.Text));
            var steps=new List<NpcGoalStep>();
            int offset=0;
            for(int i=0;i<specs.Length;i++)
            {
                var spec=specs[i];
                steps.Add(new NpcGoalStep(spec.Kind,new NpcGoalSourceSpan(offset,spec.Text.Length,spec.Text),
                    spec.Target,spec.Duration,spec.Quantity,new string[0]));
                offset+=spec.Text.Length+(i+1<specs.Length?3:0);
            }
            return new NpcGoalProposal(Binding(),original,NpcGoalIntent.Request,steps,new string[0],new string[0],new string[0]);
        }

        private static NpcActionOption Wait(int id, double x=10)
        {
            return new NpcActionOption {Id=id,Kind=NpcActionKind.Wait,Label="wait at offered point",
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:1",X=x,Y=2,Z=3}};
        }

        private static NpcActionOption Move(int id, double x=10)
        {
            return new NpcActionOption {Id=id,Kind=NpcActionKind.MoveTo,Label="move to offered point",
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:1",X=x,Y=2,Z=3}};
        }

        private static NpcActionOption Purchase(int id, string item="42", int quantity=2)
        {
            return new NpcActionOption {Id=id,Kind=NpcActionKind.Purchase,Label="purchase offered item",
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,
                    PersonId="seller:1",ContainerId="7",ItemId=item,Quantity=quantity,QuotedGold=9}};
        }

        private static NpcActionOption Handover(int id, NpcActionDecision source)
        {
            var option=NpcActionSequenceChat.AcquiredItemHandoverSemantic(0,source);
            option.Id=id;
            return option;
        }

        private static NpcActionOption TransferRange(int id, int maximum)
        {
            return new NpcActionOption {Id=id,Kind=NpcActionKind.Transfer,MaximumQuantity=maximum,Label="transfer offered item",
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId="42",Quantity=1,
                    TransferDirection=NpcActionTransferDirection.NpcToCounterparty}};
        }

        private static bool Binds(NpcGoalProposal proposal, IList<IList<NpcActionOption>> candidates, IList<int> selected)
        {
            NpcBoundGoal bound;string reason;
            return NpcGoalBinding.TryBind(proposal,candidates,selected,out bound,out reason);
        }

        private static IList<NpcActionOption> Filter(NpcGoalProposal proposal, int index,
            IList<NpcActionOption> candidates, IList<NpcActionDecision> previous)
        { return NpcGoalBinding.FilterCandidates(proposal,index,candidates,previous); }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Single paid hire binds fixed one-day terms before separate native consent",()=>{
                var paid=new NpcActionOption {Id=8,Kind=NpcActionKind.Follow,PaidCompanion=true,
                    Label="hire for 120 gold / 24 game hours",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",Quantity=1}};
                var ordinary=new NpcActionOption {Id=7,Kind=NpcActionKind.Follow,
                    Target=NpcActionExecutor.CopyTarget(paid.Target)};
                var proposal=Proposal(new StepSpec("Follow","hire you for 24 hours","you",
                    new NpcGoalDuration(24,NpcGoalTimeUnit.GameHours,"24 hours")));
                var filtered=Filter(proposal,0,new[]{ordinary,paid},new NpcActionDecision[0]);
                Check.Equal(1,filtered.Count);Check.True(filtered[0].PaidCompanion);
                NpcActionDecision decision;string reason;
                Check.True(NpcGoalBinding.TryBindStep(proposal,0,filtered,8,new NpcActionDecision[0],out decision,out reason),reason);
                Check.Equal(86400,decision.DurationSeconds);
                var naturalDay=Proposal(new StepSpec("Follow","hire you for one day","you",
                    new NpcGoalDuration(24,NpcGoalTimeUnit.GameHours,"one day")));
                Check.True(NpcGoalBinding.TryBindStep(naturalDay,0,new[]{paid},8,new NpcActionDecision[0],out decision,out reason),reason);
                Check.Equal(86400,decision.DurationSeconds);
                var implicitDay=Proposal(new StepSpec("Follow","hire you","you"));
                Check.True(NpcGoalBinding.TryBindStep(implicitDay,0,new[]{paid},8,new NpcActionDecision[0],out decision,out reason),reason);
                Check.Equal(86400,decision.DurationSeconds);
                var shortHire=Proposal(new StepSpec("Follow","hire you for 10 minutes","you",
                    new NpcGoalDuration(10,NpcGoalTimeUnit.GameMinutes,"10 minutes")));
                Check.Equal(0,Filter(shortHire,0,new[]{paid},new NpcActionDecision[0]).Count);
                var chain=Proposal(new StepSpec("MoveTo","go to gate","gate"),
                    new StepSpec("Follow","hire you for 24 hours","you",
                        new NpcGoalDuration(24,NpcGoalTimeUnit.GameHours,"24 hours")));
                NpcActionDecision moved;
                Check.True(NpcGoalBinding.TryBindStep(chain,0,new[]{Move(1)},1,new NpcActionDecision[0],out moved,out reason),reason);
                Check.Equal(0,Filter(chain,1,new[]{paid},new[]{moved}).Count);
            }));
            cases.Add(("Goal candidate mask excludes Wait at the old location and preserves offered identity",()=>{
                var proposal=Proposal(new StepSpec("MoveTo","go to gate","gate"),
                    new StepSpec("Wait","wait there for 18 seconds","there",
                        new NpcGoalDuration(18,NpcGoalTimeUnit.GameSeconds,"18 seconds")));
                NpcActionDecision move;string reason;
                Check.True(NpcGoalBinding.TryBindStep(proposal,0,new[]{Move(4,30)},4,
                    new NpcActionDecision[0],out move,out reason),reason);
                var offered=new[]{Wait(2,10),Wait(8,30)};
                var filtered=Filter(proposal,1,offered,new[]{move});
                Check.Equal(1,filtered.Count);
                Check.Equal(8,filtered[0].Id);
                NpcActionDecision wait;
                Check.True(NpcGoalBinding.TryBindStep(proposal,1,filtered,8,new[]{move},out wait,out reason),reason);
                Check.Equal(18,wait.DurationSeconds);
                filtered[0].Target.X=55;
                Check.Equal(30d,offered[1].Target.X);
                Check.Equal(30d,move.Option.Target.X);
            }));
            cases.Add(("Goal candidate mask uses typed bounds without rewriting original quantity metadata",()=>{
                var proposal=Proposal(new StepSpec("Transfer","give five coins","",
                    quantity:new NpcGoalQuantity(5,"five")));
                var offered=new[]{TransferRange(3,2),TransferRange(7,8)};
                var filtered=Filter(proposal,0,offered,new NpcActionDecision[0]);
                Check.Equal(1,filtered.Count);
                Check.Equal(7,filtered[0].Id);
                Check.Equal(1,filtered[0].Target.Quantity);
                Check.Equal(8,filtered[0].MaximumQuantity);
                NpcActionDecision decision;string reason;
                Check.True(NpcGoalBinding.TryBindStep(proposal,0,filtered,7,new NpcActionDecision[0],out decision,out reason),reason);
                Check.Equal(5,decision.Option.Target.Quantity);
                Check.Equal(1,offered[1].Target.Quantity);
            }));
            cases.Add(("Goal candidate mask preserves duplicate-ID rejection and fails closed on invalid previous steps",()=>{
                var proposal=Proposal(new StepSpec("Wait","wait"));
                Check.Equal(0,Filter(proposal,0,new[]{Wait(2),Wait(2,20)},new NpcActionDecision[0]).Count);
                Check.Equal(0,Filter(proposal,0,new[]{Wait(2),Move(2)},new NpcActionDecision[0]).Count);
                Check.Equal(0,Filter(proposal,0,new[]{Wait(2)},new[]{new NpcActionDecision()}).Count);
                Check.Equal(0,Filter(null,0,new[]{Wait(2)},new NpcActionDecision[0]).Count);
            }));
            cases.Add(("Goal binding selects one exact offered ID and returns defensive copies",()=>{
                var proposal=Proposal(new StepSpec("Wait","wait here for 20 seconds","here",
                    new NpcGoalDuration(20,NpcGoalTimeUnit.GameSeconds,"20 seconds")));
                var offered=new[]{Wait(4,10),Wait(9,25)};
                NpcBoundGoal bound;string reason;
                Check.True(NpcGoalBinding.TryBind(proposal,new IList<NpcActionOption>[] {offered},new[]{9},out bound,out reason),reason);
                var first=bound.CopyDecisions();
                Check.Equal(9,first[0].Option.Id);Check.Equal(20,first[0].DurationSeconds);Check.Near(25,first[0].Option.Target.X);
                offered[1].Target.X=99;
                first[0].Option.Target.X=77;
                var second=bound.CopyDecisions();
                Check.Near(25,second[0].Option.Target.X);Check.Equal(9,second[0].Option.Id);
            }));

            cases.Add(("Goal binding rejects absent duplicate and wrong-kind candidate IDs",()=>{
                var proposal=Proposal(new StepSpec("Wait","wait here"));
                Check.True(!Binds(proposal,new IList<NpcActionOption>[] {new[]{Wait(4)}},new[]{8}));
                Check.True(!Binds(proposal,new IList<NpcActionOption>[] {new[]{Wait(4),Wait(4,20)}},new[]{4}));
                Check.True(!Binds(proposal,new IList<NpcActionOption>[] {new[]{Move(4)}},new[]{4}));
            }));

            cases.Add(("Goal binding requires explicit typed duration for Wait",()=>{
                NpcGoalProposal unstated=Proposal(new StepSpec("Wait","wait here"));
                NpcBoundGoal bound;string reason;
                Check.True(!NpcGoalBinding.TryBind(unstated,new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1},out bound,out reason),
                    "An unstated Wait duration must remain unresolved rather than becoming a default.");
                Check.True(bound==null);
                Check.True(!string.IsNullOrWhiteSpace(reason));

                NpcGoalProposal explicitDuration=Proposal(new StepSpec("Wait","wait here for 18 seconds","here",
                    new NpcGoalDuration(18,NpcGoalTimeUnit.GameSeconds,"18 seconds")));
                Check.True(NpcGoalBinding.TryBind(explicitDuration,new IList<NpcActionOption>[] {new[]{Wait(2)}},new[]{2},out bound,out reason),reason);
                Check.Equal(18,bound.CopyDecisions()[0].DurationSeconds);
            }));

            cases.Add(("Goal binding defaults only unspecified timed duration and preserves explicit duration",()=>{
                var unspecified=Proposal(new StepSpec("Follow","follow me","me"));
                var follow=new NpcActionOption {Id=1,Kind=NpcActionKind.Follow,Label="follow the player",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"}};
                NpcBoundGoal bound;string reason;
                Check.True(NpcGoalBinding.TryBind(unspecified,new IList<NpcActionOption>[] {new[]{follow}},new[]{1},out bound,out reason),reason);
                Check.Equal(300,bound.CopyDecisions()[0].DurationSeconds);
                var explicitValue=Proposal(new StepSpec("Wait","wait here for two minutes","here",
                    new NpcGoalDuration(2,NpcGoalTimeUnit.GameMinutes,"two minutes")));
                Check.True(NpcGoalBinding.TryBind(explicitValue,new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1},out bound,out reason),reason);
                Check.Equal(120,bound.CopyDecisions()[0].DurationSeconds);
                var typedAmount=Proposal(new StepSpec("Wait","wait here for ten seconds","here",
                    new NpcGoalDuration(60,NpcGoalTimeUnit.GameSeconds,"ten seconds")));
                Check.True(NpcGoalBinding.TryBind(typedAmount,new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1},out bound,out reason),reason);
                Check.Equal(60,bound.CopyDecisions()[0].DurationSeconds);
                var fractionalGameHour=Proposal(new StepSpec("Wait","wait here during an hour","here",
                    new NpcGoalDuration(0.25m,NpcGoalTimeUnit.GameHours,"an hour")));
                Check.True(NpcGoalBinding.TryBind(fractionalGameHour,new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1},out bound,out reason),reason);
                Check.Equal(900,bound.CopyDecisions()[0].DurationSeconds);
                Check.Throws<ArgumentException>(()=>Proposal(new StepSpec("Wait","wait here for 10 seconds","here",
                    new NpcGoalDuration(60,NpcGoalTimeUnit.GameSeconds,"60 seconds"))));
                Check.True(!Binds(Proposal(new StepSpec("Wait","wait here for 9 seconds","here",
                    new NpcGoalDuration(9,NpcGoalTimeUnit.GameSeconds,"9 seconds"))),
                    new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1}));
            }));

            cases.Add(("Goal binding uses typed quantities and enforces offered bounds",()=>{
                var proposal=Proposal(new StepSpec("Transfer","transfer three items","three items",null,
                    new NpcGoalQuantity(3,"three")));
                NpcBoundGoal bound;string reason;
                Check.True(NpcGoalBinding.TryBind(proposal,new IList<NpcActionOption>[] {new[]{TransferRange(3,5)}},new[]{3},out bound,out reason),reason);
                Check.Equal(3,bound.CopyDecisions()[0].Option.Target.Quantity);
                Check.Equal(5,bound.CopyDecisions()[0].Option.MaximumQuantity);
                Check.True(!Binds(Proposal(new StepSpec("Transfer","transfer six items","six items",null,
                    new NpcGoalQuantity(6,"six"))),
                    new IList<NpcActionOption>[] {new[]{TransferRange(3,5)}},new[]{3}));
                Check.True(!Binds(Proposal(new StepSpec("Transfer","transfer four items","four items",null,
                    new NpcGoalQuantity(4,"four"))),
                    new IList<NpcActionOption>[] {new[]{new NpcActionOption {Id=3,Kind=NpcActionKind.Transfer,
                        Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId="42",Quantity=2,
                            TransferDirection=NpcActionTransferDirection.NpcToCounterparty}}}},new[]{3}));
                var typedAmount=Proposal(new StepSpec("Transfer","transfer three items","three items",null,
                    new NpcGoalQuantity(4,"three")));
                Check.True(NpcGoalBinding.TryBind(typedAmount,new IList<NpcActionOption>[] {new[]{TransferRange(3,5)}},new[]{3},out bound,out reason),reason);
                Check.Equal(4,bound.CopyDecisions()[0].Option.Target.Quantity);
                var unspecified=Proposal(new StepSpec("Transfer","transfer an item","an item"));
                Check.True(NpcGoalBinding.TryBind(unspecified,new IList<NpcActionOption>[] {new[]{TransferRange(3,5)}},new[]{3},out bound,out reason),reason);
                Check.Equal(1,bound.CopyDecisions()[0].Option.Target.Quantity);
            }));

            cases.Add(("Goal binding rejects constraints missing fields and contradictory source literals",()=>{
                var source=Proposal(new StepSpec("Wait","wait here"));
                Check.True(!Binds(new NpcGoalProposal(source.Binding,source.OriginalText,source.Intent,new List<NpcGoalStep>(source.Steps),
                    new[]{"only if safe"},new string[0],new string[0]),new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1}));
                Check.True(!Binds(new NpcGoalProposal(source.Binding,source.OriginalText,source.Intent,new List<NpcGoalStep>(source.Steps),
                    new string[0],new[]{"do not wake me"},new string[0]),new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1}));
                Check.True(!Binds(new NpcGoalProposal(source.Binding,source.OriginalText,source.Intent,new List<NpcGoalStep>(source.Steps),
                    new string[0],new string[0],new[]{"target missing"}),new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1}));
                var tampered=new NpcGoalStep("Wait",new NpcGoalSourceSpan(0,9,"wait here"),"here",
                    new NpcGoalDuration(60,NpcGoalTimeUnit.GameSeconds,"60 seconds"),null,new string[0]);
                Check.Throws<ArgumentException>(()=>new NpcGoalProposal(source.Binding,source.OriginalText,source.Intent,new[]{tampered},
                    new string[0],new string[0],new string[0]));
                var unknown=new NpcGoalProposal(source.Binding,source.OriginalText,source.Intent,
                    new[]{new NpcGoalStep("Other",new NpcGoalSourceSpan(0,9,"wait here"),"",null,null,new string[0])},
                    new string[0],new string[0],new string[0]);
                Check.True(!Binds(unknown,new IList<NpcActionOption>[] {new[]{Wait(1)}},new[]{1}));
            }));

            cases.Add(("Goal binding derives conditional handover from exact preceding purchase",()=>{
                var proposal=Proposal(new StepSpec("Purchase","buy two items","two items",null,new NpcGoalQuantity(2,"two")),
                    new StepSpec("Transfer","give me those items","those items"));
                var purchase=Purchase(11);
                var dependent=Handover(22,new NpcActionDecision {Option=purchase,DurationSeconds=0});
                NpcBoundGoal bound;string reason;
                Check.True(NpcGoalBinding.TryBind(proposal,new IList<NpcActionOption>[] {new[]{purchase},new[]{dependent}},
                    new[]{11,22},out bound,out reason),reason);
                var decisions=bound.CopyDecisions();
                Check.Equal(1,decisions[1].Option.ItemSourceStep);Check.Equal("42",decisions[1].Option.Target.ItemId);
                Check.Equal(2,decisions[1].Option.Target.Quantity);
                var forged=Handover(22,new NpcActionDecision {Option=Purchase(11,"43"),DurationSeconds=0});
                Check.True(!Binds(proposal,new IList<NpcActionOption>[] {new[]{purchase},new[]{forged}},new[]{11,22}));
            }));

            cases.Add(("Goal binding binds later Wait to exact static MoveTo target",()=>{
                var proposal=Proposal(new StepSpec("MoveTo","go to the bridge","the bridge"),
                    new StepSpec("Wait","wait there for 20 seconds","there",
                        new NpcGoalDuration(20,NpcGoalTimeUnit.GameSeconds,"20 seconds")));
                var move=Move(31,40);
                var prior=new NpcActionDecision {Option=move,DurationSeconds=0};
                NpcActionOption wait;string reason;
                Check.True(NpcTaskPositionBinding.TryBindWaitOptionAfterMoveTo(prior,Wait(32,99),33,out wait,out reason),reason);
                NpcBoundGoal bound;
                Check.True(NpcGoalBinding.TryBind(proposal,new IList<NpcActionOption>[] {new[]{move},new[]{wait}},new[]{31,33},out bound,out reason),reason);
                Check.Near(40,bound.CopyDecisions()[1].Option.Target.X);
                Check.True(!Binds(proposal,new IList<NpcActionOption>[] {new[]{move},new[]{Wait(33,99)}},new[]{31,33}));
            }));

            cases.Add(("Goal binding verifies previous steps before binding one current step",()=>{
                var proposal=Proposal(new StepSpec("MoveTo","go to the bridge","the bridge"),
                    new StepSpec("Wait","wait there for 20 seconds","there",
                        new NpcGoalDuration(20,NpcGoalTimeUnit.GameSeconds,"20 seconds")));
                var move=Move(41,40);
                var first=Wait(42,40);
                NpcActionDecision prior, next;string reason;
                Check.True(NpcGoalBinding.TryBindStep(proposal,0,new[]{move},41,new NpcActionDecision[0],out prior,out reason),reason);
                Check.True(NpcGoalBinding.TryBindStep(proposal,1,new[]{first},42,new[]{prior},out next,out reason),reason);
                Check.Near(40,next.Option.Target.X);
                Check.True(!NpcGoalBinding.TryBindStep(proposal,1,new[]{Wait(42,99)},42,new[]{prior},out next,out reason));
            }));

            cases.Add(("Goal binding rejects partial candidate coverage",()=>{
                var proposal=Proposal(new StepSpec("MoveTo","go to the bridge","the bridge"),new StepSpec("Wait","wait there"));
                Check.True(!Binds(proposal,new IList<NpcActionOption>[] {new[]{Move(1)},new NpcActionOption[0]},new[]{1,2}));
                Check.True(!Binds(proposal,new IList<NpcActionOption>[] {new[]{Move(1)}},new[]{1}));
            }));

            cases.Add(("Goal binding preserves single native TakeSeat and non-timed willingness metadata",()=>{
                var takeSeat=Proposal(new StepSpec("TakeSeat","take a seat for 60 seconds","a seat",
                    new NpcGoalDuration(60,NpcGoalTimeUnit.GameSeconds,"60 seconds")));
                var seat=new NpcActionOption {Id=51,Kind=NpcActionKind.TakeSeat,Label="take offered seat",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:1",X=5,Y=1,Z=2}};
                NpcBoundGoal bound;string reason;
                Check.True(NpcGoalBinding.TryBind(takeSeat,new IList<NpcActionOption>[] {new[]{seat}},new[]{51},out bound,out reason),reason);
                Check.Equal(60,bound.CopyDecisions()[0].DurationSeconds);

                var interact=Proposal(new StepSpec("Interact","speak with the keeper","the keeper"));
                var speaker=new NpcActionOption {Id=52,Kind=NpcActionKind.Interact,Label="speak with keeper",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="keeper:1"}};
                Check.True(NpcGoalBinding.TryBind(interact,new IList<NpcActionOption>[] {new[]{speaker}},new[]{52},out bound,out reason),reason);
                Check.Equal(300,bound.CopyDecisions()[0].DurationSeconds);
            }));
        }
    }
}
