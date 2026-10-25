using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class OutdoorCorpseTests
    {
        private static WorldStateStore Body(out CivilianIncident alarm, out string witness)
        {
            var store = new WorldStateStore(13);
            var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
            var victim = town.Households[0].Residents[0]; witness = town.Households[1].Residents[0].Id;
            alarm = store.RecordViolence(1, victim.Id, 100);
            store.LocateViolence(alarm.Id, -1, 1, 0, 2);
            Population.MarkDead(town, victim.Id); store.ConfirmCorpse(alarm.Id, 100, -1, 3, 0, 4);
            return store;
        }
        public static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Early physical corpse removal starts an immediate nonrenewable thirty-minute inquiry window",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});var guard=roster.Guards[0];
                var target=new SavedRoutePoint{X=3,Z=4};
                roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,200,200,
                    new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,target}}},
                    new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},2,10);
                roster.Advance(230);bool statement;
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,230,out statement));
                Check.True(store.ClearOutdoorCorpse(alarm.Id,guard.Id,3,4,230));
                Check.Near(2030,roster.Reports[0].InvestigationUntil);
                Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,230,out statement),
                    "a physically present guard can ask immediately after cleanup");
                Check.True(!statement);store.ObserveOutdoorCorpse(alarm.Id,true,400);
                Check.Near(2030,roster.Reports[0].InvestigationUntil);
                store.Restore(store.Snapshot());roster=store.GetGuards(1);guard=roster.Guards.Find(g=>g.Id==guard.Id);
                var native=store.RegisterNativePerson(1,-1,998,"Late witness",ResidentSex.Male,0,0,182,0);
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,native.Id,true,2,true,2030,out statement));
                var before=roster.Position(guard.Id,2030-0.000001);var returning=roster.Position(guard.Id,2030);
                Check.Near(before.X,returning.X,0.001);Check.Near(before.Z,returning.Z,0.001);
                Check.Equal(GuardDuty.Returning,guard.Duty);
                var forged=store.Snapshot();forged.Garrisons[0].Reports[0].InvestigationUntil++;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(forged));
                forged=store.Snapshot();forged.Version=63;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(forged));
                forged=store.Snapshot();var report=forged.Garrisons[0].Reports[0];
                report.CorpseInquiryStartedAt++;report.InvestigationUntil++;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(forged));
            }));
            cases.Add(("Physical cleanup starts inquiries without requiring a player departure",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});var guard=roster.Guards[0];
                var target=new SavedRoutePoint{X=3,Z=4};
                roster.DispatchOutdoorInvestigation("outdoor:"+alarm.Id,200,
                    new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,target}}},
                    new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},1,2,10);
                roster.Advance(230);Check.True(store.ClearOutdoorCorpse(alarm.Id,guard.Id,3,4,230));
                bool statement;Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,230,out statement));
                Check.Near(2030,guard.SearchUntil);store.Restore(store.Snapshot());
                roster=store.GetGuards(1);
                var before=roster.Position(guard.Id,2030-0.000001);var after=roster.Position(guard.Id,2030);
                Check.Near(before.X,after.X,0.001);Check.Near(before.Z,after.Z,0.001);
            }));
            cases.Add(("Corpse inquiry keeps an earlier departure return deadline",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint{X=200,Z=4}});var guard=roster.Guards[0];
                var target=new SavedRoutePoint{X=3,Z=4};
                roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,200,200,
                    new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,target}}},
                    new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},2,10);
                double arrived=guard.ArrivesAt,deadline=guard.SearchUntil;roster.Advance(arrived);
                Check.True(deadline<arrived+1800);Check.True(store.ClearOutdoorCorpse(alarm.Id,guard.Id,3,4,arrived));
                Check.Near(deadline,guard.SearchUntil);bool statement;
                Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,arrived,out statement));
                store.Restore(store.Snapshot());store.GetGuards(1).Advance(3800);
                Check.Equal(GuardDuty.Posted,store.GetGuards(1).Guards[0].Duty);
            }));
            cases.Add(("Schema63 inquiry history survives migration into a fixed cleanup window",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});var guard=roster.Guards[0];
                var target=new SavedRoutePoint{X=3,Z=4};
                roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,200,200,
                    new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,target}}},
                    new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},2,10);
                roster.Advance(2000);store.ObserveOutdoorCorpse(alarm.Id,true,2000);bool statement;
                Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,2000,out statement));
                var legacy=store.Snapshot();legacy.Version=63;var report=legacy.Garrisons[0].Reports[0];
                report.HasCorpseInquiryWindow=false;report.CorpseInquiryStartedAt=0;
                report.CorpseSearchVersion=0;
                legacy.AlarmIncidents[0].Corpse.Inquiries[0].WindowVersion=0;
                var restored=new WorldStateStore(0);restored.Restore(legacy);
                Check.True(restored.SynchronizeOutdoorCorpseInquiry(alarm.Id));
                var person=restored.RegisterNativePerson(1,-1,1998,"Other bystander",ResidentSex.Male,0,0,182,0);
                Check.True(restored.TryQuestionCorpseCase(alarm.Id,guard.Id,person.Id,true,2,true,2100,out statement));
                restored.Restore(restored.Snapshot());var rows=restored.Snapshot().AlarmIncidents[0].Corpse.Inquiries;
                Check.Equal(0,rows[0].WindowVersion);Check.Equal(1,rows[1].WindowVersion);
            }));
            cases.Add(("Late corpse responders return from their actual travelled route when inquiry expires",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                var target=new SavedRoutePoint{X=3,Z=4};
                var roster=store.GetOrCreateGuards(1,2,new[]{target,new SavedRoutePoint{X=500,Z=4}});
                var routes=new Dictionary<string,List<SavedRoutePoint>>();
                foreach(var guard in roster.Guards)routes.Add(guard.Id,new List<SavedRoutePoint>{guard.Post,target});
                roster.DispatchOutdoorInvestigation("outdoor:"+alarm.Id,200,routes,
                    new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},2,2,10);
                roster.Advance(230);var early=roster.Guards.Find(g=>g.Post.X==3);var late=roster.Guards.Find(g=>g.Post.X==500);
                Check.True(late.ArrivesAt>2030);Check.True(store.ClearOutdoorCorpse(alarm.Id,early.Id,3,4,230));
                var before=roster.Position(late.Id,2030-0.000001);var after=roster.Position(late.Id,2030);
                Check.Near(before.X,after.X,0.001);Check.Near(before.Z,after.Z,0.001);
                Check.Equal(GuardDuty.Returning,late.Duty);Check.True(after.X>target.X);
                Check.True(!roster.Reports[0].Arrivals.Find(a=>a.GuardId==late.Id).Arrived);
                store.Restore(store.Snapshot());roster=store.GetGuards(1);roster.Advance(5000);
                Check.Equal(GuardDuty.Posted,roster.Guards.Find(g=>g.Id==late.Id).Duty);
            }));
            cases.Add(("A physically arrived departure guard removes the body before the absence timeout",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});var guard=roster.Guards[0];
                var target=new SavedRoutePoint{X=3,Z=4};
                roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,200,200,
                    new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,target}}},
                    new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},2,10);
                roster.Advance(230);store.ObserveOutdoorCorpse(alarm.Id,true,230);
                Check.True(!alarm.Corpse.Cleared,"brief shop absence alone must not clear the body");
                Check.True(!store.ClearOutdoorCorpse(alarm.Id,guard.Id,30,40,230),"a remote guard cannot clear it");
                Check.True(store.ClearOutdoorCorpse(alarm.Id,guard.Id,3,4,230),"a real guard at the body uses physical cleanup");
                Check.Near(230,alarm.Corpse.ClearedAt);Check.True(!alarm.Corpse.Departure.Completed);
                store.Restore(store.Snapshot());Check.True(store.ObserveCorpses(1,-1,231).Count==0);
            }));
            cases.Add(("Departure deadline reserves return travel for every previously assigned guard", () => {
                var roster=GuardRoster.Create(1,9,2,new[]{new SavedRoutePoint{X=90},new SavedRoutePoint()});
                var target=new SavedRoutePoint{X=100};
                var routes=new Dictionary<string,List<SavedRoutePoint>>();
                foreach(var guard in roster.Guards)routes.Add(guard.Id,new List<SavedRoutePoint>{guard.Post,target});
                var patrol=new List<SavedRoutePoint>{target,new SavedRoutePoint{X=110},target};
                roster.DispatchOutdoorInvestigation("case",100,routes,patrol,2,2,10);
                roster.DispatchDepartureInvestigation("case",200,200,routes,patrol,2,10);
                roster=GuardRoster.CopyValidated(roster);
                roster.Advance(3800);
                foreach(var guard in roster.Guards)Check.Equal(GuardDuty.Posted,guard.Duty);
            }));
            cases.Add(("Departure investigation records physical mobile and static inquiries without invented guilt", () => {
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                var native=store.RegisterNativePerson(1,-1,321,"Bystander",ResidentSex.Male,0,0,182,0);
                var seen=store.Witness(alarm.Id,native.Id,150);seen.StaticWitness=true;
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});var guard=roster.Guards[0];
                var target=new SavedRoutePoint{X=3,Z=4};
                var route=new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,target}}};
                roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,200,200,route,
                    new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},2,10);
                roster.Advance(2000);
                bool statement;
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,1999,out statement));
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,5,true,2000,out statement));
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,false,2000,out statement));
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,2000,out statement),
                    "an inquiry cannot skip the body-removal phase");
                store.ObserveOutdoorCorpse(alarm.Id,true,2000);
                Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,2000,out statement));
                Check.True(!statement,"a questioned bystander is not automatically a witness");
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,2001,out statement));
                Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,native.Id,true,2,true,2001,out statement));
                Check.True(statement && seen.StatementDelivered);
                Check.True(!alarm.AccusationIssued && !alarm.PlayerAttributed,"interview does not invent player attribution");
                store.Restore(store.Snapshot());
                var body=store.Snapshot().AlarmIncidents[0].Corpse;
                Check.Equal(2,body.Inquiries.Count);Check.True(body.Inquiries[1].StaticPerson);
                var corrupt=store.Snapshot();corrupt.AlarmIncidents[0].Corpse.Inquiries[0].At=1999;
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));
                store.GetGuards(1).Advance(3800);
                Check.True(!store.TryQuestionCorpseCase(alarm.Id,guard.Id,"unknown",false,1,true,3800,out statement));
            }));
            cases.Add(("Departure chronology rejects forged early cleanup and preserves genuine schema60 corpses", () => {
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                var old=store.Snapshot();old.Version=60;store.Restore(old);
                Check.True(store.Snapshot().AlarmIncidents[0].Corpse.Departure==null);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                var corrupt=store.Snapshot();corrupt.AlarmIncidents[0].Corpse.Departure.AwaySince=201;
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));
                Check.Throws<ArgumentException>(()=>store.ObserveOutdoorCorpse(alarm.Id,true,199));
                store.ObserveOutdoorCorpse(alarm.Id,false,2000);
                corrupt=store.Snapshot();corrupt.AlarmIncidents[0].Corpse.Departure.CompletedAt=1999;
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));
            }));
            cases.Add(("Departure dispatch selects one nearest reachable guard and ends scene within one game hour", () => {
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                var roster=store.GetOrCreateGuards(1,2,new[]{new SavedRoutePoint(),new SavedRoutePoint{X=90}});
                var target=new SavedRoutePoint{X=100};var routes=new Dictionary<string,List<SavedRoutePoint>>();
                foreach(var guard in roster.Guards)routes.Add(guard.Id,new List<SavedRoutePoint>{guard.Post,target});
                var patrol=new List<SavedRoutePoint>{target,new SavedRoutePoint{X=110},target};
                var sent=roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,200,200,routes,patrol,2,10);
                Check.Equal(1,sent.Count);Check.Near(90,sent[0].Post.X);
                Check.Equal(GuardDuty.Responding,sent[0].Duty);
                Check.True(alarm.Corpse.Discovery==null,"dispatch is not fabricated eyewitness evidence");
                store.Restore(store.Snapshot());roster=store.GetGuards(1);var assigned=roster.Guards.Find(g=>g.Post.X==90);
                roster.Advance(2000);Check.Equal(GuardDuty.Searching,assigned.Duty);
                double until=assigned.SearchUntil;
                var before=roster.Position(assigned.Id,until-0.000001);
                var returning=roster.Position(assigned.Id,until);
                Check.Near(before.X,returning.X,0.001);
                roster.Advance(3800);Check.Equal(GuardDuty.Posted,assigned.Duty);
                Check.True(assigned.IncidentId==null);
                Check.Near(90,roster.Position(assigned.Id,3800).X);
                store.Restore(store.Snapshot());
                Check.Equal(GuardDuty.Posted,store.GetGuards(1).Guards.Find(g=>g.Id==assigned.Id).Duty);
            }));
            cases.Add(("Player departure waits thirty game minutes of absence and never clears an attended body", () => {
                CivilianIncident alarm; string witness; var store=Body(out alarm,out witness);
                store.NoticeOutdoorCorpse(alarm.Id,witness,false,150);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                store.ObserveOutdoorCorpse(alarm.Id,true,1999);
                Check.True(!alarm.Corpse.Cleared,"1799 game seconds away must not clear the corpse");
                store.ObserveOutdoorCorpse(alarm.Id,true,2000);
                Check.True(!alarm.Corpse.Cleared,"elapsed time alone cannot clear an attended corpse");
                store.ObserveOutdoorCorpse(alarm.Id,false,2000);
                Check.True(!alarm.Corpse.Cleared,"a new departure starts a new absence interval");
                store.ObserveOutdoorCorpse(alarm.Id,false,3800);
                Check.True(alarm.Corpse.Cleared,"off-site corpse is eligible exactly thirty minutes after departure");
                Check.Near(3800,alarm.Corpse.ClearedAt);
                Check.Equal(witness,alarm.Corpse.Discovery.ObserverId);
                Check.True(!alarm.Corpse.Discovery.PlayerPresent,"cleanup must not fabricate player guilt");
            }));
            cases.Add(("Departure before timeout survives reload and expires before the player returns", () => {
                CivilianIncident alarm; string witness; var store=Body(out alarm,out witness);
                store.ObserveOutdoorCorpse(alarm.Id,false,200);
                Check.True(!alarm.Corpse.Cleared);
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                restored.ObserveOutdoorCorpse(alarm.Id,true,2000);
                var body=restored.Snapshot().AlarmIncidents[0].Corpse;
                Check.True(body.Cleared,"absence must persist while the settlement is unloaded");
                Check.True(body.Discovery==null,"offscreen cleanup is not a witness statement");
                Check.Near(2000,body.ClearedAt);
                restored.Restore(restored.Snapshot());
                restored.ObserveOutdoorCorpse(alarm.Id,true,2100);
                Check.Near(2000,restored.Snapshot().AlarmIncidents[0].Corpse.ClearedAt);
            }));
            cases.Add(("Crime scene warning allows ten unpaused seconds and resets only on departure", () => {
                var warning = new CrimeSceneWarning(); Check.True(warning.Begin()); Check.Equal(10, warning.DisplaySeconds);
                warning.Advance(4, true, false); Check.Near(6, warning.Remaining); Check.Equal(6, warning.DisplaySeconds);
                warning.Advance(100, true, true); Check.Near(6, warning.Remaining); Check.Equal(6, warning.DisplaySeconds);
                warning.Advance(5.1, true, false); Check.Equal(1, warning.DisplaySeconds);
                var saved = CrimeSceneWarning.Copy(warning);
                saved.Advance(0.8, true, false); Check.Equal(1, saved.DisplaySeconds); Check.True(!saved.Violated);
                saved.Advance(0.11, true, false); Check.Equal(0, saved.DisplaySeconds); Check.True(saved.Violated);
                warning.Advance(1, true, false); Check.Equal(0, warning.DisplaySeconds);
                Check.True(!warning.Begin());
                saved.Advance(0, false, false); Check.True(!saved.Issued && !saved.Violated);
                Check.True(saved.Begin()); Check.Near(10, saved.Remaining);
                Check.Throws<ArgumentException>(() => saved.Advance(double.NaN, true, false));
                saved.Remaining = 11; Check.Throws<ArgumentException>(() => CrimeSceneWarning.Copy(saved));
            }));
            cases.Add(("Outdoor investigators patrol for one hour then return without timer renewal", () => {
                var roster = GuardRoster.Create(1, 9, 1, new[] { new SavedRoutePoint() });
                var guard = roster.Guards[0];
                var target = new SavedRoutePoint { X = 100 };
                var approaches = new Dictionary<string, List<SavedRoutePoint>> {
                    { guard.Id, new List<SavedRoutePoint> { guard.Post, target } } };
                var patrol = new List<SavedRoutePoint> { target, new SavedRoutePoint { X = 120 }, target };
                roster.DispatchOutdoorInvestigation("body", 100, approaches, patrol, 1, 2, 10);
                Check.Near(600, guard.ArrivesAt); Check.Near(4200, guard.SearchUntil);
                var position = roster.Position(guard.Id, 650);
                Check.Near(110, position.X);
                var copy = GuardRoster.CopyValidated(roster);
                copy.DispatchOutdoorInvestigation("body", 900, approaches, patrol, 1, 2, 10);
                Check.Near(4200, copy.Guards[0].SearchUntil);
                Check.True(copy.Reports[0].HasArrival); Check.Near(600, copy.Reports[0].FirstArrivalAt);
                Check.Near(110, copy.Position(guard.Id, 650).X);
                var corrupted = GuardRoster.CopyValidated(copy);
                corrupted.Guards[0].Route[corrupted.Guards[0].Route.Count - 1].X += 10;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(corrupted));
                copy.Advance(4200); Check.Equal(GuardDuty.Returning, copy.Guards[0].Duty);
                copy.Advance(4700); Check.Equal(GuardDuty.Posted, copy.Guards[0].Duty);
                Check.True(GuardRoster.CopyValidated(copy).Reports[0].HasArrival);
            }));
            cases.Add(("Outdoor investigation rejects disconnected patrols without assigning guards", () => {
                var roster = GuardRoster.Create(1, 9, 1, new[] { new SavedRoutePoint() }); var guard = roster.Guards[0];
                var routes = new Dictionary<string, List<SavedRoutePoint>> {
                    { guard.Id, new List<SavedRoutePoint> { guard.Post, new SavedRoutePoint { X = 100 } } } };
                Check.Throws<ArgumentException>(() => roster.DispatchOutdoorInvestigation("body", 100, routes,
                    new List<SavedRoutePoint> { new SavedRoutePoint { X = 10 }, new SavedRoutePoint { X = 20 }, new SavedRoutePoint { X = 10 } }, 1, 2, 10));
                Check.Equal(0, roster.Reports.Count); Check.Equal(GuardDuty.Posted, guard.Duty);
            }));
            cases.Add(("Unseen outdoor corpse survives a fresh departure even after elapsed days", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                store.ObserveOutdoorCorpse(alarm.Id, false, 900000);
                Check.Equal(1, store.ObserveCorpses(1, -1, 900000).Count);
                Check.True(!alarm.Corpse.Cleared && alarm.Corpse.Discovery == null);
            }));
            cases.Add(("Seen outdoor corpse requires a guard at the body and persists after reload", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 150);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 151, true);
                Check.True(!alarm.Corpse.Discovery.PlayerPresent, "Later arrival must not retroactively accuse a passerby.");
                store.ObserveOutdoorCorpse(alarm.Id, true, 900000);
                Check.True(!alarm.Corpse.Cleared);
                // Staying on site keeps the physical-guard cleanup path independent of elapsed time.
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint { X=3,Z=4 }});
                Check.True(!store.ClearOutdoorCorpse(alarm.Id,roster.Guards[0].Id,30,4,900001));
                Check.True(store.ClearOutdoorCorpse(alarm.Id,roster.Guards[0].Id,3,4,900001));
                var copy = new WorldStateStore(0); copy.Restore(store.Snapshot());
                copy.ObserveOutdoorCorpse(alarm.Id, true, 900100);
                var body = copy.Snapshot().AlarmIncidents[0].Corpse;
                Check.True(body.Cleared); Check.Near(900001, body.ClearedAt);
                Check.True(copy.Snapshot().AlarmIncidents[0].People.Exists(p=>p.ResidentId==witness && p.OutdoorReport!=null));
                Check.Near(150, body.Discovery.SeenAt); Check.Equal(witness, body.Discovery.ObserverId);
            }));
            cases.Add(("Corpse discovery requires a living known observer and preserves first sighting", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, "unknown", false, 150));
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, alarm.VictimId, false, 150));
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, witness, false, 99));
                Check.True(alarm.Corpse.Discovery == null);
                var roster = store.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
                store.NoticeOutdoorCorpse(alarm.Id, roster.Guards[0].Id, true, 160);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 170);
                Check.Near(160, alarm.Corpse.Discovery.SeenAt); Check.True(alarm.Corpse.Discovery.Guard);
                store.ObserveOutdoorCorpse(alarm.Id, false, 159);
                Check.True(!alarm.Corpse.Cleared);
                roster.Kill(roster.Guards[0].Id, 180);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Near(160, restored.Snapshot().AlarmIncidents[0].Corpse.Discovery.SeenAt);
                var malformed = restored.Snapshot(); malformed.Garrisons[0].Guards[0] = null;
                Check.Throws<ArgumentException>(() => restored.Restore(malformed));
            }));
            cases.Add(("Outdoor corpse restore rejects invented observer and early cleanup transactionally", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 150);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint { X=3,Z=4 }});
                store.ClearOutdoorCorpse(alarm.Id,roster.Guards[0].Id,3,4,160);
                var broken = store.Snapshot(); broken.AlarmIncidents[0].Corpse.Discovery.ObserverId = "invented";
                Check.Throws<ArgumentException>(() => store.Restore(broken));
                broken = store.Snapshot(); broken.AlarmIncidents[0].Corpse.ClearedAt = 149;
                Check.Throws<ArgumentException>(() => store.Restore(broken));
                Check.Near(160, store.Snapshot().AlarmIncidents[0].Corpse.ClearedAt);
            }));
            cases.Add(("Current outdoor bodies reject unseen cleanup while old cleared saves remain cleared", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                var corrupted = store.Snapshot();
                corrupted.AlarmIncidents[0].Corpse.Cleared = true;
                corrupted.AlarmIncidents[0].Corpse.ClearedAt = 90000;
                Check.Throws<ArgumentException>(() => store.Restore(corrupted));
                corrupted.AlarmIncidents[0].Corpse.CleanupVersion = 0;
                store.Restore(corrupted);
                Check.True(store.Snapshot().AlarmIncidents[0].Corpse.Cleared);
            }));
            cases.Add(("Body report persists independently of fear and cannot fabricate a witness statement",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.NoticeOutdoorCorpse(alarm.Id,witness,false,150);
                var person=alarm.People.Find(p=>p.ResidentId==witness);
                Check.True(!CivilianAlarms.PersonActive(alarm,person,151));
                person.OutdoorReport.HasPosition=true;person.OutdoorReport.X=12;person.OutdoorReport.Z=14;person.OutdoorReport.Deferred=true;
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                var saved=restored.Snapshot().AlarmIncidents[0].People.Find(p=>p.ResidentId==witness);
                Check.True(saved.OutdoorReport.Deferred);Check.Near(12,saved.OutdoorReport.X);
                saved.OutdoorReport.X=99;Check.Near(12,restored.Snapshot().AlarmIncidents[0].People.Find(p=>p.ResidentId==witness).OutdoorReport.X);
                var bad=store.Snapshot();bad.AlarmIncidents[0].People.Find(p=>p.ResidentId==witness).StatementDelivered=true;
                Check.Throws<ArgumentException>(()=>restored.Restore(bad));
            }));
            cases.Add(("Murder refuge ends at one hour or only after resolution AND corpse removal",()=>{
                CivilianIncident alarm;string witness;var store=Body(out alarm,out witness);
                store.MarkPlayerMurder(alarm.Id);var person=store.Witness(alarm.Id,witness,150);
                person.HasPosition=true;person.ReachedRefuge=true;person.BuildingKey=8;person.Reaction=CivilianReaction.Flee;
                store.RecordShelterArrival(alarm.Id,witness,200);
                Check.True(CivilianAlarms.PersonActive(alarm,person,3799));Check.True(!CivilianAlarms.PersonActive(alarm,person,3800));
                store.MarkMurderAccusationIssued(alarm.Id);store.ResolveAccusedPlayerMurders();
                Check.True(CivilianAlarms.PersonActive(alarm,person,300));Check.True(store.ShelterLocked(1,8,300));
                var guards=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint{X=3,Z=4}});
                store.ClearOutdoorCorpse(alarm.Id,guards.Guards[0].Id,3,4,400);
                Check.True(!CivilianAlarms.PersonActive(alarm,person,401));Check.True(!store.ShelterLocked(1,8,401));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());Check.True(!restored.ShelterLocked(1,8,401));
            }));
            cases.Add(("Known enemy murderer and exterior static witness survive restore without player accusation",()=>{
                CivilianIncident alarm;string ignored;var store=Body(out alarm,out ignored);
                var native=store.RegisterNativePerson(1,-1,123,"Witness",ResidentSex.Male,0,0,182,0);
                var person=store.Witness(alarm.Id,native.Id,150);person.StaticWitness=true;person.Reaction=CivilianReaction.Flee;
                alarm.KillerThreatId="native:1234";
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                var saved=restored.Snapshot().AlarmIncidents[0];Check.Equal("native:1234",saved.KillerThreatId);
                Check.True(!saved.PlayerAttributed&&!saved.AccusationIssued&&saved.People.Exists(p=>p.StaticWitness));
                var bad=store.Snapshot();bad.AlarmIncidents[0].PlayerAttributed=true;
                Check.Throws<ArgumentException>(()=>restored.Restore(bad));
            }));
            cases.Add(("Outdoor discovery cannot clear an indoor corpse", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                alarm.Corpse.BuildingKey = 7;
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, witness, false, 150));
                Check.Throws<ArgumentException>(() => store.ObserveOutdoorCorpse(alarm.Id, false, 160));
                Check.True(!alarm.Corpse.Cleared);
            }));
        }
    }
}
