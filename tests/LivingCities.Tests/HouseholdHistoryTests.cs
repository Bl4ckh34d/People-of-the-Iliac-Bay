using System;
using System.Collections.Generic;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class HouseholdHistoryTests
    {
        private static readonly JsonSerializerOptions Json=new JsonSerializerOptions { IncludeFields=true };
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Historical families roundtrip without replacing current occupants", Roundtrip));
            cases.Add(("Historical household corruption is rejected transactionally", Corruption));
            cases.Add(("Schema 19 migrates without inventing historical families", Legacy));
            cases.Add(("Household archive requires confirmed extinction and keeps original identity", Archive));
        }
        private static WorldState Fixture()
        {
            var store=new WorldStateStore(42);
            var home=store.GetOrCreateFamilies(3,new[]{new HomeProfile(17,3,220)}).Households[0];
            var victim=home.Residents[0];
            store.CommitQuestBindings(store.PrepareQuestBindings(new[]{new QuestResidentBinding {
                QuestUid=11,QuestName="test",PersonSymbol="recipient",SettlementId=3,HomeKey=17,
                ResidentId=victim.Id,PersonName="Old Resident",HomeName="Old Residence",RuleId="test" }}));
            store.RecordViolence(3,victim.Id,100);
            foreach(var person in home.Residents) person.IsAlive=false;
            var world=store.Snapshot();
            world.HouseholdHistory.Add(new RetiredHousehold { SettlementId=3,RetiredAt=200,
                Household=world.Settlements[0].Households[0] });
            world.Settlements[0].Households[0]=HouseholdGeneration.Create(42,3,new HomeProfile(17,3,220),1);
            return world;
        }
        private static void Roundtrip()
        {
            var world=Fixture();
            var restored=new WorldStateStore(1);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(world,Json),Json));
            var saved=restored.Snapshot();
            Check.Equal(21,saved.Version);
            Check.Equal(1,saved.HouseholdHistory.Count);
            Check.Equal(1,saved.Settlements[0].Households.Count);
            Check.Equal(1,saved.Settlements[0].Households[0].Generation);
            Check.Equal(saved.HouseholdHistory[0].Household.Residents[0].Id,saved.QuestBindings[0].ResidentId);
            Check.Equal(saved.QuestBindings[0].ResidentId,saved.AlarmIncidents[0].VictimId);
            Check.Equal(saved.HouseholdHistory[0].Household.Residents[0].Id,
                restored.ResolveResident(3,saved.QuestBindings[0].ResidentId).Id);
            Check.Equal(null,restored.ResolveResident(999,saved.QuestBindings[0].ResidentId));
            saved.HouseholdHistory[0].Household.Residents[0].FullName="Mutated";
            Check.True(restored.Snapshot().HouseholdHistory[0].Household.Residents[0].FullName!="Mutated","Historical snapshot leaked references.");
        }
        private static void Corruption()
        {
            for(int failure=0;failure<8;failure++)
            {
                var world=Fixture();
                if(failure==0) world.HouseholdHistory[0].Household.Residents[0].IsAlive=true;
                if(failure==1) world.HouseholdHistory.Add(world.HouseholdHistory[0]);
                if(failure==2) world.HouseholdHistory[0].SettlementId=999;
                if(failure==3) world.HouseholdHistory[0].RetiredAt=double.NaN;
                if(failure==4) world.HouseholdHistory[0].Household.Generation=1;
                if(failure==5) world.Settlements[0].Households[0].Generation=2;
                if(failure==6) world.HouseholdHistory=null;
                if(failure==7) world.HouseholdHistory[0].Household.Residents[0].Id=world.Settlements[0].Households[0].Residents[0].Id;
                var store=new WorldStateStore(22);
                string before=JsonSerializer.Serialize(store.Snapshot(),Json);
                Check.Throws<ArgumentException>(()=>store.Restore(world));
                Check.Equal(before,JsonSerializer.Serialize(store.Snapshot(),Json));
            }
        }
        private static void Legacy()
        {
            var store=new WorldStateStore(42);
            store.GetOrCreateFamilies(3,new[]{new HomeProfile(17,3,220)});
            var old=store.Snapshot(); old.Version=19; old.HouseholdHistory=null;
            string id=old.Settlements[0].Households[0].Residents[0].Id;
            store.Restore(old);
            Check.Equal(0,store.Snapshot().HouseholdHistory.Count);
            Check.Equal(0,store.Snapshot().Settlements[0].Households[0].Generation);
            Check.Equal(id,store.Snapshot().Settlements[0].Households[0].Residents[0].Id);
        }
        private static void Archive()
        {
            var store=new WorldStateStore(42);
            var current=store.GetOrCreateFamilies(3,new[]{new HomeProfile(17,3,220)}).Households[0];
            string oldId=current.Residents[0].Id;
            var replacement=HouseholdGeneration.Create(42,3,new HomeProfile(17,3,220),1);
            Check.Throws<ArgumentException>(()=>store.ArchiveHouseholdAndInstall(3,17,0,200,replacement));
            Check.Equal(0,store.Snapshot().HouseholdHistory.Count);
            foreach(var resident in current.Residents) resident.IsAlive=false;
            store.ArchiveHouseholdAndInstall(3,17,0,200,replacement);
            var saved=store.Snapshot();
            Check.Equal(1,saved.HouseholdHistory.Count);
            Check.Equal(1,saved.Settlements[0].Households[0].Generation);
            Check.Equal(oldId,store.ResolveResident(oldId).Id);
        }
    }
}
