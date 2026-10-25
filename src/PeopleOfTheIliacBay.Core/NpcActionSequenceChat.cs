using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public static class NpcActionSequenceChat
    {
        // Clauses are original player spans supplied by the semantic decomposition, never rewritten command templates.
        public static List<NpcActionDecision> ParseSemantic(IList<string> clauses,IList<IList<NpcActionOption>> options,IList<string> outputs)
        {
            if(clauses==null || options==null || outputs==null || clauses.Count<2 || clauses.Count>NpcTaskContract.MaximumSteps ||
                options.Count!=clauses.Count || outputs.Count!=clauses.Count)return null;
            var result=new List<NpcActionDecision>();
            int totalLength=0;
            for(int i=0;i<clauses.Count;i++)
            {
                if(string.IsNullOrWhiteSpace(clauses[i]) || (totalLength+=clauses[i].Length)>2000)return null;
                var decision=NpcActionChat.ParseSemantic(outputs[i],options[i]);
                if(decision==null || decision.Option.PaidCompanion || decision.Option.Kind==NpcActionKind.CastSpell || !NpcTaskContract.Supported(decision.Option.Kind) ||
                    decision.Option.Kind==NpcActionKind.Follow && i!=clauses.Count-1 ||
                    decision.Option.ItemSourceStep<0 || decision.Option.ItemSourceStep>i)return null;
                if(decision.Option.Kind==NpcActionKind.Wait && i>0)
                {
                    // This proves the selected native target is the preceding
                    // static arrival destination; the task-step prompt must preserve the
                    // player's textual target because Core does not parse prose.
                    if(!NpcTaskPositionBinding.IsBoundWaitAfterStaticArrival(result[i-1],decision))return null;
                }
                result.Add(decision);
            }
            return result;
        }
        public static NpcActionOption AcquiredItemHandoverSemantic(int sourceIndex,NpcActionDecision source)
        {
            return source==null?null:BindAcquiredItemHandover(sourceIndex,source.Option);
        }
        private static NpcActionOption BindAcquiredItemHandover(int sourceIndex,NpcActionOption source)
        {
            if(sourceIndex<0 || sourceIndex>=NpcTaskContract.MaximumSteps-1 || source==null || source.Target==null)return null;
            var target=source.Target;
            bool purchase=source.Kind==NpcActionKind.Purchase && !string.IsNullOrEmpty(target.ContainerId);
            bool homeRetrieval=source.Kind==NpcActionKind.Transfer && target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc &&
                target.Kind==NpcActionTargetKind.IndoorPoint && !string.IsNullOrEmpty(target.ContainerId);
            ulong uid;
            if((!purchase && !homeRetrieval) || !ulong.TryParse(target.ItemId,out uid) || uid==0 || target.Quantity<1 || target.Quantity>1000000)return null;
            string origin=purchase?"preceding purchase":"preceding home-container retrieval";
            return new NpcActionOption {Id=1,Kind=NpcActionKind.Transfer,ItemSourceStep=sourceIndex+1,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=target.ItemId,Quantity=target.Quantity,
                    TransferDirection=NpcActionTransferDirection.NpcToCounterparty},
                Label="hand over the exact "+target.Quantity+" item(s) acquired by the "+origin+" to the player, only after that step succeeds"};
        }
        public static NpcActionOption PurchasedHandover(int purchaseIndex,IList<NpcActionOption> purchases)
        {
            return purchases!=null && purchases.Count==1 && purchases[0].Kind==NpcActionKind.Purchase?
                BindAcquiredItemHandover(purchaseIndex,purchases[0]):null;
        }
        // Preserve every original target and ID. Add separate candidates for a
        // later Wait at the exact selected static arrival destination.
        public static List<NpcActionOption> BindWaitOptionsAfterMoveTo(NpcActionDecision previous,
            IList<NpcActionOption> options)
        {
            var result=new List<NpcActionOption>();
            if(options==null)return result;
            int nextId=0;
            foreach(var option in options)
            {
                if(option==null || option.Target==null)continue;
                if(option.Id>nextId)nextId=option.Id;
                result.Add(new NpcActionOption {Id=option.Id,Kind=option.Kind,Label=option.Label,
                    MaximumQuantity=option.MaximumQuantity,ItemSourceStep=option.ItemSourceStep,
                    PaidCompanion=option.PaidCompanion,
                    Target=NpcActionExecutor.CopyTarget(option.Target),
                    CompanionCast=option.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(option.CompanionCast)});
            }
            if(nextId==int.MaxValue)return result;
            foreach(var option in options)
            {
                if(option==null || option.Target==null || option.Kind!=NpcActionKind.Wait)continue;
                if(nextId==int.MaxValue)return result;
                NpcActionOption bound;string reason;
                if(NpcTaskPositionBinding.TryBindWaitOptionAfterStaticArrival(previous,option,++nextId,out bound,out reason))
                {
                    result.Add(bound);
                }
            }
            return result;
        }
        public static string AcceptedIntentFallback(IList<NpcActionDecision> decisions)
        {
            if(decisions==null || decisions.Count==0)return null;
            return "I agreed to this ordered task. I will begin its steps after our conversation ends.";
        }
    }
}
