using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class CorpsePropertyKeyTests
    {
        private static WorldStateStore Body(out CivilianIncident alarm, out string witness)
        {
            var store = new WorldStateStore(27);
            var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
            var victim = town.Households[0].Residents[0];
            witness = town.Households[1].Residents[0].Id;
            alarm = store.RecordViolence(1, victim.Id, 100);
            store.LocateViolence(alarm.Id, -1, 1, 0, 2);
            Population.MarkDead(town, victim.Id);
            store.ConfirmCorpse(alarm.Id, 100, -1, 3, 0, 4);
            return store;
        }

        public static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Legacy key availability rechecks issuer without resetting the saved ledger", () => {
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.InitializeCorpsePropertyKeyClaims(alarm.Id,new[]{
                    new CorpsePropertyKeyClaim {SettlementId=1,BuildingKey=7},
                    new CorpsePropertyKeyClaim {SettlementId=1,BuildingKey=4}});
                var keys=store.GetAvailableCorpsePropertyKeyClaims(alarm.Id,(person,town,building)=>person==alarm.VictimId && building==7);
                Check.Equal(1,keys.Count);Check.Equal(7,keys[0].BuildingKey);
                Check.Equal(2,store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
            }));
            cases.Add(("Temporary inn property excluded without losing permanent home key", () => {
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{8},1);
                town.Households[0].BedCapacity=4;
                var person=store.RegisterNativePerson(1,4,11,"Inn Lodger",ResidentSex.Female,1,41,182,1);
                Check.True(store.AssignNativeBed(person.Id,new HomeProfile(4,0),new[]{new HomeProfile(8,4)}));
                var alarm=store.RecordViolence(1,person.Id,100);store.LocateViolence(alarm.Id,4,0,0,0);person.IsAlive=false;
                store.ConfirmCorpse(alarm.Id,101,4,0,0,0);
                store.InitializeCorpseHomeKey(alarm.Id,4,(settlement,building)=>building!=4);
                var keys=store.GetAvailableCorpsePropertyKeyClaims(alarm.Id);
                Check.Equal(1,keys.Count);Check.Equal(8,keys[0].BuildingKey);
            }));
            cases.Add(("Shop tenant retains bed-assigned access without keeper confirmation", () => {
                var store=new WorldStateStore(42);store.GetOrCreate(1,new[]{8},1);
                var person=store.RegisterNativePerson(1,4,11,"Shop Tenant",ResidentSex.Female,1,0,182,1);
                Check.True(store.AssignNativeBed(person.Id,new HomeProfile(4,1),new HomeProfile[0]));
                var alarm=store.RecordViolence(1,person.Id,100);store.LocateViolence(alarm.Id,4,0,0,0);person.IsAlive=false;
                store.ConfirmCorpse(alarm.Id,101,4,0,0,0);
                store.InitializeCorpseHomeKey(alarm.Id,null,(settlement,building)=>building!=99);
                var keys=store.GetAvailableCorpsePropertyKeyClaims(alarm.Id);
                Check.Equal(1,keys.Count);Check.Equal(4,keys[0].BuildingKey);
            }));
            cases.Add(("Confirmed off-site keeper receives home and business keys but rejects another workplace", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 8 }, 1);
                town.Households[0].BedCapacity = 4;
                var person = store.RegisterNativePerson(1, 4, 11, "Keeper Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                Check.True(store.AssignNativeBed(person.Id, new HomeProfile(4, 0), new[] { new HomeProfile(8, 4) }));
                var alarm = store.RecordViolence(1, person.Id, 100);
                store.LocateViolence(alarm.Id, 4, 1, 2, 3); person.IsAlive = false;
                store.ConfirmCorpse(alarm.Id, 101, 4, 1, 2, 3);
                Check.Throws<ArgumentException>(() => store.InitializeCorpseHomeKey(alarm.Id, 99));
                Check.True(store.InitializeCorpseHomeKey(alarm.Id, 4));
                var keys = store.GetAvailableCorpsePropertyKeyClaims(alarm.Id);
                Check.Equal(2, keys.Count);
                Check.True(keys.Exists(key => key.BuildingKey == 8));
                Check.True(keys.Exists(key => key.BuildingKey == 4));
                store.MarkCorpsePropertyKeyTaken(alarm.Id, 1, 4);
                var restored = new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.True(!restored.InitializeCorpseHomeKey(alarm.Id, 4));
                Check.Equal(1, restored.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
            }));
            cases.Add(("Corpse home key uses household ownership rather than death location and remains taken", () => {
                CivilianIncident alarm; string witness;
                var store = Body(out alarm, out witness);
                Check.True(store.InitializeCorpseHomeKey(alarm.Id));
                var keys = store.GetAvailableCorpsePropertyKeyClaims(alarm.Id);
                Check.Equal(1, keys.Count); Check.Equal(1, keys[0].SettlementId); Check.Equal(7, keys[0].BuildingKey);
                store.MarkCorpsePropertyKeyTaken(alarm.Id, 1, 7);
                var restored = new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.True(!restored.InitializeCorpseHomeKey(alarm.Id));
                Check.Equal(0, restored.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
            }));
            cases.Add(("Bedless native corpse does not receive the workplace key as an assumed home", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var person = store.RegisterNativePerson(1, 4, 11, "Visitor", ResidentSex.Male, 1, 41, 182, 1);
                var alarm = store.RecordViolence(1, person.Id, 100);
                store.LocateViolence(alarm.Id, 4, 1, 2, 3); person.IsAlive = false;
                store.ConfirmCorpse(alarm.Id, 101, 4, 1, 2, 3);
                Check.True(store.InitializeCorpseHomeKey(alarm.Id));
                Check.Equal(0, store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
            }));
            cases.Add(("Native household head receives one home key despite two identity representations", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 8 }, 1);
                var person = store.RegisterNativePerson(1, 4, 11, "Keeper Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                store.AssignNativeBed(person.Id, new HomeProfile(4, 2), new HomeProfile[0]);
                store.CreateKeeperHousehold(person.Id, new HomeProfile(4, 2));
                var alarm = store.RecordViolence(1, person.Id, 100);
                store.LocateViolence(alarm.Id, -1, 1, 2, 3);
                Population.MarkDead(town, person.Id);
                store.ConfirmCorpse(alarm.Id, 101, -1, 1, 2, 3);
                store.InitializeCorpseHomeKey(alarm.Id);
                var keys = store.GetAvailableCorpsePropertyKeyClaims(alarm.Id);
                Check.Equal(1, keys.Count); Check.Equal(4, keys[0].BuildingKey);
            }));
            cases.Add(("Corpse property keys initialize once and preserve taken state through reload", () => {
                CivilianIncident alarm;
                string witness;
                var store = Body(out alarm, out witness);
                var claims = new[] {
                    new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 7 },
                    new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 11 },
                    new CorpsePropertyKeyClaim { SettlementId = 2, BuildingKey = 19 }
                };
                Check.True(store.InitializeCorpsePropertyKeyClaims(alarm.Id, claims));
                Check.Equal(3, store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
                Check.True(store.MarkCorpsePropertyKeyTaken(alarm.Id, 1, 11));
                Check.True(!store.MarkCorpsePropertyKeyTaken(alarm.Id, 1, 11));

                var restored = new WorldStateStore(1);
                restored.Restore(store.Snapshot());
                Check.True(!restored.InitializeCorpsePropertyKeyClaims(alarm.Id, claims));
                var available = restored.GetAvailableCorpsePropertyKeyClaims(alarm.Id);
                Check.Equal(2, available.Count);
                Check.Equal(1, available[0].SettlementId); Check.Equal(7, available[0].BuildingKey);
                Check.Equal(2, available[1].SettlementId); Check.Equal(19, available[1].BuildingKey);
                Check.Throws<ArgumentException>(() => restored.InitializeCorpsePropertyKeyClaims(alarm.Id,
                    new[] { new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 99 } }));
                Check.Equal(2, restored.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
            }));

            cases.Add(("Corpse property key claims reject invalid or duplicate property identities", () => {
                CivilianIncident alarm;
                string witness;
                var store = Body(out alarm, out witness);
                Check.Throws<ArgumentException>(() => store.InitializeCorpsePropertyKeyClaims(alarm.Id,
                    new[] { new CorpsePropertyKeyClaim { SettlementId = -1, BuildingKey = 4 } }));
                Check.Throws<ArgumentException>(() => store.InitializeCorpsePropertyKeyClaims(alarm.Id,
                    new[] { new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = -1 } }));
                Check.Throws<ArgumentException>(() => store.InitializeCorpsePropertyKeyClaims(alarm.Id,
                    new[] { new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 4, Taken = true } }));
                Check.Throws<ArgumentException>(() => store.InitializeCorpsePropertyKeyClaims(alarm.Id,
                    new[] {
                        new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 4 },
                        new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 4 }
                    }));
                Check.Equal(0, store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
            }));

            cases.Add(("Legacy corpses have no claims and cleared corpses cannot receive claims", () => {
                CivilianIncident alarm;
                string witness;
                var store = Body(out alarm, out witness);
                Check.Equal(0, store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
                var legacy = store.Snapshot();
                legacy.AlarmIncidents[0].Corpse.PropertyKeyClaimsInitialized = false;
                legacy.AlarmIncidents[0].Corpse.PropertyKeyClaims = null;
                var restored = new WorldStateStore(1);
                restored.Restore(legacy);
                Check.Equal(0, restored.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
                Check.True(restored.InitializeCorpsePropertyKeyClaims(alarm.Id,
                    new[] { new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 7 } }));
                var corrupt = restored.Snapshot();
                corrupt.AlarmIncidents[0].Corpse.PropertyKeyClaims.Add(
                    new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 7 });
                Check.Throws<ArgumentException>(() => restored.Restore(corrupt));
                Check.Equal(1, restored.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 150);
                store.ObserveOutdoorCorpse(alarm.Id, false, 151);
                Check.True(alarm.Corpse.Cleared);
                Check.Throws<ArgumentException>(() => store.InitializeCorpsePropertyKeyClaims(alarm.Id,
                    new[] { new CorpsePropertyKeyClaim { SettlementId = 1, BuildingKey = 7 } }));
                Check.Equal(0, store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count);
            }));
        }
    }
}
