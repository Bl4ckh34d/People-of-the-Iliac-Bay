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
    // Opt-in native proof. It uses an existing actor and owned native item; it
    // requests Follow through the existing public action adapter, then verifies
    // that loan/return never replace or cancel that real binding.
    internal static class PersonalItemLoanNativeProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, ResidentActor actor, WorldStateStore store,
            DaggerfallUnityItem requestedItem = null)
        {
            var game = GameManager.Instance;
            string probe = Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE");
            Require((probe == "personal-loan" || probe == "personal-trade") && host && actor && store != null && game,
                "Personal item loan requires explicit owned fixture.");
            string person = actor.Resident.Id;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            Require(owner && owner.Binding != null, "Loan actor lacks native inventory binding.");
            var beforeAction = store.ActiveNpcAction(person);
            DaggerfallUnityItem item = requestedItem ?? FindDisposable(owner.Binding.Items);
            Require(item != null, "Follow actor has no owned disposable native item.");
            game.PlayerMotor.transform.position = actor.transform.position + actor.transform.right * 2f;
            int playerGold = game.PlayerEntity.GoldPieces;
            int npcGold = store.ReadNpcInventory(person).WalletGold;
            int receipts = store.ReadPlayerCommerceReceipts().Entries.Count;
            int count = item.stackCount;
            try
            {
                DaggerfallTalkWindow talk = null;
                yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                PersonalTradeNativeProbe.CaptureTradeContext(host,talk,"May I borrow an item?");
                if(store.CanBeginPlayerItemLoan(person))SetTrust(store,person,50,"loan-probe-low");
                Require(!store.CanBeginPlayerItemLoan(person),"Low-trust fixture was not established.");
                var available=typeof(PeopleOfTheIliacBayMod).GetMethod("PersonalTradeActorAvailable",Private,null,
                    new[]{typeof(ResidentActor),typeof(string)},null);
                Require((bool)available.Invoke(host,new object[]{actor,person}),"Loan rejection fixture is not an available native actor.");
                PersonalTradeWindowSession declined;
                string declinedReason;
                Require(!Create(host, LoanPurpose.Lend, out declined, out declinedReason),
                    "Unknown or low trust admitted a new loan.");
                SetTrust(store, person, 80, "loan-probe-high");
                DaggerfallUI.Instance.PopToHUD();yield return null;
                if(beforeAction==null)
                {
                    var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();string reason="No Follow option.";
                    foreach(var option in streets.ConversationActionOptions(person,"Follow me."))
                        if(option.Kind==NpcActionKind.Follow && NpcProbeCommandSource.TryAcceptAction(streets,person,
                            "loan-probe-follow:"+Guid.NewGuid().ToString("N"),new NpcActionDecision {Option=option,DurationSeconds=300},out reason))
                        {beforeAction=store.ActiveNpcAction(person);break;}
                    Require(beforeAction!=null,"Native Follow request was not accepted: "+reason);
                }
                Require(beforeAction.Kind==NpcActionKind.Follow && !NpcActionExecutor.IsTerminal(beforeAction),"Loan fixture needs actual active Follow.");
                yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                var window = Open(host, talk, LoanPurpose.Lend);
                yield return null;
                Stage(window, PersonalTradeSide.Npc, item, 1);
                Call(window, "RequestQuote");
                Require(item.stackCount == count && game.PlayerEntity.GoldPieces == playerGold &&
                    store.ReadPlayerCommerceReceipts().Entries.Count == receipts && SameAction(beforeAction, store.ActiveNpcAction(person)),
                    "Loan preview or cancel changed item, gold, receipt, or Follow.");
                Call(window, "CloseUnchanged");
                yield return null;
                Require(item.stackCount == count && game.PlayerEntity.GoldPieces == playerGold &&
                    store.ReadNpcInventory(person).WalletGold == npcGold && store.ReadPlayerCommerceReceipts().Entries.Count == receipts &&
                    SameAction(beforeAction, store.ActiveNpcAction(person)), "Loan cancel changed state or Follow.");

                yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                window = Open(host, talk, LoanPurpose.Lend);
                yield return null;
                Stage(window, PersonalTradeSide.Npc, item, 1);
                Call(window, "RequestQuote");
                Require(((PersonalTradeWindowQuote)Field(window, "quote")).CanSubmit, "Zero-gold Take+Lend was not accepted.");
                Call(window, "SubmitOffer");
                yield return null;
                var loanEntry=store.ReadPlayerCommerceReceipts().Entries.FindLast(entry=>entry.NpcId==person && entry.LoanPurpose==LoanPurpose.Lend);
                var lent = loanEntry==null?null:game.PlayerEntity.Items.GetItem(loanEntry.RecipientItemUid);
                Require(lent != null && lent.stackCount == 1 && game.PlayerEntity.GoldPieces == playerGold &&
                    store.ReadNpcInventory(person).WalletGold == npcGold && store.ReadPlayerCommerceReceipts().Entries.Count == receipts + 1,
                    "Take+Lend did not commit exact zero-gold native receipt. status=" +
                    ((TextLabel)Field(window,"statusLabel")).Text + "; receipt=" + (loanEntry != null) +
                    "; item=" + (lent == null ? "missing" : lent.UID + "/" + lent.stackCount) +
                    "; gold=" + game.PlayerEntity.GoldPieces + "/" + playerGold + "; npcGold=" +
                    store.ReadNpcInventory(person).WalletGold + "/" + npcGold + "; receipts=" +
                    store.ReadPlayerCommerceReceipts().Entries.Count + "/" + (receipts + 1));
                SetTrust(store, person, 0, "loan-probe-drop");
                yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                window = Open(host, talk, LoanPurpose.Return);
                yield return null;
                Stage(window, PersonalTradeSide.Player, lent, 1);
                Call(window, "RequestQuote");
                Require(((PersonalTradeWindowQuote)Field(window, "quote")).CanSubmit, "Trust drop blocked exact loan return.");
                Call(window, "SubmitOffer");
                yield return null;
                Require(game.PlayerEntity.Items.GetItem(lent.UID) == null && owner.Binding.Items.GetItem(lent.UID) != null &&
                    game.PlayerEntity.GoldPieces == playerGold && store.ReadNpcInventory(person).WalletGold == npcGold &&
                    store.ReadPlayerCommerceReceipts().Entries.Count == receipts + 2 && SameAction(beforeAction, store.ActiveNpcAction(person)),
                    "Give+Return did not preserve zero gold, receipt, or Follow.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_LOAN_NATIVE_PASS: takeLend=true; giveReturn=true; zeroGold=true; trustDropReturn=true; followUnchanged=true");
            }
            finally { DaggerfallUI.Instance.PopToHUD(); }
        }

        private static bool Create(PeopleOfTheIliacBayMod host, LoanPurpose purpose, out PersonalTradeWindowSession session, out string reason)
        {
            return host.TryCreatePersonalItemLoanWindowSession(purpose, out session, out reason);
        }
        private static PersonalTradeWindow Open(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, LoanPurpose purpose)
        {
            PersonalTradeNativeProbe.CaptureTradeContext(host,talk,purpose==LoanPurpose.Lend?"May I borrow an item?":"I want to return the borrowed item.");
            string reason;
            Require(host.TryOpenPersonalItemLoanWindow(talk, purpose, out reason), "Loan handoff refused: " + reason);
            var window = DaggerfallUI.UIManager.TopWindow as PersonalTradeWindow;
            Require(window != null, "Loan handoff did not open personal trade window."); return window;
        }
        private static void SetTrust(WorldStateStore store, string person, int target, string prefix)
        {
            for (int index = 0; index < 20; index++)
            {
                var current = store.ReadNpcRelationship(person, NpcDirectedRelationship.PlayerTargetId);
                int value = current == null || !current.TrustKnown ? 50 : current.ReliabilityTrust;
                if (value == target) return;
                int delta = Math.Min(10, Math.Abs(target - value)) * (target > value ? 1 : -1);
                var kind = delta > 0 ? NpcExperienceEventKind.ObligationFulfilled : NpcExperienceEventKind.ObligationBreached;
                string id = prefix + ":" + index;
                var evidence = NpcExperienceEvent.FromNativeOutcome(person, person, id + ":receipt", id + ":episode", id + ":outcome", kind,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId);
                var proposal = new NpcRelationshipLedgerUpdateProposal { ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
                    ObserverId = person, TargetId = NpcDirectedRelationship.PlayerTargetId,
                    RelationshipUpdate = new NpcRelationshipUpdateProposal { ExpectedRevision = current == null ? 0 : current.Revision, Evidence = evidence, TrustDelta = delta } };
                Require(store.TryCommitNpcRelationship(proposal, proposal.ExpectedLedgerRevision), "Controlled native trust fixture failed.");
            }
            Require(false, "Controlled trust fixture did not reach target.");
        }
        private static DaggerfallUnityItem FindDisposable(ItemCollection items)
        { for (int i = 0; i < items.Count; i++) { var item = items.GetItem(i); if (item != null && item.stackCount > 0 && !item.IsQuestItem && !item.IsEquipped && !item.IsSummoned && !item.IsArtifact && item.value > 0) return item; } return null; }
        private static object Field(PersonalTradeWindow window, string name) { return typeof(PersonalTradeWindow).GetField(name, Private).GetValue(window); }
        private static void Stage(PersonalTradeWindow window, PersonalTradeSide side, DaggerfallUnityItem item, int quantity)
        { var method = typeof(PersonalTradeWindow).GetMethod("SetQuantity", Private, null, new[] { typeof(PersonalTradeSide), typeof(DaggerfallUnityItem), typeof(int) }, null); Require((bool)method.Invoke(window, new object[] { side, item, quantity }), "Loan item cannot be staged."); Call(window, "RefreshView"); }
        private static void Call(PersonalTradeWindow window, string name) { typeof(PersonalTradeWindow).GetMethod(name, Private).Invoke(window, null); }
        private static bool SameAction(NpcActionJob left, NpcActionJob right) { return left != null && right != null && left.Id == right.Id && left.Kind == right.Kind && left.FinishAt == right.FinishAt && !NpcActionExecutor.IsTerminal(right); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
