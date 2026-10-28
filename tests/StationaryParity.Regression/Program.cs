using System;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

static class Program
{
    static int checks;
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;Console.WriteLine("PASS "+message);}
    static int Main()
    {
        try
        {
            var death=typeof(WorldStateStore).GetMethod("RecordNativeStationaryDeath");
            Need(death!=null,"stationary deaths enter the existing canonical corpse lifecycle");
            var early=new WorldStateStore(76);early.EnsureNativeLocationIdentity(7);
            Need(early.CurrentPopulation(7).IsPartial && early.GetOrCreateFamilies(7,new[]{new HomeProfile(70,2,100)}).Households.Count==1,
                "early native identity registration does not suppress actual later household generation");
            var store=new WorldStateStore(77);store.GetOrCreate(1,new int[0],1);
            foreach(string layout in new[]{"scene:building:42:quest:123:person", "scene:dungeon:71:block:2:4:seed:99"})
            {
                var person=store.RegisterNativeLayoutPerson(1,layout,41,"Actual Stationary Person",ResidentSex.Female,0,0,182,1);
                ulong uid=(ulong)(100+checks);store.InitializeNpcInventory(person.Id,19,new[]{new NpcInventoryItem{Uid=uid,NativePayload="native-held-item"}});
                var incident=(CivilianIncident)death.Invoke(store,new object[]{person.Id,100d,42,1d,2d,3d,true});
                Need(!person.IsAlive && incident.Corpse!=null && incident.PlayerAttributed,"actual native death records deceased identity and positioned corpse");
                Need(!store.TryRegisterNativeUnwitnessedScene(incident.Id),"death outside a known owned or heard room does not invent an investigation scene");
                Need(store.NpcInventoryItemOwner(uid)==person.Id && store.ReadNpcInventory(person.Id).WalletGold==19,"death retains exact actual item and wallet custody");
                var again=(CivilianIncident)death.Invoke(store,new object[]{person.Id,110d,42,1d,2d,3d,true});
                Need(again.Id==incident.Id && store.Snapshot().AlarmIncidents.FindAll(value=>value.VictimId==person.Id).Count==1,"repeated native death callback creates no duplicate corpse");
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Need(!restored.NativePersonInSettlement(1,person.Id).IsAlive && restored.NpcInventoryItemOwner(uid)==person.Id && restored.ReadNpcInventory(person.Id).Items.Count==1,"current save restore preserves native death and one exact inventory");
                var looted=store.ReadNpcInventory(person.Id);
                Need(store.CommitNpcInventoryTransaction("loot:"+person.Id,new[]{new NpcInventoryUpdate{PersonId=person.Id,ExpectedRevision=looted.Revision,WalletGold=0,Items=new System.Collections.Generic.List<NpcInventoryItem>()}}),"actual corpse removal updates its existing inventory owner");
                death.Invoke(store,new object[]{person.Id,120d,42,1d,2d,3d,true});restored.Restore(store.Snapshot());
                Need(restored.ReadNpcInventory(person.Id).Items.Count==0 && restored.ReadNpcInventory(person.Id).WalletGold==0 && restored.NpcInventoryItemOwner(uid)==null,"looted corpse stays empty after repeated death notification and current save restore");
            }
            Console.WriteLine(checks+" checks passed");return RuntimeChecks.Run();
        }
        catch(Exception error){Console.Error.WriteLine("FAIL "+(error.InnerException??error).Message);return 1;}
    }
}
