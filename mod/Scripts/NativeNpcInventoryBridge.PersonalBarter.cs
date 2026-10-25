using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // All delegates reread live native state. The UI holds virtual lines only.
    public sealed class PersonalBarterLiveChecks
    {
        public Func<bool> NpcAdultNow, NpcEligibleNow, NpcWillingNow, PlayerConsentedNow;
        public Func<DaggerfallUnityItem,bool> NpcValuesItemNow, OccupationallyDisposableNow;
        public Func<DaggerfallUnityItem,bool> ItemUniqueNow, ItemSourceAllowedNow;
    }

    public static partial class NativeNpcInventoryBridge
    {
        private sealed class NativeBarterMove
        {
            public PersonalBarterLine Proposed;
            public DaggerfallUnityItem Original, Moved;
            public ItemCollection Source, Destination;
            public int OriginalCount;
        }

        public static bool TryPreparePersonalBarter(WorldStateStore store,NativeNpcInventoryBinding npc,
            DaggerfallEntity npcEntity,PlayerEntity player,string transactionId,IList<PersonalBarterLine> lines,
            int netGoldToNpc,PersonalBarterLiveChecks checks,out PersonalBarterOrder prepared,out string reason,bool quoteOnly=false)
        {
            prepared=null;reason="The personal trade is no longer available.";
            if(npcEntity==null || npc==null || !ReferenceEquals(npcEntity.Items,npc.Items))return false;
            return TryPreparePersonalBarterForInventory(store,npc,player,transactionId,lines,netGoldToNpc,checks,
                npcDelta=>npc.Items.GetWeight()+npcDelta<=npcEntity.MaxEncumbrance,out prepared,out reason,quoteOnly);
        }

        // Entity and static owners use this same atomic transfer/rollback path.
        // Caller supplies the real owner's live capacity test for the proposed weight delta.
        internal static bool TryPreparePersonalBarterForInventory(WorldStateStore store,NativeNpcInventoryBinding npc,
            PlayerEntity player,string transactionId,IList<PersonalBarterLine> lines,int netGoldToNpc,
            PersonalBarterLiveChecks checks,Func<float,bool> npcCapacityForDelta,out PersonalBarterOrder prepared,out string reason,bool quoteOnly=false)
        {
            prepared=null;reason="The personal trade is no longer available.";
            try
            {
                var saved=ValidateBarterInputs(store,npc,player,transactionId,lines,checks,npcCapacityForDelta);
                if(saved==null)return false;
                prepared=BuildPreparedBarter(store,npc,player,saved,transactionId,lines,netGoldToNpc,checks,npcCapacityForDelta,quoteOnly);
                if(prepared==null)return false;
                reason=null;return true;
            }
            catch(Exception error) { reason=error.Message;prepared=null;return false; }
        }

        public static bool TryPersonalBarter(WorldStateStore store,NativeNpcInventoryBinding npc,
            DaggerfallEntity npcEntity,PlayerEntity player,PersonalBarterOrder order,double committedGameTime,
            PersonalBarterLiveChecks checks,NpcCommerceHaggleBasketBinding haggleBinding,
            Func<int> nativeBaseNetGoldNow,NpcCommerceHaggleBasketRound submittedRound,
            out PersonalBarterReceipt receipt,out string reason)
        {
            receipt=null;reason="The personal trade is no longer available.";
            if(npcEntity==null || npc==null || !ReferenceEquals(npcEntity.Items,npc.Items))return false;
            return TryPersonalBarterForInventory(store,npc,player,order,committedGameTime,checks,haggleBinding,
                nativeBaseNetGoldNow,submittedRound,npcDelta=>npc.Items.GetWeight()+npcDelta<=npcEntity.MaxEncumbrance,out receipt,out reason);
        }

        internal static bool TryPersonalBarterForInventory(WorldStateStore store,NativeNpcInventoryBinding npc,
            PlayerEntity player,PersonalBarterOrder order,double committedGameTime,PersonalBarterLiveChecks checks,
            NpcCommerceHaggleBasketBinding haggleBinding,Func<int> nativeBaseNetGoldNow,
            NpcCommerceHaggleBasketRound submittedRound,Func<float,bool> npcCapacityForDelta,
            out PersonalBarterReceipt receipt,out string reason)
        {
            receipt=null;reason="The personal trade is no longer available.";
            var moves=new List<NativeBarterMove>();int playerGoldBefore=0;bool touched=false,committed=false,unknown=false;
            try
            {
                if(order==null || haggleBinding==null || nativeBaseNetGoldNow==null || submittedRound==null || npcCapacityForDelta==null || double.IsNaN(committedGameTime) ||
                    double.IsInfinity(committedGameTime) || committedGameTime<0)return false;
                int nativeBaseNetGold=nativeBaseNetGoldNow();
                PersonalBarterOrder freshBase;
                string ignored;
                if(!TryPreparePersonalBarterForInventory(store,npc,player,order.TransactionId,order.Lines,
                    nativeBaseNetGold,checks,npcCapacityForDelta,out freshBase,out ignored,quoteOnly:true) ||
                    !NpcCommerceHagglingPolicy.TryResolveAcceptedBasketNetGold(haggleBinding,freshBase,
                        nativeBaseNetGold,submittedRound,out var approvedNetGold))return false;
                PersonalBarterOrder fresh;
                if(!TryPreparePersonalBarterForInventory(store,npc,player,order.TransactionId,order.Lines,
                    approvedNetGold,checks,npcCapacityForDelta,out fresh,out ignored) || !SameOrderExceptNet(order,fresh))return false;
                // Only the binding-approved net reaches native wallets and receipt.
                order=fresh;
                var saved=CheckBinding(store,npc,order.TransactionId);
                if(saved==null)return false;
                playerGoldBefore=player.GoldPieces;
                var npcBefore=Payloads(npc.Items);
                if(npcBefore==null)throw new InvalidOperationException("NPC inventory payload is invalid.");
                // Any later line can fail. Arm rollback before first native move.
                touched=true;
                foreach(var line in order.Lines)
                {
                    ItemCollection source=line.Direction==PersonalBarterDirection.NpcToPlayer?npc.Items:player.Items;
                    ItemCollection destination=line.Direction==PersonalBarterDirection.NpcToPlayer?player.Items:npc.Items;
                    var original=source.GetItem(line.ItemUid);
                    if(original==null || original.UID!=line.ItemUid || original.stackCount<line.Quantity ||
                        destination.GetItem(line.ItemUid)!=null)throw new InvalidOperationException("Basket item changed before transfer.");
                    int count=original.stackCount;
                    var record=new NativeBarterMove { Proposed=line,Original=original,Source=source,
                        Destination=destination,OriginalCount=count };
                    moves.Add(record);
                    var moved=line.Quantity==count?original:source.SplitStack(original,line.Quantity);
                    record.Moved=moved;
                    if(moved==null || moved.UID==0 || moved.stackCount!=line.Quantity ||
                        !ReferenceEquals(source.GetItem(moved.UID),moved))throw new InvalidOperationException("Native item split changed basket identity.");
                    source.RemoveItem(moved);destination.AddItem(moved,noStack:true);
                    if(ReferenceEquals(destination.GetItem(moved.UID),moved)==false || source.Contains(moved) ||
                        line.Quantity<count && original.stackCount!=count-line.Quantity)
                        throw new InvalidOperationException("Native basket transfer changed item identity or quantity.");
                }
                int playerGoldAfter=checked(playerGoldBefore-order.NetGoldToNpc);
                player.GoldPieces=playerGoldAfter;
                if(player.GoldPieces!=playerGoldAfter || player.CarriedWeight>player.MaxEncumbrance ||
                    !npcCapacityForDelta(0) || !LiveChecks(checks))
                    throw new InvalidOperationException("Basket capacity, consent or safety changed.");
                foreach(var move in moves)if(Protected(move.Original,checks) ||
                    move.Proposed.Direction==PersonalBarterDirection.PlayerToNpc && !checks.NpcValuesItemNow(move.Original))
                    throw new InvalidOperationException("Basket item is no longer tradeable.");
                var receiptLines=new List<PersonalBarterReceiptLine>();var npcAfter=Payloads(npc.Items);
                if(npcAfter==null)throw new InvalidOperationException("NPC inventory payload changed unexpectedly.");
                foreach(var move in moves)
                {
                    string beforePayload,afterPayload;
                    if(move.Proposed.Direction==PersonalBarterDirection.NpcToPlayer)
                    {
                        if(!npcBefore.TryGetValue(move.Original.UID,out beforePayload))throw new InvalidOperationException("NPC source item disappeared.");
                        npcAfter.TryGetValue(move.Original.UID,out afterPayload);
                        receiptLines.Add(new PersonalBarterReceiptLine { Direction=move.Proposed.Direction,
                            SourceItemUid=move.Original.UID,RecipientItemUid=move.Moved.UID,Quantity=move.Proposed.Quantity,
                            LoanPurpose=move.Proposed.LoanPurpose,OriginReceiptId=move.Proposed.OriginReceiptId,
                            PlayerInventoryTokenAfter=PlayerCommerceContract.InventorySnapshotToken("player",order.PlayerInventoryContainerId,
                                move.Moved.UID,move.Moved.stackCount),
                            NpcPayloadBefore=beforePayload,NpcPayloadAfter=afterPayload });
                    }
                    else
                    {
                        if(!npcAfter.TryGetValue(move.Moved.UID,out afterPayload))throw new InvalidOperationException("NPC recipient item disappeared.");
                        receiptLines.Add(new PersonalBarterReceiptLine { Direction=move.Proposed.Direction,
                            SourceItemUid=move.Original.UID,RecipientItemUid=move.Moved.UID,Quantity=move.Proposed.Quantity,
                            LoanPurpose=move.Proposed.LoanPurpose,OriginReceiptId=move.Proposed.OriginReceiptId,
                            PlayerInventoryTokenAfter=PlayerCommerceContract.InventorySnapshotToken("player",order.PlayerInventoryContainerId,
                                move.Original.UID,move.Original.stackCount),
                            NpcPayloadAfter=afterPayload });
                    }
                }
                var preparedReceipt=new PersonalBarterReceipt { TransactionId=order.TransactionId,NpcId=npc.PersonId,
                    ExpectedNpcRevision=saved.Revision,NpcInventoryRevisionAfter=saved.Revision+1,
                    NpcWalletGoldAfter=checked(saved.WalletGold+order.NetGoldToNpc),NetGoldToNpc=order.NetGoldToNpc,
                    PlayerInventoryTokenAfter=PlayerInventoryTokenAfter(order,moves),
                    PlayerWalletTokenAfter=PlayerCommerceContract.WalletSnapshotToken("player",order.PlayerWalletContainerId,player.GoldPieces),
                    Lines=receiptLines };
                var update=new NpcInventoryUpdate { PersonId=npc.PersonId,ExpectedRevision=saved.Revision,
                    WalletGold=preparedReceipt.NpcWalletGoldAfter,Items=CaptureItems(npc.Items) };
                string receiptId=PersonalTradeCommitBoundary.SourceBoundId("receipt",order.TransactionId);
                string effectId=PersonalTradeCommitBoundary.SourceBoundId("effect",order.TransactionId);
                unknown=true;PersonalBarterReceipt committedReceipt;
                bool accepted=PersonalTradeCommitBoundary.TryCommit(() => store.TryCommitPersonalBarter(update,
                    preparedReceipt,receiptId,effectId,committedGameTime),preparedReceipt,out committedReceipt);
                unknown=false;
                if(!accepted)throw new InvalidOperationException("Basket inventory, receipt or ownership changed.");
                committed=true;npc.Revision=saved.Revision+1;receipt=committedReceipt;reason=null;return true;
            }
            catch(Exception error) { if(unknown)throw;reason=error.Message;return false; }
            finally { if(touched && !committed && !unknown)RestoreBasket(moves,player,playerGoldBefore); }
        }

        private static NpcInventoryState ValidateBarterInputs(WorldStateStore store,NativeNpcInventoryBinding npc,
            PlayerEntity player,string transactionId,IList<PersonalBarterLine> lines,PersonalBarterLiveChecks checks,
            Func<float,bool> npcCapacityForDelta)
        {
            if(store==null || npc==null || player==null || player.Items==null || npc.Items==null || npcCapacityForDelta==null ||
                string.IsNullOrWhiteSpace(transactionId) || transactionId.Length>240 ||
                lines==null || checks==null || checks.NpcAdultNow==null || checks.NpcEligibleNow==null ||
                checks.NpcWillingNow==null || checks.PlayerConsentedNow==null || checks.NpcValuesItemNow==null ||
                checks.OccupationallyDisposableNow==null || checks.ItemUniqueNow==null || checks.ItemSourceAllowedNow==null)return null;
            return CheckBinding(store,npc,transactionId);
        }

        private static PersonalBarterOrder BuildPreparedBarter(WorldStateStore store,NativeNpcInventoryBinding npc,
            PlayerEntity player,NpcInventoryState saved,string transactionId,IList<PersonalBarterLine> lines,int netGold,
            PersonalBarterLiveChecks checks,Func<float,bool> npcCapacityForDelta,bool quoteOnly)
        {
            if(player.GoldPieces<0 || saved.WalletGold<0)return null;
            float playerDelta=0,npcDelta=0;var state=new PersonalBarterState { NpcId=npc.PersonId,
                NpcRevision=saved.Revision,NpcWalletGold=saved.WalletGold,PlayerGold=player.GoldPieces,
                PlayerInventoryContainerId="player-items:"+npc.PersonId,PlayerWalletContainerId="player-wallet:"+npc.PersonId,
                PlayerConsented=checks.PlayerConsentedNow(),NpcConsented=checks.NpcWillingNow(),NpcAdult=checks.NpcAdultNow(),
                NpcEligible=checks.NpcEligibleNow(),NpcWilling=checks.NpcWillingNow() };
            state.PlayerWalletToken=PlayerCommerceContract.WalletSnapshotToken("player",state.PlayerWalletContainerId,player.GoldPieces);
            var copied=new List<PersonalBarterLine>();var ids=new HashSet<ulong>();int npcItemCount=saved.Items.Count;
            foreach(var line in lines)
            {
                if(line==null || !ids.Add(line.ItemUid) || line.ItemUid==0 || line.Quantity<1)return null;
                var source=line.Direction==PersonalBarterDirection.NpcToPlayer?npc.Items:player.Items;
                var item=source.GetItem(line.ItemUid);
                string owner=item==null?null:store.NpcInventoryItemOwner(item.UID);
                if(item==null || item.UID!=line.ItemUid || item.stackCount<line.Quantity ||
                    line.Direction==PersonalBarterDirection.NpcToPlayer && owner!=npc.PersonId ||
                    line.Direction==PersonalBarterDirection.PlayerToNpc && owner!=null || Protected(item,checks))return null;
                if(line.Direction==PersonalBarterDirection.NpcToPlayer && line.Quantity==item.stackCount)npcItemCount--;
                if(line.Direction==PersonalBarterDirection.PlayerToNpc)npcItemCount++;
                float weight=item.EffectiveUnitWeightInKg();if(weight<0)return null;
                if(line.Direction==PersonalBarterDirection.NpcToPlayer) { playerDelta+=weight*line.Quantity;npcDelta-=weight*line.Quantity; }
                else { playerDelta-=weight*line.Quantity;npcDelta+=weight*line.Quantity; }
                var copy=new PersonalBarterLine { Direction=line.Direction,ItemUid=line.ItemUid,Quantity=line.Quantity,
                    LoanPurpose=line.LoanPurpose,OriginReceiptId=line.OriginReceiptId };
                var current=new PersonalBarterLineState { Direction=line.Direction,ItemUid=line.ItemUid,SourceQuantity=item.stackCount,
                    ReceiverCapacity=0,ItemAllowed=true,ItemValued=line.Direction==PersonalBarterDirection.NpcToPlayer || checks.NpcValuesItemNow(item),
                    ItemEquipped=item.IsEquipped,ItemOccupationallyNecessary=!checks.OccupationallyDisposableNow(item),
                    ItemUnique=item.IsArtifact || checks.ItemUniqueNow(item),ItemKnownStolen=!checks.ItemSourceAllowedNow(item),
                    ItemQuestProtected=item.IsQuestItem || item.IsSummoned || item is HouseholdPropertyKeyItem ||
                        item.ItemGroup==ItemGroups.Currency || item.ItemGroup==ItemGroups.Transportation || item.ItemGroup==ItemGroups.Deeds };
                if(line.Direction==PersonalBarterDirection.PlayerToNpc)
                {
                    copy.ExpectedPlayerInventoryToken=PlayerCommerceContract.InventorySnapshotToken("player",state.PlayerInventoryContainerId,
                        item.UID,item.stackCount);current.PlayerInventoryToken=copy.ExpectedPlayerInventoryToken;
                }
                copied.Add(copy);state.Lines.Add(current);
            }
            if(npcItemCount<0 || npcItemCount>NpcInventoryLimits.MaximumItems ||
                !quoteOnly && player.CarriedWeight+playerDelta-netGold*DaggerfallWorkshop.Game.Banking.DaggerfallBankManager.goldUnitWeightInKg>player.MaxEncumbrance ||
                !npcCapacityForDelta(npcDelta))return null;
            foreach(var current in state.Lines)current.ReceiverCapacity=current.SourceQuantity;
            var order=new PersonalBarterOrder { TransactionId=transactionId,NpcId=npc.PersonId,ExpectedNpcRevision=saved.Revision,
                PlayerInventoryContainerId=state.PlayerInventoryContainerId,PlayerWalletContainerId=state.PlayerWalletContainerId,
                ExpectedPlayerWalletToken=state.PlayerWalletToken,NetGoldToNpc=netGold,Lines=copied };
            return (quoteOnly?PersonalBarterContract.CanQuote(order,state):PersonalBarterContract.CanBegin(order,state))?order:null;
        }

        private static bool Protected(DaggerfallUnityItem item,PersonalBarterLiveChecks checks)
        { return item.IsQuestItem || item.IsSummoned || item.IsEquipped || item.IsArtifact || item is HouseholdPropertyKeyItem ||
            item.ItemGroup==ItemGroups.Currency || item.ItemGroup==ItemGroups.Transportation || item.ItemGroup==ItemGroups.Deeds ||
            !checks.OccupationallyDisposableNow(item) || checks.ItemUniqueNow(item) || !checks.ItemSourceAllowedNow(item); }
        private static bool LiveChecks(PersonalBarterLiveChecks checks)
        { return checks.NpcAdultNow() && checks.NpcEligibleNow() && checks.NpcWillingNow() && checks.PlayerConsentedNow(); }
        private static Dictionary<ulong,string> Payloads(ItemCollection items)
        { var result=new Dictionary<ulong,string>();foreach(var item in CaptureItems(items)){if(result.ContainsKey(item.Uid))return null;result.Add(item.Uid,item.NativePayload);}return result; }
        private static bool SameOrderExceptNet(PersonalBarterOrder a,PersonalBarterOrder b)
        {
            if(a==null || b==null || a.TransactionId!=b.TransactionId || a.NpcId!=b.NpcId || a.ExpectedNpcRevision!=b.ExpectedNpcRevision ||
                a.PlayerInventoryContainerId!=b.PlayerInventoryContainerId || a.PlayerWalletContainerId!=b.PlayerWalletContainerId ||
                a.ExpectedPlayerWalletToken!=b.ExpectedPlayerWalletToken || a.Lines==null || b.Lines==null || a.Lines.Count!=b.Lines.Count)return false;
            for(int index=0;index<a.Lines.Count;index++){var x=a.Lines[index];var y=b.Lines[index];if(x==null || y==null || x.Direction!=y.Direction || x.ItemUid!=y.ItemUid || x.Quantity!=y.Quantity || x.ExpectedPlayerInventoryToken!=y.ExpectedPlayerInventoryToken || x.LoanPurpose!=y.LoanPurpose || x.OriginReceiptId!=y.OriginReceiptId)return false;}return true;
        }
        private static string PlayerInventoryTokenAfter(PersonalBarterOrder order,List<NativeBarterMove> moves)
        {
            foreach(var move in moves)if(move.Proposed.Direction==PersonalBarterDirection.PlayerToNpc)
                return PlayerCommerceContract.InventorySnapshotToken("player",order.PlayerInventoryContainerId,move.Original.UID,move.Original.stackCount);
            foreach(var move in moves)return PlayerCommerceContract.InventorySnapshotToken("player",order.PlayerInventoryContainerId,move.Moved.UID,move.Moved.stackCount);
            throw new InvalidOperationException("Basket has no player inventory token.");
        }
        private static void RestoreBasket(List<NativeBarterMove> moves,PlayerEntity player,int playerGoldBefore)
        {
            Exception failure=null;
            try { for(int index=moves.Count-1;index>=0;index--){var move=moves[index];if(move.Moved==null)throw new InvalidOperationException("Native basket split has no recoverable item.");PersonalTradeCommitBoundary.Restore(move.Original,move.Moved,move.OriginalCount,
                item=>item.UID,uid=>move.Source.GetItem(uid),uid=>move.Destination.GetItem(uid),
                item=>move.Source.AddItem(item,noStack:true),item=>move.Source.RemoveItem(item),item=>move.Destination.RemoveItem(item),
                (item,count)=>item.stackCount=count,()=>player.GoldPieces=playerGoldBefore,()=>player.GoldPieces==playerGoldBefore);} }
            catch(Exception error) { failure=error; }
            player.GoldPieces=playerGoldBefore;
            if(player.GoldPieces!=playerGoldBefore)throw new InvalidOperationException("Native basket rollback lost player gold.",failure);
            if(failure!=null)throw new InvalidOperationException("Native basket rollback failed.",failure);
        }
    }
}
