using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class CorpseSuspectEvidenceTests
    {
        private static WorldStateStore Case(out CivilianIncident alarm,out string witness,out GuardRecord guard,bool player=true,bool clear=true)
        {
            var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8},1);
            var victim=town.Households[0].Residents[0];witness=town.Households[1].Residents[0].Id;
            alarm=store.RecordViolence(1,victim.Id,100);store.LocateViolence(alarm.Id,-1,0,0,0);
            Population.MarkDead(town,victim.Id);store.ConfirmCorpse(alarm.Id,100,-1,0,0,0);
            if(player)store.MarkPlayerMurder(alarm.Id);
            store.Witness(alarm.Id,witness,110);
            var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});guard=roster.Guards[0];
            roster.DispatchOutdoorInvestigation("outdoor:"+alarm.Id,200,
                new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,new SavedRoutePoint()}}},
                new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=20},new SavedRoutePoint()},1,1,1);
            roster.Advance(200);if(clear)Check.True(store.ClearOutdoorCorpse(alarm.Id,guard.Id,0,0,200));return store;
        }
        public static void Add(List<(string,Action)> cases)
        {
            cases.Add(("An NPC investigator hands off to cleanup at its actual position and retains its post",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard,false);
                alarm.KillerThreatId="native:1234";bool statement;
                store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,200,out statement);
                Check.True(store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,200));
                var roster=store.GetGuards(1);roster.Engage(guard.Id,201);
                roster.ObserveEngagement(guard.Id,new SavedRoutePoint{X=5},1,201,false);
                var path=new List<SavedRoutePoint>{new SavedRoutePoint{X=5},new SavedRoutePoint{X=8}};
                var home=new List<SavedRoutePoint>{new SavedRoutePoint{X=8},guard.Post};
                Check.Throws<ArgumentException>(()=>roster.RedirectMurdererCorpseCleanup(guard.Id,"other","cleanup:test","loot:1",201,path,home,1,1));
                Check.Equal(1,roster.Reports.Count);
                roster.RedirectMurdererCorpseCleanup(guard.Id,"native:1234","cleanup:test","loot:1",201,path,home,1,1);
                Check.Near(5,roster.Position(guard.Id,201).X);Check.Near(8,roster.Position(guard.Id,204).X);
                Check.Equal(GuardDuty.Searching,guard.Duty);Check.Equal("cleanup:test",guard.IncidentId);
                Check.True(roster.Reports[1].ThreatId==null && !roster.Reports[1].OccupiesScene && roster.Reports[1].CorpseSuspect==null);
                Check.Equal("loot:1",roster.Reports[1].CleanupCorpseId);Check.True(roster.Reports[0].CleanupCorpseId==null);
                store.Restore(store.Snapshot());roster=store.GetGuards(1);guard=roster.Guards[0];
                Check.Equal("cleanup:test",guard.IncidentId);Check.Near(0,guard.Post.X);
                roster.Engage(guard.Id,205);roster.ReturnFromEngagement(guard.Id,205,home,1,1);roster.Advance(213);
                Check.Equal(GuardDuty.Posted,guard.Duty);Check.Near(0,roster.Position(guard.Id,213).X);
            })));
            cases.Add(("A physically delivered direct report identifies a suspect after cleanup without another interview",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard,true,false);
                store.NoticeOutdoorCorpse(alarm.Id,witness,false,200);
                Check.True(store.TryDeliverOutdoorMurderStatement(alarm.Id,witness,guard.Id,2,true,201));
                Check.True(!store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,201));
                store.Restore(store.Snapshot());alarm=store.OutdoorCorpses(1)[0];guard=store.GetGuards(1).Guards[0];
                Check.Near(201,alarm.People.Find(p=>p.ResidentId==witness).OutdoorReport.DeliveredAt);
                Check.True(store.ClearOutdoorCorpse(alarm.Id,guard.Id,0,0,220));
                Check.True(store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,220));
                var known=store.GetGuards(1).Reports[0].CorpseSuspect;
                Check.Near(220,known.LearnedAt);Check.Equal(witness,known.WitnessId);
                Check.True(!alarm.AccusationIssued && store.GetGuards(1).Reports[0].SuspectSighting==null);
                store.Restore(store.Snapshot());Check.True(store.GetGuards(1).Reports[0].CorpseSuspect!=null);
            })));
            cases.Add(("Murder report delivery requires actual contact, sight, a direct witness and the assigned case",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard,true,false);
                store.NoticeOutdoorCorpse(alarm.Id,witness,false,200);
                Check.True(!store.TryDeliverOutdoorMurderStatement(alarm.Id,witness,guard.Id,5,true,201));
                Check.True(!store.TryDeliverOutdoorMurderStatement(alarm.Id,witness,guard.Id,2,false,201));
                Check.True(!store.TryDeliverOutdoorMurderStatement(alarm.Id,witness,"unknown",2,true,201));
                var person=alarm.People.Find(p=>p.ResidentId==witness);person.DirectWitness=false;
                Check.True(!store.TryDeliverOutdoorMurderStatement(alarm.Id,witness,guard.Id,2,true,201));
                Check.True(!person.StatementDelivered && !person.OutdoorReport.Delivered);
            })));
            cases.Add(("Unverified old delivery flags cannot supply murder identity and invalid contact timestamps reject restore",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard,true,false);
                store.NoticeOutdoorCorpse(alarm.Id,witness,false,200);
                var person=alarm.People.Find(p=>p.ResidentId==witness);person.StatementDelivered=true;person.OutdoorReport.Delivered=true;person.OutdoorReport.GuardId=guard.Id;
                Check.True(store.ClearOutdoorCorpse(alarm.Id,guard.Id,0,0,220));
                Check.True(!store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,220));
                person.OutdoorReport.Delivered=false;person.StatementDelivered=false;
                Check.True(store.TryDeliverOutdoorMurderStatement(alarm.Id,witness,guard.Id,2,true,221));
                var broken=store.Snapshot();broken.AlarmIncidents[0].People.Find(p=>p.ResidentId==witness).OutdoorReport.DeliveredAt=199;
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
            })));
            cases.Add(("Corpse testimony identifies the player without fabricating a suspect position or crime",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard);
                Check.True(!store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,200));
                bool statement;Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,200,out statement));
                Check.True(statement);Check.True(store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,200));
                var report=store.GetGuards(1).Reports[0];Check.Equal(CorpseSuspectKind.Player,report.CorpseSuspect.Kind);
                Check.Equal("player",report.CorpseSuspect.Identity);Check.True(report.ThreatId==null && report.SuspectSighting==null);
                Check.Equal(GuardDuty.Searching,guard.Duty);Check.True(guard.LastSeenSuspect==null && !alarm.AccusationIssued);
                store.Restore(store.Snapshot());Check.Equal(witness,store.GetGuards(1).Reports[0].CorpseSuspect.WitnessId);
            })));
            cases.Add(("Enemy murder testimony preserves investigation and native target identity across restore",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard,false);
                alarm.KillerThreatId="native:1234";bool statement;
                Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,200,out statement));
                Check.True(store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,200));
                store.Restore(store.Snapshot());var report=store.GetGuards(1).Reports[0];
                Check.True(report.Investigation && report.ThreatId==null);Check.Equal("native:1234",report.CorpseSuspect.Identity);
                Check.Equal(CorpseSuspectKind.HostileEntity,report.CorpseSuspect.Kind);
            })));
            cases.Add(("Hearsay and an unidentified killer cannot create suspect knowledge",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard,false);
                bool statement;Check.True(store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,200,out statement));
                Check.True(!store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,200));
                Check.True(store.GetGuards(1).Reports[0].CorpseSuspect==null);
            })));
            cases.Add(("A known corpse suspect requires an exact visible sighting before any target position exists",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard);
                bool statement;store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,200,out statement);
                store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,200);
                Check.True(!store.TryObserveCorpseSuspect(alarm.Id,guard.Id,CorpseSuspectKind.Player,"other",3,4,5,true,201));
                Check.True(!store.TryObserveCorpseSuspect(alarm.Id,guard.Id,CorpseSuspectKind.Player,"player",3,4,5,false,201));
                Check.True(!store.TryObserveCorpseSuspect(alarm.Id,guard.Id,CorpseSuspectKind.Player,"player",3,4,21,true,201));
                Check.True(store.TryObserveCorpseSuspect(alarm.Id,guard.Id,CorpseSuspectKind.Player,"player",3,4,5,true,201));
                var report=store.GetGuards(1).Reports[0];Check.Near(3,report.SuspectSighting.X);Check.Near(201,report.SuspectSighting.At);
                Check.Equal(GuardDuty.Searching,guard.Duty);Check.True(guard.LastSeenSuspect==null);
                store.Restore(store.Snapshot());Check.Near(4,store.GetGuards(1).Reports[0].SuspectSighting.Z);
            })));
            cases.Add(("Forged corpse identity provenance and future fields reject restore atomically",(Action)(()=>{
                CivilianIncident alarm;string witness;GuardRecord guard;var store=Case(out alarm,out witness,out guard);
                bool statement;store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness,false,2,true,200,out statement);
                store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness,200);
                var broken=store.Snapshot();broken.Garrisons[0].Reports[0].CorpseSuspect.Identity="somebody-else";
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                broken=store.Snapshot();broken.AlarmIncidents[0].Corpse.Inquiries.Clear();
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                broken=store.Snapshot();broken.Version=64;broken.Garrisons[0].Reports[0].CorpseSearchVersion=0;
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                Check.Equal("player",store.GetGuards(1).Reports[0].CorpseSuspect.Identity);
            })));
        }
    }
}
