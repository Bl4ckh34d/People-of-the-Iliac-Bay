using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcInventoryTests
    {
        private static WorldStateStore Create(out string a,out string b)
        {var s=new WorldStateStore(42);var t=s.GetOrCreate(100,new[]{10,20},1);a=t.Households[0].Residents[0].Id;b=t.Households[1].Residents[0].Id;return s;}
        private static NpcInventoryItem Item(ulong uid) {return new NpcInventoryItem{Uid=uid,NativePayload="{\"uid\":"+uid+",\"stackCount\":3}"};}
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Starting wallets use permanent origins, deterministic ranges and saved balances",()=>{
                int[] lows={250,50,10,0}, highs={500,150,35,15};
                for(int type=0;type<4;type++) {
                    var seen=new HashSet<int>();
                    for(uint seed=0;seed<2000;seed++) {
                        var s=new WorldStateStore(seed);var p=s.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                        int amount=s.StartingNpcGold(p.Id,t=>type,f=>false);
                        Check.True(amount>=lows[type] && amount<=highs[type]);seen.Add(amount);
                        Check.Equal(amount,s.StartingNpcGold(p.Id,t=>type,f=>false));
                    }
                    Check.True(seen.Contains(lows[type]) && seen.Contains(highs[type]));
                }
                var world=new WorldStateStore(42);
                world.RegisterTraveler(new TravelerPerson{Id="traveler:test",Name="Visitor",HomeSettlement=200,Race=0,Sex=ResidentSex.Male});
                int queried=-1;int gold=world.StartingNpcGold("traveler:test",town=>{queried=town;return 0;},f=>false);
                Check.Equal(200,queried);Check.True(gold>=250 && gold<=500);
                world.InitializeNpcInventory("traveler:test",7,new NpcInventoryItem[0]);
                var restored=new WorldStateStore(42);restored.Restore(world.Snapshot());
                Check.Equal(7,restored.StartingNpcGold("traveler:test",t=>throw new Exception("Must not reroll saved wallet"),f=>false));
            }));
            cases.Add(("Nobility overrides farm wallet; genuine native farm workers get farm funds",()=>{
                var s=new WorldStateStore(5);
                s.GetOrCreate(100,new[]{10},1);
                var noble=s.RegisterNativePerson(100,10,1,"Noble",ResidentSex.Male,0,77,0,0);
                int rich=s.StartingNpcGold(noble.Id,t=>3,f=>f==77);Check.True(rich>=250 && rich<=500);
                var worker=s.RegisterNativePerson(100,11,2,"Worker",ResidentSex.Male,0,0,0,0);
                int poor=s.StartingNpcGold(worker.Id,t=>3,f=>false);Check.True(poor>=0 && poor<=15);
            }));
            cases.Add(("Inbound transfer receiver permits terminal recovery and only its exact executing transfer",()=>{
                var recovery=new NpcActionJob{Id="old",Phase=NpcActionPhase.Completed,ReturningToRoutine=true};
                Check.True(NpcTransferReceiverPolicy.Allows(recovery,null));
                var transfer=new NpcActionJob{Id="transfer",Kind=NpcActionKind.Transfer,Phase=NpcActionPhase.Executing,
                    Target=new NpcActionTarget{PersonId="player",TransferDirection=NpcActionTransferDirection.PlayerToNpc}};
                Check.True(NpcTransferReceiverPolicy.Allows(transfer,null,"transfer"));
                Check.True(!NpcTransferReceiverPolicy.Allows(transfer,null));
                Check.True(!NpcTransferReceiverPolicy.Allows(transfer,null,"other"));
                transfer.Kind=NpcActionKind.Wait;Check.True(!NpcTransferReceiverPolicy.Allows(transfer,null,"transfer"));
            }));
            cases.Add(("Inbound transfer task preparation cannot bypass another task",()=>{
                var task=new NpcTaskPlan{Id="task",Phase=NpcTaskPhase.Ready,CurrentStepIndex=1};
                Check.True(!NpcTransferReceiverPolicy.Allows(null,task));
                Check.True(!NpcTransferReceiverPolicy.Allows(null,task,"unrelated"));
                Check.True(NpcTransferReceiverPolicy.Allows(new NpcActionJob{Id="task:step:0",Phase=NpcActionPhase.Completed,ReturningToRoutine=true},task,"task:step:1"));
                task.Phase=NpcTaskPhase.Running;task.ActiveActionId="task:step:1";
                var active=new NpcActionJob{Id="task:step:1",Kind=NpcActionKind.Transfer,Phase=NpcActionPhase.Executing,
                    Target=new NpcActionTarget{PersonId="player",TransferDirection=NpcActionTransferDirection.PlayerToNpc}};
                Check.True(NpcTransferReceiverPolicy.Allows(active,task,active.Id));
                active.Target.TransferDirection=NpcActionTransferDirection.NpcToCounterparty;
                Check.True(!NpcTransferReceiverPolicy.Allows(active,task,active.Id));
            }));
            cases.Add(("NPC inventory materializes once and preserves removals across branches",()=>{
                string a,b;var s=Create(out a,out b);Check.True(s.ReadNpcInventory(a)==null);Check.Equal(0,s.Snapshot().NpcInventories.Count);
                Check.True(s.InitializeNpcInventory(a,50,new[]{Item(77)}));Check.True(!s.InitializeNpcInventory(a,90,new[]{Item(88)}));
                var save=s.Snapshot();var p=s.ReadNpcInventory(a);p.Items.Clear();Check.Equal(1,s.ReadNpcInventory(a).Items.Count);
                Check.True(s.CommitNpcInventoryTransaction("remove",new[]{new NpcInventoryUpdate{PersonId=a,ExpectedRevision=0,WalletGold=50}}));
                Check.Equal(0,s.ReadNpcInventory(a).Items.Count);s.Restore(save);Check.Equal(77UL,s.ReadNpcInventory(a).Items[0].Uid);
                var old=s.Snapshot();old.Version=27;old.NpcInventories=null;s.Restore(old);Check.Equal(0,s.Snapshot().NpcInventories.Count);
            }));
            cases.Add(("Empty NPC inventory ledger remains present after save restore and rejects refill",()=>{
                string a,b;var store=Create(out a,out b);
                Check.True(!store.HasNpcInventory(a));
                Check.True(store.InitializeNpcInventory(a,0,new NpcInventoryItem[0]));
                Check.True(store.HasNpcInventory(a));
                var saved=store.Snapshot();
                var restored=new WorldStateStore(0);restored.Restore(saved);
                Check.True(restored.HasNpcInventory(a));
                Check.Equal(0,restored.ReadNpcInventory(a).Items.Count);
                Check.True(!restored.InitializeNpcInventory(a,25,new[]{Item(91)}));
                Check.Equal(0,restored.ReadNpcInventory(a).Items.Count);
            }));
            cases.Add(("NPC inventory transactions move exact payload and funds atomically",()=>{
                string a,b;var s=Create(out a,out b);s.InitializeNpcInventory(a,50,new[]{Item(77)});s.InitializeNpcInventory(b,12,new NpcInventoryItem[0]);
                var payload=s.ReadNpcInventory(a).Items[0].NativePayload;
                var changes=new[]{new NpcInventoryUpdate{PersonId=a,ExpectedRevision=0,WalletGold=55},new NpcInventoryUpdate{PersonId=b,ExpectedRevision=0,WalletGold=7,Items=new List<NpcInventoryItem>{Item(77)}}};
                Check.True(s.CommitNpcInventoryTransaction("sale",changes));Check.Equal(55,s.ReadNpcInventory(a).WalletGold);Check.Equal(payload,s.ReadNpcInventory(b).Items[0].NativePayload);
                Check.True(!s.CommitNpcInventoryTransaction("sale",changes));Check.Equal(1L,s.ReadNpcInventory(a).Revision);
                changes[0].ExpectedRevision=1;changes[1].ExpectedRevision=0;changes[0].WalletGold=99;
                Check.True(!s.CommitNpcInventoryTransaction("other",changes));Check.Equal(55,s.ReadNpcInventory(a).WalletGold);
            }));
            cases.Add(("NPC inventory rejects invalid funds UIDs owners and duplicate receipts",()=>{
                string a,b;var s=Create(out a,out b);s.InitializeNpcInventory(a,50,new[]{Item(77)});s.InitializeNpcInventory(b,12,new NpcInventoryItem[0]);
                Check.True(!s.InitializeNpcInventory("unknown",0,new NpcInventoryItem[0]));
                Check.Throws<ArgumentException>(()=>s.InitializeNpcInventory(b,-1,new NpcInventoryItem[0]));
                Check.True(!s.CommitNpcInventoryTransaction("bad",new[]{new NpcInventoryUpdate{PersonId=b,ExpectedRevision=0,WalletGold=1,Items=new List<NpcInventoryItem>{Item(77)}}}));
                Check.True(!s.CommitNpcInventoryTransaction("bad",new[]{new NpcInventoryUpdate{PersonId=b,ExpectedRevision=0,WalletGold=-1}}));
                Check.True(s.TryPayNpcService(a,10,"drink",0));Check.True(!s.TryPayNpcService(a,10,"drink",1));
                Check.True(!s.TryPayNpcService(a,100,"costly",1));Check.Equal(40,s.ReadNpcInventory(a).WalletGold);
            }));
            cases.Add(("NPC inventory restore is detached and rejects corruption before replacing branch",()=>{
                string a,b;var s=Create(out a,out b);s.InitializeNpcInventory(a,50,new[]{Item(77)});s.InitializeNpcInventory(b,12,new NpcInventoryItem[0]);
                var bad=s.Snapshot();bad.NpcInventories[1].Items.Add(Item(77));Check.Throws<ArgumentException>(()=>s.Restore(bad));
                bad=s.Snapshot();bad.NpcInventories[0].PersonId="unknown";Check.Throws<ArgumentException>(()=>s.Restore(bad));
                bad=s.Snapshot();bad.NpcInventories[0].Items[0].NativePayload=new string('x',NpcInventoryLimits.MaximumItemPayloadLength+1);Check.Throws<ArgumentException>(()=>s.Restore(bad));
                Check.Equal(50,s.ReadNpcInventory(a).WalletGold);Check.Equal(1,s.ReadNpcInventory(a).Items.Count);
            }));
            cases.Add(("NPC inventory stale retries stay rejected after the last receipt changes",()=>{
                string a,b;var s=Create(out a,out b);s.InitializeNpcInventory(a,50,new NpcInventoryItem[0]);var earlier=s.Snapshot();
                Check.True(s.TryPayNpcService(a,5,"first",0));Check.True(s.TryPayNpcService(a,4,"second",1));
                Check.True(!s.TryPayNpcService(a,5,"first",0));Check.Equal(41,s.ReadNpcInventory(a).WalletGold);
                s.Restore(earlier);Check.True(s.TryPayNpcService(a,2,"branch",0));Check.Equal(48,s.ReadNpcInventory(a).WalletGold);
            }));
            cases.Add(("NPC inventory restore rejects missing lists duplicate owners and invalid receipts",()=>{
                string a,b;var s=Create(out a,out b);s.InitializeNpcInventory(a,50,new[]{Item(77)});
                var broken=s.Snapshot();broken.NpcInventories=null;Check.Throws<ArgumentException>(()=>s.Restore(broken));
                broken=s.Snapshot();broken.NpcInventories.Add(broken.NpcInventories[0]);Check.Throws<ArgumentException>(()=>s.Restore(broken));
                broken=s.Snapshot();broken.NpcInventories[0].Revision=1;Check.Throws<ArgumentException>(()=>s.Restore(broken));
                broken=s.Snapshot();broken.NpcInventories[0].Items=null;Check.Throws<ArgumentException>(()=>s.Restore(broken));
                Check.Equal(50,s.ReadNpcInventory(a).WalletGold);
            }));
            cases.Add(("Exact item receipt preflight enforces item count UID and serialized payload limits",new Action(()=>{
                var inventory=new List<NpcInventoryItem>();
                for(ulong uid=1;uid<NpcInventoryLimits.MaximumItems;uid++)inventory.Add(new NpcInventoryItem{Uid=uid,NativePayload="{}"});
                var incoming=new NpcInventoryItem{Uid=9000,NativePayload="{}"};
                Check.True(NpcInventoryLimits.CanAppendExactItem(inventory,incoming));
                inventory.Add(new NpcInventoryItem{Uid=8999,NativePayload="{}"});
                Check.True(!NpcInventoryLimits.CanAppendExactItem(inventory,incoming));
                inventory.RemoveAt(inventory.Count-1);
                Check.True(!NpcInventoryLimits.CanAppendExactItem(inventory,new NpcInventoryItem{Uid=1,NativePayload="{}"}));
                var payloadNearLimit=new List<NpcInventoryItem>();ulong payloadUid=10000;
                for(int i=0;i<31;i++)payloadNearLimit.Add(new NpcInventoryItem{Uid=payloadUid++,NativePayload=new string('x',NpcInventoryLimits.MaximumItemPayloadLength)});
                payloadNearLimit.Add(new NpcInventoryItem{Uid=payloadUid++,NativePayload=new string('x',NpcInventoryLimits.MaximumItemPayloadLength-100)});
                Check.True(NpcInventoryLimits.CanAppendExactItem(payloadNearLimit,new NpcInventoryItem{Uid=3,NativePayload=new string('y',100)}));
                Check.True(!NpcInventoryLimits.CanAppendExactItem(payloadNearLimit,new NpcInventoryItem{Uid=3,NativePayload=new string('y',101)}));
                Check.True(!NpcInventoryLimits.CanAppendExactItem(new List<NpcInventoryItem>(),new NpcInventoryItem{Uid=3,NativePayload=new string('x',NpcInventoryLimits.MaximumItemPayloadLength+1)}));
                Check.True(!NpcInventoryLimits.CanAppendExactItem(new List<NpcInventoryItem>(),new NpcInventoryItem{Uid=0,NativePayload="{}"}));
            })));
        }
    }
}
