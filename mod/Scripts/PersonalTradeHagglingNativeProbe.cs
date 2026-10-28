using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in fixture. It invokes the live window session's captured callbacks;
    // it never replaces a quote, mood, receipt, inventory, or wallet adapter.
    internal static class PersonalTradeHagglingNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,ResidentActor actor,WorldStateStore store,DaggerfallUnityItem item)
        {
            var game=GameManager.Instance;
            string mode=Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE");
            Require((mode=="personal-trade" || mode=="personal-haggling") && host && actor && store!=null && game &&
                NativeProbeText.IsTestCharacter(game.PlayerEntity.Name),
                "Personal haggling needs its owned explicit fixture.");
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            Require(owner && owner.Binding!=null && item!=null && ReferenceEquals(owner.Binding.Items.GetItem(item.UID),item),
                "The controlled native trade stock is not owned by this existing actor.");
            game.PlayerMotor.transform.position=actor.transform.position+actor.transform.right*2f;
            int originalGold=game.PlayerEntity.GoldPieces;
            game.PlayerEntity.GoldPieces=Math.Max(originalGold,5000);
            try
            {
                int moodBefore=Mood(store,actor.Resident.Id);
                DaggerfallTalkWindow talk=null;
                yield return ConversationMoodProbe.OpenTalk(host,game,actor,value=>talk=value);
                var window=Open(host,talk);yield return null;
                var session=(PersonalTradeWindowSession)Field(window,"session");
                var staging=Stage(session,item);
                var baseline=Proposal(session,staging,0,false);
                PersonalTradeWindowQuote quote;string reason;
                Require(session.Prepare(baseline,out quote,out reason) && quote!=null && quote.NetGoldToNpc>0,
                    "Actual personal haggling session did not price existing NPC stock: "+reason);
                // Quote refreshes and text-only price edits must be inert.
                Require(session.Prepare(baseline,out quote,out reason),"A fresh bound baseline quote failed.");
                var edited=Proposal(session,staging,Math.Max(0,quote.NetGoldToNpc-1),true);
                Require(session.Prepare(edited,out quote,out reason),"A price edit failed before submission.");
                Require(Mood(store,actor.Resident.Id)==moodBefore,"Quote or unsent price edit changed mood.");
                Call(window,"CloseUnchanged");yield return null;
                Require(Mood(store,actor.Resident.Id)==moodBefore,"Cancelled personal trade created success or proposal mood.");

                yield return ConversationMoodProbe.OpenTalk(host,game,actor,value=>talk=value);
                window=Open(host,talk);yield return null;
                session=(PersonalTradeWindowSession)Field(window,"session");staging=Stage(session,item);baseline=Proposal(session,staging,0,false);
                Require(session.Prepare(baseline,out quote,out reason) && quote!=null,"Bound haggling baseline failed: "+reason);
                int nativeBase=quote.NetGoldToNpc;
                var bound=Bound(session);
                Require(bound!=null && bound.InitialNetGoldToNpc==nativeBase && nativeBase-bound.NpcMinimumNetGoldToNpc>=10,
                    "Existing stock cannot demonstrate a positive per-item haggling advantage.");
                int fixedLimit=bound.NpcMinimumNetGoldToNpc;
                var poor=Proposal(session,staging,Math.Min(fixedLimit-1,nativeBase-(int)Math.Ceiling(nativeBase*0.5)),true);
                Require(session.Prepare(poor,out var firstPoor,out reason) && firstPoor.Disposition==NpcCommerceHaggleDisposition.Reject &&
                    firstPoor.ProposalMoodDelta<0,"Very poor submitted price was not rejected with a negative mood effect.");
                session.ProposalSubmitted(poor,firstPoor);
                int moodAfterFirst=Mood(store,actor.Resident.Id);
                Require(moodAfterFirst==moodBefore+firstPoor.ProposalMoodDelta,"First poor proposal did not change mood exactly once.");
                session.ProposalSubmitted(poor,firstPoor);
                Require(Mood(store,actor.Resident.Id)==moodAfterFirst,"Duplicate proposal callback changed mood twice.");
                Require(session.Prepare(poor,out var secondPoor,out reason) && secondPoor.ProposalRound>firstPoor.ProposalRound &&
                    secondPoor.ProposalMoodDelta<0,"Later poor proposal was not independently evaluated.");
                session.ProposalSubmitted(poor,secondPoor);
                int moodAfterSecond=Mood(store,actor.Resident.Id);
                Require(moodAfterSecond==moodAfterFirst+secondPoor.ProposalMoodDelta && moodAfterSecond<moodAfterFirst,
                    "A later poor proposal did not worsen mood once more.");
                var improved=Proposal(session,staging,fixedLimit,true);
                Require(session.Prepare(improved,out var counter,out reason) && counter.NetGoldToNpc>=fixedLimit &&
                    counter.NetGoldToNpc<=nativeBase && (counter.Disposition==NpcCommerceHaggleDisposition.CounterOffer ||
                    counter.Disposition==NpcCommerceHaggleDisposition.Accept),"Improved offer did not approach the fixed native limit.");
                session.ProposalSubmitted(improved,counter);
                var unchanged=Bound(session);
                Require(ReferenceEquals(bound,unchanged) && unchanged.NpcMinimumNetGoldToNpc==fixedLimit &&
                    unchanged.InitialNetGoldToNpc==nativeBase,"Submitted offers ratcheted the original bound.");
                // Paying the original native quote is valid and leaves at least ten gold advantage per item over the fixed limit.
                var generous=Proposal(session,staging,nativeBase,true);
                Require(session.Prepare(generous,out var accepted,out reason) && accepted.CanSubmit &&
                    accepted.Disposition==NpcCommerceHaggleDisposition.Accept,"Exact native quote was not accepted after negotiation.");
                session.ProposalSubmitted(generous,accepted);
                int moodBeforeReceipt=Mood(store,actor.Resident.Id);string receiptId;
                Require(session.Submit(generous,accepted,out receiptId,out reason) && !string.IsNullOrWhiteSpace(receiptId),
                    "Accepted real haggling basket did not commit: "+reason);
                int expected=NpcCommerceHagglingPolicy.CommittedTradeMoodDelta(bound,nativeBase);
                Require(expected==0 && Mood(store,actor.Resident.Id)==moodBeforeReceipt,
                    "Committed trade must not apply the offer mood a second time.");
                int moodAfterReceipt=Mood(store,actor.Resident.Id);
                session.ProposalSubmitted(generous,accepted);
                Require(Mood(store,actor.Resident.Id)==moodAfterReceipt,"Completed receipt mood was replayed.");
                Require(store.ReadPersonalBarterReceipts().Entries.Find(entry=>entry.ReceiptId==receiptId)!=null,
                    "Actual haggling receipt was not retained.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_HAGGLING_NATIVE_PASS: stock=true; quoteInert=true; cancelInert=true; "+
                    "poorOnce=true; poorLater=true; fixedLimit=true; receiptMood=true; receipt="+receiptId);
            }
            finally { DaggerfallUI.Instance.PopToHUD();game.PlayerEntity.GoldPieces=originalGold; }
        }

        private static PersonalTradeWindow Open(PeopleOfTheIliacBayMod host,DaggerfallTalkWindow talk)
        {
            PersonalTradeNativeProbe.CaptureTradeContext(host,talk,"May I trade with you?");
            var args=new object[]{talk,null};Require((bool)typeof(PeopleOfTheIliacBayMod).GetMethod("TryOpenPersonalTradeWindow",Private).Invoke(host,args),
                "Actual Talk trade handoff refused: "+args[1]);
            var window=DaggerfallUI.UIManager.TopWindow as PersonalTradeWindow;Require(window!=null,"Actual trade handoff did not open its window.");return window;
        }
        private static PersonalTradeStaging Stage(PersonalTradeWindowSession session,DaggerfallUnityItem item)
        {
            Require(session!=null && session.CanStage!=null && session.CanStage(PersonalTradeSide.Npc,item),"Existing stock is no longer stageable.");
            var staging=new PersonalTradeStaging();Require(staging.TrySetQuantity(PersonalTradeSide.Npc,item.UID,item.stackCount,1,false),"Could not stage exact existing stock.");return staging;
        }
        private static PersonalTradeWindowProposal Proposal(PersonalTradeWindowSession session,PersonalTradeStaging staging,int offer,bool submitted)
        { var proposal=PersonalTradeWindowProposal.Create(session.NpcId,session.StoreEpoch,staging,offer,submitted);Require(proposal!=null,"Could not create a bounded haggling proposal.");return proposal; }
        private static NpcCommerceHaggleBasketBinding Bound(PersonalTradeWindowSession session)
        {
            object target=session.Prepare.Target;if(target==null)return null;
            foreach(var field in target.GetType().GetFields(Private|BindingFlags.Public))
                if(typeof(NpcCommerceHaggleBasketBinding).IsAssignableFrom(field.FieldType))return field.GetValue(target) as NpcCommerceHaggleBasketBinding;
            return null;
        }
        private static int Mood(WorldStateStore store,string person){return store.CaptureConversationMood(person,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Value;}
        private static object Field(PersonalTradeWindow window,string name){return typeof(PersonalTradeWindow).GetField(name,Private).GetValue(window);}
        private static void Call(PersonalTradeWindow window,string name){typeof(PersonalTradeWindow).GetMethod(name,Private).Invoke(window,null);}
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
