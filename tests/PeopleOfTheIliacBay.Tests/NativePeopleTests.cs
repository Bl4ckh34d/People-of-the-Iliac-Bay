using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using FullSerializer;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class NativePeopleTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Native castle layout identity is known without an invented work site or bed and survives restore", () => {
                var register=typeof(WorldStateStore).GetMethod("RegisterNativeLayoutPerson");
                Check.True(register!=null,"Missing native layout registration.");
                var store=new WorldStateStore(42);store.GetOrCreate(1,new[]{8},1);
                var first=(NativePerson)register.Invoke(store,new object[] {1,"castle:7:0:0:123",11,"Court Noble",ResidentSex.Male,1,595,182,1});
                var second=(NativePerson)register.Invoke(store,new object[] {1,"castle:7:1:0:123",11,"Other Noble",ResidentSex.Male,1,595,182,1});
                Check.True(first.Id!=second.Id,"Repeated native blocks merged two people.");
                Check.Equal(-1,first.WorkKey);Check.Equal(-1,first.HomeKey);Check.Equal(-1,first.BedSlot);
                Check.True(store.IsKnownNpcObservedSpeechSpeaker(first.Id));
                var serializer=new fsSerializer();fsData data;serializer.TrySerialize(store.Snapshot(),out data).AssertSuccess();
                WorldState saved=null;serializer.TryDeserialize(data,ref saved).AssertSuccess();
                var restored=new WorldStateStore(0);restored.Restore(saved);
                Check.True(restored.IsKnownNpcObservedSpeechSpeaker(first.Id));
                Check.Equal(first.Id,restored.NativePersonInSettlement(1,first.Id).Id);
                var again=(NativePerson)register.Invoke(restored,new object[] {1,"castle:7:0:0:123",11,"Court Noble",ResidentSex.Male,1,595,182,1});
                Check.Equal(first.Id,again.Id);Check.Equal(2,restored.NativePeopleFor(1).Count);
                Check.Equal("npc:1:4:12",restored.RegisterNativePerson(1,4,12,"Keeper",ResidentSex.Female,1,41,182,1).Id);
                var unsurveyed=new WorldStateStore(42);
                var early=(NativePerson)register.Invoke(unsurveyed,new object[] {1,"castle:7:0:0:123",11,"Court Noble",ResidentSex.Male,1,595,182,1});
                Check.True(!unsurveyed.HasSettlement(1) && unsurveyed.IsKnownNpcObservedSpeechSpeaker(early.Id));
                var earlyReload=new WorldStateStore(0);earlyReload.Restore(unsurveyed.Snapshot());
                Check.True(earlyReload.IsKnownNpcObservedSpeechSpeaker(early.Id));
                var wrongSchema=unsurveyed.Snapshot();wrongSchema.Version=77;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(wrongSchema));
            }));
            cases.Add(("Native person ID lookup tracks registration, death, settlement and restored save branch", () => {
                var store=new WorldStateStore(42);store.GetOrCreate(1,new[]{8},1);store.GetOrCreate(2,new[]{9},1);
                Check.True(store.NativePersonInSettlement(1,"npc:1:4:11")==null);
                var keeper=store.RegisterNativePerson(1,4,11,"Keeper Kinghart",ResidentSex.Male,1,41,182,1);
                Check.True(ReferenceEquals(keeper,store.NativePersonInSettlement(1,keeper.Id)));
                Check.True(store.NativePersonInSettlement(2,keeper.Id)==null);
                keeper.IsAlive=false;
                Check.True(!store.NativePersonInSettlement(1,keeper.Id).IsAlive);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                var dead=restored.NativePersonInSettlement(1,keeper.Id);
                Check.True(dead!=null && !dead.IsAlive);
                var later=restored.RegisterNativePerson(1,5,12,"Later Keeper",ResidentSex.Female,1,0,182,2);
                Check.True(ReferenceEquals(later,restored.NativePersonInSettlement(1,later.Id)));
            }));
            cases.Add(("Native household head death retains family witness, corpse and identity after reload", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 8 }, 1);
                var keeper = store.RegisterNativePerson(1, 4, 11, "Keeper Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                store.AssignNativeBed(keeper.Id, new HomeProfile(4, 2), new HomeProfile[0]);
                var home = store.CreateKeeperHousehold(keeper.Id, new HomeProfile(4, 2));
                var alarm = store.RecordViolence(1, keeper.Id, 100);
                store.LocateViolence(alarm.Id, 4, 1, 2, 3);
                store.Witness(alarm.Id, home.Residents[1].Id, 100);
                Check.True(Population.MarkDead(town, keeper.Id));
                store.ConfirmCorpse(alarm.Id, 101, 4, 2, 2, 3);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(!restored.NativePeopleFor(1)[0].IsAlive);
                Check.True(!restored.Snapshot().Settlements[0].Households.Find(h => h.HeadId == keeper.Id).Residents[0].IsAlive);
                Check.Equal(home.Residents[1].Id, restored.Snapshot().AlarmIncidents[0].People[0].ResidentId);
                Check.Equal(keeper.Id, restored.ObserveCorpses(1, 4, 200)[0].VictimId);
                Check.Equal(alarm.Id, restored.RecordViolence(1, keeper.Id, 200).Id);
                var corrupt = store.Snapshot(); corrupt.NativePeople[0].IsAlive = true;
                Check.Throws<ArgumentException>(() => restored.Restore(corrupt));
                Check.True(!restored.NativePeopleFor(1)[0].IsAlive);
            }));
            cases.Add(("Unlinked bedless keeper death uses work-site discovery and rejects missing identity", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var keeper = store.RegisterNativePerson(1, 4, 11, "Keeper Kinghart", ResidentSex.Female, 1, 41, 182, 1);
                var alarm = store.RecordViolence(1, keeper.Id, 100);
                store.LocateViolence(alarm.Id, 4, 1, 2, 3); keeper.IsAlive = false;
                store.ConfirmCorpse(alarm.Id, 101, 4, 1, 2, 3);
                store.RegisterUnwitnessedScene(alarm.Id);
                Check.Equal(0, store.ObserveCrimeScenes(1, 4, 200).Count);
                Check.Equal(1, store.ObserveCrimeScenes(1, -1, 201).Count);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal(1, restored.ObserveCrimeScenes(1, -1, 202).Count);
                Check.Equal(1, restored.ObserveCorpses(1, 4, 90000).Count);
                Check.True(!restored.NativePeopleFor(1)[0].IsAlive);
                var corrupt = store.Snapshot(); corrupt.NativePeople.Clear();
                Check.Throws<ArgumentException>(() => restored.Restore(corrupt));
                Check.Equal(keeper.Id, restored.Snapshot().AlarmIncidents[0].VictimId);
                Check.Throws<ArgumentException>(() => restored.RecordViolence(1, "npc:unknown", 203));
            }));
            cases.Add(("Native keeper heads a bed-limited family with saved surname and parent links", () => {
                foreach (var sex in new[] { ResidentSex.Male, ResidentSex.Female })
                {
                    var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                    var keeper = store.RegisterNativePerson(1, 4, 11, "Peryrick Kinghart", sex, 2, 41, 182, 1);
                    var profile = new HomeProfile(4, 5, 300);
                    store.AssignNativeBed(keeper.Id, profile, new HomeProfile[0]);
                    var home = store.CreateKeeperHousehold(keeper.Id, profile);
                    Check.Equal(4, home.Residents.Count); Check.Equal(keeper.Id, home.HeadId); Check.Equal("Kinghart", home.Surname);
                    var head = home.Residents.Find(r => r.Id == keeper.Id);
                    Check.Equal("Peryrick Kinghart", head.FullName); Check.Equal(sex, head.Sex); Check.Equal(2, head.Race);
                    Check.Equal(1, home.Residents.FindAll(r => r.Id == keeper.Id).Count);
                    Check.Equal(2, home.Residents.FindAll(r => r.AgeGroup == ResidentAgeGroup.Child).Count);
                    foreach (var child in home.Residents.FindAll(r => r.AgeGroup == ResidentAgeGroup.Child))
                        Check.Equal(keeper.Id, sex == ResidentSex.Male ? child.FatherId : child.MotherId);
                    foreach (var member in home.Residents) Check.Equal(2, member.Race);
                    var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                    var saved = restored.CreateKeeperHousehold(keeper.Id, new HomeProfile(4, 1, 10));
                    Check.Equal(4, saved.Residents.Count); Check.Equal(head.PartnerId, saved.Residents.Find(r => r.Id == keeper.Id).PartnerId);
                    Check.Equal(keeper.Id, HomeBeds.Assign(saved, new[] { 10, 20, 30, 40, 50 })[0].ResidentId);
                    Check.True(Population.MarkDead(restored.GetOrCreate(1, new[] { 8 }), keeper.Id));
                    Check.True(!restored.NativePeopleFor(1)[0].IsAlive);
                    var dead = new WorldStateStore(0); dead.Restore(restored.Snapshot());
                    Check.True(!dead.NativePeopleFor(1)[0].IsAlive);
                    Check.True(!dead.GetOrCreate(1, new[] { 8 }).Households.Find(h => h.HeadId == keeper.Id).Residents[0].IsAlive);
                }
            }));
            cases.Add(("Two-bed keeper households have one opposite-sex partner and retain legacy identities", () => {
                foreach (var sex in new[] { ResidentSex.Male, ResidentSex.Female })
                {
                    var store = new WorldStateStore(42); var old = store.GetOrCreate(1, new[] { 8 }, 1).Households[0].Residents[0];
                    string oldId = old.Id;
                    var keeper = store.RegisterNativePerson(1, 4, 11, "Keeper Kinghart", sex, 1, 41, 182, 1);
                    store.AssignNativeBed(keeper.Id, new HomeProfile(4, 2), new HomeProfile[0]);
                    var home = store.CreateKeeperHousehold(keeper.Id, new HomeProfile(4, 2));
                    Check.Equal(2, home.Residents.Count);
                    Check.True(home.Residents.Find(r => r.Id != keeper.Id).Sex != sex);
                    Check.Equal(keeper.Id, home.Residents.Find(r => r.Id != keeper.Id).PartnerId);
                    Check.Equal(oldId, store.Snapshot().Settlements[0].Households[0].Residents[0].Id);
                }
            }));
            cases.Add(("One-bed keeper household has only native head and cannot overwrite another saved family", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var keeper = store.RegisterNativePerson(1, 4, 11, "Keeper Kinghart", ResidentSex.Female, 1, 41, 182, 1);
                store.AssignNativeBed(keeper.Id, new HomeProfile(4, 1), new HomeProfile[0]);
                var home = store.CreateKeeperHousehold(keeper.Id, new HomeProfile(4, 1));
                Check.Equal(1, home.Residents.Count); Check.Equal(keeper.Id, home.Residents[0].Id);
                Check.True(home.Residents[0].PartnerId == null);
                Check.Equal(0, keeper.BedSlot); Check.Equal(1, store.NativePeopleFor(1).Count);
                var corrupt = store.Snapshot(); corrupt.Settlements[0].Households.Find(h => h.BuildingKey == 4).HeadId = "missing";
                Check.Throws<ArgumentException>(() => store.Restore(corrupt));
                Check.Equal(keeper.Id, store.Snapshot().Settlements[0].Households.Find(h => h.BuildingKey == 4).HeadId);
            }));
            cases.Add(("Keeper family uses spare beds while preserving saved on-site staff reservations", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var keeper = store.RegisterNativePerson(1, 4, 1, "Keeper Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                var employee = store.RegisterNativePerson(1, 4, 2, "Employee Coppercroft", ResidentSex.Female, 1, 41, 182, 2);
                var profile = new HomeProfile(4, 3, 300);
                store.AssignNativeBed(keeper.Id, profile, new HomeProfile[0]);
                store.AssignNativeBed(employee.Id, profile, new HomeProfile[0]);
                Check.Equal(1, employee.BedSlot);
                var home = store.CreateKeeperHousehold(keeper.Id, profile);
                Check.True(home != null); Check.Equal(2, home.Residents.Count);
                var claims = HomeBeds.Assign(home, new[] { 10, 20, 30 });
                Check.Equal(10, claims.Find(c => c.ResidentId == keeper.Id).BedRecordIndex);
                Check.Equal(30, claims.Find(c => c.ResidentId != keeper.Id).BedRecordIndex);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal(1, restored.NativePeopleFor(1).Find(p => p.Id == employee.Id).BedSlot);
                Check.True(restored.CreateKeeperHousehold(employee.Id, profile) == null);
                var third = restored.RegisterNativePerson(1, 4, 3, "Third Employee", ResidentSex.Male, 1, 41, 182, 3);
                Check.True(!restored.AssignNativeBed(third.Id, profile, new HomeProfile[0]));
                Check.Equal(-1, restored.Snapshot().NativePeople.Find(p => p.Id == third.Id).HomeKey);
            }));
            cases.Add(("Dead unlinked keeper does not create a new living family", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var keeper = store.RegisterNativePerson(1, 4, 1, "Keeper Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                var profile = new HomeProfile(4, 3);
                store.AssignNativeBed(keeper.Id, profile, new HomeProfile[0]); keeper.IsAlive = false;
                Check.True(store.CreateKeeperHousehold(keeper.Id, profile) == null);
                Check.Equal(1, store.Snapshot().Settlements[0].Households.Count);
            }));
            cases.Add(("Native head cannot claim a second bed through sparse household validation", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var keeper = store.RegisterNativePerson(1, 4, 1, "Keeper Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                store.AssignNativeBed(keeper.Id, new HomeProfile(4, 1), new HomeProfile[0]);
                store.CreateKeeperHousehold(keeper.Id, new HomeProfile(4, 1));
                var corrupt = store.Snapshot(); corrupt.NativePeople[0].BedSlot = 1; corrupt.NativePeople[0].BedCapacity = 2;
                corrupt.Settlements[0].Households.Find(h => h.BuildingKey == 4).BedCapacity = 2;
                Check.Throws<ArgumentException>(() => store.Restore(corrupt));
                Check.Equal(0, store.NativePeopleFor(1)[0].BedSlot);
            }));
            cases.Add(("Native keeper witnesses share bounded alarms, escape timing and transactional persistence", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 8, 9 }, 3);
                var victim = town.Households[0].Residents[0];
                var keeper = store.RegisterNativePerson(1, 4, 1, "Keeper", ResidentSex.Female, 1, 41, 182, 1);
                var alarm = store.RecordViolence(1, victim.Id, 100);
                store.LocateViolence(alarm.Id, 4, 0, 0, 0);
                var witness = store.Witness(alarm.Id, keeper.Id, 100);
                Check.True(witness.DirectWitness); Check.Equal(CivilianReaction.Flee, witness.Reaction);
                witness.HasPosition = true; witness.BuildingKey = 4;
                witness.Escape = CivilianEscape.Create(witness, 100,
                    new List<GuardPoint3> { new GuardPoint3 { X = 0 }, new GuardPoint3 { X = 3 } },
                    new List<SavedRoutePoint> { new SavedRoutePoint { X = 0 }, new SavedRoutePoint { X = 10 } }, 3.5);
                Check.True(store.Warn(alarm.Id, keeper.Id, town.Households[1].Residents[0].Id, 101));
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                var saved = restored.ActiveAlarm(keeper.Id, 102).People.Find(p => p.ResidentId == keeper.Id);
                Check.Equal(alarm.Id, restored.NativePeopleFor(1)[0].ResponseIncidentId);
                Check.True(!restored.ClearNativeResponse(keeper.Id, 102));
                Check.Equal(1, saved.Warned.Count); Check.Equal(3.5, saved.Escape.Speed);
                Check.Equal(0, restored.AdvanceEscapes(1, saved.Escape.ReportAt - 1).Count);
                Check.Equal(1, restored.AdvanceEscapes(1, saved.Escape.ReportAt).Count);
                var corrupt = store.Snapshot(); corrupt.NativePeople.Clear();
                Check.Throws<ArgumentException>(() => restored.Restore(corrupt));
                Check.True(restored.ActiveAlarm(keeper.Id, 102) != null);
                Check.True(restored.ActiveAlarm(keeper.Id, CivilianAlarms.ActiveUntil(alarm)) == null);
                Check.True(restored.ClearNativeResponse(keeper.Id, CivilianAlarms.ActiveUntil(alarm)));
                Check.True(restored.NativePeopleFor(1)[0].ResponseIncidentId == null);
                Check.Equal(keeper.Id, restored.RecordViolence(1, keeper.Id, 2000).VictimId);
            }));
            cases.Add(("Native person identity preserves name and appearance across repeated registration and reload", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var person = store.RegisterNativePerson(1, 4, -123, "Ysausa Masterham", ResidentSex.Female, 1, 0, 184, 18);
                Check.Equal(person.Id, store.RegisterNativePerson(1, 4, -123, "Temporary quest label", ResidentSex.Female, 1, 0, 184, 18).Id);
                Check.Equal("Ysausa Masterham", person.Name);
                Check.Equal(1, store.Snapshot().NativePeople.Count);
                var snapshot = store.Snapshot(); snapshot.NativePeople[0].Name = "Changed copy";
                Check.Equal("Ysausa Masterham", store.Snapshot().NativePeople[0].Name);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                var saved = restored.NativePeopleFor(1)[0];
                Check.Equal(person.Id, saved.Id); Check.Equal(184, saved.TextureArchive); Check.Equal(18, saved.TextureRecord);
                Check.Equal(ResidentSex.Female, saved.Sex);
                Check.Throws<ArgumentException>(() => restored.RegisterNativePerson(1, 4, -123, "X", ResidentSex.Male, 1, 0, 184, 18));
            }));
            cases.Add(("Native keeper reserves on-site bed first and bedless staff use free residential capacity", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 8 }, 1);
                town.Households[0].BedCapacity = 3;
                var first = store.RegisterNativePerson(1, 4, 1, "First Keeper", ResidentSex.Male, 1, 41, 182, 1);
                var second = store.RegisterNativePerson(1, 4, 2, "Second Keeper", ResidentSex.Female, 1, 41, 182, 2);
                var third = store.RegisterNativePerson(1, 5, 3, "Third Keeper", ResidentSex.Male, 1, 41, 182, 3);
                var fourth = store.RegisterNativePerson(1, 5, 4, "Fourth Keeper", ResidentSex.Female, 1, 41, 182, 4);
                var homes = new[] { new HomeProfile(8, 3, 100) };
                Check.True(store.AssignNativeBed(first.Id, new HomeProfile(4, 1, 10), homes));
                Check.Equal(4, first.HomeKey); Check.Equal(0, first.BedSlot);
                Check.True(store.AssignNativeBed(second.Id, new HomeProfile(4, 1, 10), homes));
                Check.Equal(8, second.HomeKey); Check.Equal(1, second.BedSlot);
                Check.True(store.AssignNativeBed(third.Id, new HomeProfile(5, 0, 10), homes));
                Check.Equal(8, third.HomeKey); Check.Equal(2, third.BedSlot);
                Check.True(!store.AssignNativeBed(fourth.Id, new HomeProfile(5, 0, 10), homes));
                Check.Equal(-1, fourth.HomeKey);
                Check.True(store.AssignNativeBed(first.Id, new HomeProfile(4, 0, 10), new HomeProfile[0]));
                Check.Equal(4, first.HomeKey); // A later survey cannot relocate a saved person silently.
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal(1, restored.NativePeopleFor(1).Find(p => p.Id == second.Id).BedSlot);
            }));
            cases.Add(("Native person restore rejects duplicated bed reservations and preserves legacy population", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var first = store.RegisterNativePerson(1, 4, 1, "Keeper One", ResidentSex.Male, 1, 41, 182, 1);
                var second = store.RegisterNativePerson(1, 4, 2, "Keeper Two", ResidentSex.Female, 1, 41, 182, 2);
                store.AssignNativeBed(first.Id, new HomeProfile(4, 2, 10), new HomeProfile[0]);
                store.AssignNativeBed(second.Id, new HomeProfile(4, 2, 10), new HomeProfile[0]);
                var corrupt = store.Snapshot(); corrupt.NativePeople[1].BedSlot = corrupt.NativePeople[0].BedSlot;
                Check.Throws<ArgumentException>(() => store.Restore(corrupt));
                Check.Equal(1, store.NativePeopleFor(1)[1].BedSlot);
                var legacy = store.Snapshot(); legacy.NativePeople = null;
                var restored = new WorldStateStore(0); restored.Restore(legacy);
                Check.Equal(0, restored.NativePeopleFor(1).Count); Check.Equal(1, restored.Snapshot().Settlements.Count);
            }));
        }
    }
}
