using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class CrimeSceneTests
    {
        private static WorldStateStore Scene(out CivilianIncident incident)
        {
            var store = new WorldStateStore(1);
            var town = store.GetOrCreate(1, new[] { 7 }, 1);
            var victim = town.Households[0].Residents[0];
            incident = store.RecordViolence(1, victim.Id, 100);
            store.LocateViolence(incident.Id, 7, 1, 2, 3);
            Population.MarkDead(town, victim.Id);
            store.RegisterUnwitnessedScene(incident.Id);
            return store;
        }
        private static void Dispatch(WorldStateStore store, CivilianIncident alarm)
        {
            var roster = store.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
            var guard = roster.Guards[0];
            roster.CacheBuildingApproach(7, new SavedRoutePoint { X = 100 }, new Dictionary<string, List<SavedRoutePoint>> {
                { guard.Id, new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 100 } } } });
            roster.DispatchBuilding("discovery:" + alarm.Id, 7, alarm.Scene.LeftAt, 1, 2, 10, true);
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Witnessed murder keeps accusation policy while allowing corpse inspection",()=>{
                CivilianIncident alarm; var store=Scene(out alarm); alarm.Scene=null;
                store.ConfirmCorpse(alarm.Id,100,7,1,2,3);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()}); var guard=roster.Guards[0];
                roster.CacheBuildingApproach(7,new SavedRoutePoint{X=10},new Dictionary<string,List<SavedRoutePoint>> {
                    {guard.Id,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=10}}} });
                string id="civilian:"+alarm.Id;
                roster.DispatchBuilding(id,7,110,1,2,10,false,null,null,true);
                var report=roster.Reports.Find(r=>r.Id==id);
                Check.True(report.CrimeScene && !report.Investigation);
                roster.Advance(161); roster.EnterBuilding(guard.Id,7,161,new GuardPoint3());
                Check.True(store.AssignCorpseInspector(alarm.Id,id,161));
                roster.ObserveInterior(guard.Id,new GuardPoint3(),1,161,true,new GuardPoint3{X=2});
                Check.True(!roster.InteriorPursuitExpired(guard.Id,460));
                Check.True(roster.InteriorPursuitExpired(guard.Id,461));
                Check.Near(3760,guard.SearchUntil);
                store.AdvanceCrimeSceneInspection(alarm.Id,guard.Id,5,false,true,true,162);
                Check.True(!alarm.Corpse.Cleared);
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.GetGuards(1).InteriorPursuitExpired(guard.Id,461));
                restored.AdvanceCrimeSceneInspection(alarm.Id,guard.Id,5,false,false,true,170);
                var saved=restored.Snapshot();
                Check.True(saved.AlarmIncidents[0].Corpse.Cleared);
                Check.True(!saved.Garrisons[0].Reports[0].Investigation);
                Check.Near(3760,saved.Garrisons[0].Reports[0].InvestigationUntil);
                var savedRoster=restored.GetGuards(1);
                savedRoster.FinishInteriorPursuit(guard.Id,461);
                Check.True(!savedRoster.InteriorPursuitExpired(guard.Id,500));
                savedRoster.ObserveInterior(guard.Id,new GuardPoint3(),1,500,true,new GuardPoint3{X=3});
                Check.True(!savedRoster.InteriorPursuitExpired(guard.Id,799));
                Check.True(savedRoster.InteriorPursuitExpired(guard.Id,800));
                var invalid=restored.Snapshot(); invalid.Garrisons[0].Guards[0].Interior.PursuitUntil=double.NaN;
                Check.Throws<ArgumentException>(()=>restored.Restore(invalid));
            }));
            cases.Add(("Neutral corpse report assigns an arrived inspector and preserves cleanup across restore", () => {
                CivilianIncident alarm; var store=Scene(out alarm); alarm.Scene=null;
                store.ConfirmCorpse(alarm.Id,100,7,1,2,3);
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});
                var guard=roster.Guards[0];
                roster.CacheBuildingApproach(7,new SavedRoutePoint{X=10},new Dictionary<string,List<SavedRoutePoint>> {
                    {guard.Id,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=10}}} });
                string report="inn-alarm:"+alarm.Id;
                roster.DispatchBuilding(report,7,110,1,2,10,true);
                Check.True(!store.AssignCorpseInspector(alarm.Id,report,111));
                roster.Advance(161); roster.EnterBuilding(guard.Id,7,161,new GuardPoint3());
                Check.True(store.AssignCorpseInspector(alarm.Id,report,161));
                store.AdvanceCrimeSceneInspection(alarm.Id,guard.Id,2,false,false,true,162);
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Near(3,restored.GetCrimeSceneCorpseSnapshot(alarm.Id).InspectionRemainingRealSeconds);
                restored.AdvanceCrimeSceneInspection(alarm.Id,guard.Id,3,false,false,true,165);
                var saved=restored.Snapshot();
                Check.True(saved.AlarmIncidents[0].Corpse.Cleared);
                var again=new WorldStateStore(0); again.Restore(saved);
                Check.Equal(0,again.ObserveCorpses(1,7,166).Count);
            }));
            cases.Add(("Dead corpse inspector is replaced by an arrived responder without extending occupation", () => {
                CivilianIncident alarm; var store=Scene(out alarm); alarm.Scene=null;
                store.ConfirmCorpse(alarm.Id,100,7,1,2,3);
                var posts=new[]{new SavedRoutePoint(),new SavedRoutePoint{X=2}};
                var roster=store.GetOrCreateGuards(1,2,posts);
                var routes=new Dictionary<string,List<SavedRoutePoint>>();
                for(int i=0;i<2;i++) routes[roster.Guards[i].Id]=new List<SavedRoutePoint>{posts[i],new SavedRoutePoint{X=10}};
                roster.CacheBuildingApproach(7,new SavedRoutePoint{X=10},routes);
                string id="inn-alarm:"+alarm.Id;
                roster.DispatchBuilding(id,7,110,2,2,10,true); roster.Advance(161);
                foreach(var guard in roster.Guards) roster.EnterBuilding(guard.Id,7,161,new GuardPoint3());
                Check.True(store.AssignCorpseInspector(alarm.Id,id,161));
                string first=alarm.Corpse.InspectionGuardId;
                double deadline=roster.Reports[0].InvestigationUntil;
                store.AdvanceCrimeSceneInspection(alarm.Id,first,2,false,false,true,162);
                Check.True(!store.AssignCorpseInspector(alarm.Id,id,162));
                Check.Near(3,alarm.Corpse.InspectionRemainingRealSeconds);
                roster.Kill(first,163);
                var abandoned=new WorldStateStore(0); abandoned.Restore(store.Snapshot());
                var other=abandoned.GetGuards(1).Guards.Find(g=>g.Id!=first);
                abandoned.GetGuards(1).Kill(other.Id,163);
                Check.True(!abandoned.AssignCorpseInspector(alarm.Id,id,164));
                Check.True(!abandoned.GetCrimeSceneCorpseSnapshot(alarm.Id).Cleared);
                var expired=new WorldStateStore(0); expired.Restore(store.Snapshot());
                Check.True(!expired.AssignCorpseInspector(alarm.Id,id,deadline+1));
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.AssignCorpseInspector(alarm.Id,id,164));
                var body=restored.GetCrimeSceneCorpseSnapshot(alarm.Id);
                Check.True(body.InspectionGuardId!=first);
                Check.Near(5,body.InspectionRemainingRealSeconds);
                Check.Near(deadline,restored.GetGuards(1).Reports[0].InvestigationUntil);
                restored.AdvanceCrimeSceneInspection(alarm.Id,body.InspectionGuardId,5,false,false,true,169);
                Check.True(restored.GetCrimeSceneCorpseSnapshot(alarm.Id).Cleared);
                Check.True(!restored.AssignCorpseInspector(alarm.Id,id,170));
            }));
            cases.Add(("Investigator clears indoor corpse only after five unpaused seconds at body", () => {
                CivilianIncident alarm; var store = Scene(out alarm);
                store.ConfirmCorpse(alarm.Id, 100);
                var readOnlyBody = store.GetCrimeSceneCorpseSnapshot(alarm.Id);
                readOnlyBody.InspectionRemainingRealSeconds = 0;
                Check.Near(5, alarm.Corpse.InspectionRemainingRealSeconds);
                Check.Equal(1, store.ObserveCorpses(1, 7, 200000).Count);
                store.ObserveCrimeScenes(1, -1, 200001); Dispatch(store, alarm);
                Check.Equal(1, store.ObserveCorpses(1, -1, 200002).Count);
                store.DiscoverCrimeScene(alarm.Id, 200501);
                Check.Equal(1, store.ObserveCorpses(1, -1, 200501).Count);
                var roster = store.GetGuards(1); var guard = roster.Guards[0];
                Check.Equal(guard.Id, alarm.Corpse.InspectionGuardId);
                roster.EnterBuilding(guard.Id, 7, 200501, new GuardPoint3());
                store.AdvanceCrimeSceneInspection(alarm.Id, guard.Id, 2, true, false, true, 200502);
                Check.Near(5, alarm.Corpse.InspectionRemainingRealSeconds);
                store.AdvanceCrimeSceneInspection(alarm.Id, guard.Id, 2, false, true, true, 200503);
                Check.Near(5, alarm.Corpse.InspectionRemainingRealSeconds);
                store.AdvanceCrimeSceneInspection(alarm.Id, guard.Id, 2, false, false, true, 200504);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                var restoredBody = restored.Snapshot().AlarmIncidents[0].Corpse;
                Check.Near(3, restoredBody.InspectionRemainingRealSeconds);
                var restoredGuard = restored.GetGuards(1).Guards[0];
                restored.AdvanceCrimeSceneInspection(alarm.Id, restoredGuard.Id, 2, false, false, true, 200600);
                Check.Equal(1, restored.ObserveCorpses(1, 7, 200600).Count);
                restored.AdvanceCrimeSceneInspection(alarm.Id, restoredGuard.Id, 1, false, false, true, 200601);
                Check.Equal(0, restored.ObserveCorpses(1, 7, 200601).Count);
                Check.True(restored.Snapshot().AlarmIncidents[0].Corpse.Cleared);
            }));
            cases.Add(("Corpse position follows death rather than the first assault", () => {
                CivilianIncident alarm; var store = Scene(out alarm); alarm.Scene = null;
                store.ConfirmCorpse(alarm.Id, 101, 8, 12, 2, 14);
                var copy = new WorldStateStore(0); copy.Restore(store.Snapshot());
                var body = copy.Snapshot().AlarmIncidents[0].Corpse;
                Check.True(body.HasPosition); Check.Equal(8, body.BuildingKey); Check.Near(12, body.X);
                Check.Equal(1, copy.ObserveCorpses(1, 8, 86502).Count);
                Check.Equal(0, copy.ObserveCorpses(1, 7, 86502).Count);
                var invalid = store.Snapshot(); invalid.AlarmIncidents[0].Corpse.X = double.NaN;
                Check.Throws<ArgumentException>(() => store.Restore(invalid));
                Check.Near(12, store.Snapshot().AlarmIncidents[0].Corpse.X);
            }));
            cases.Add(("Ordinary corpses require departure and a day while legacy deaths stay absent", () => {
                CivilianIncident alarm; var store = Scene(out alarm); alarm.Scene = null;
                Check.Equal(0, store.ObserveCorpses(1, 7, 100).Count);
                store.ConfirmCorpse(alarm.Id, 100);
                Check.Equal(1, store.ObserveCorpses(1, -1, 101).Count);
                Check.Equal(1, store.ObserveCorpses(1, 7, 86500).Count);
                Check.Equal(0, store.ObserveCorpses(1, -1, 86501).Count);
                var broken = store.Snapshot(); broken.AlarmIncidents[0].Corpse.ClearedAt = 99;
                Check.Throws<ArgumentException>(() => store.Restore(broken));
                Check.Near(86501, store.Snapshot().AlarmIncidents[0].Corpse.ClearedAt);
                var early = store.Snapshot(); early.AlarmIncidents[0].Corpse.ClearedAt = 101;
                Check.Throws<ArgumentException>(() => store.Restore(early));
                CivilianIncident outside; var exterior = Scene(out outside); outside.Scene = null; outside.BuildingKey = -1;
                exterior.ConfirmCorpse(outside.Id, 100);
                Check.Equal(1, exterior.ObserveCorpses(1, null, 86501).Count);
                Check.Equal(1, exterior.ObserveCorpses(1, -1, 86501).Count);
            }));
            cases.Add(("Unwitnessed scene waits for departure and occupation starts at guard arrival", () => {
                CivilianIncident alarm; var store = Scene(out alarm);
                Check.Equal(0, store.ObserveCrimeScenes(1, null, 9999).Count);
                Check.Equal(0, store.ObserveCrimeScenes(1, 7, 10000).Count);
                Check.True(!alarm.Scene.PlayerLeft && !alarm.Scene.Discovered);
                Check.Equal(1, store.ObserveCrimeScenes(1, -1, 10001).Count);
                Check.Near(10001, alarm.Scene.LeftAt);
                Check.Equal(1, store.ObserveCrimeScenes(1, -1, 10100).Count);
                Check.Near(10001, alarm.Scene.LeftAt);
                Check.Throws<ArgumentException>(() => store.DiscoverCrimeScene(alarm.Id, 10500));
                Dispatch(store, alarm);
                var early = store.Snapshot();
                early.Garrisons[0].Reports[0].HasArrival = true; early.Garrisons[0].Reports[0].FirstArrivalAt = alarm.Scene.LeftAt;
                early.AlarmIncidents[0].Scene.Discovered = true; early.AlarmIncidents[0].Scene.DiscoveredAt = alarm.Scene.LeftAt;
                Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(early));
                early.Garrisons[0].Reports[0].Arrivals[0].Arrived = true;
                early.Garrisons[0].Reports[0].Arrivals[0].ScheduledAt = alarm.Scene.LeftAt;
                Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(early));
                Check.Throws<ArgumentException>(() => store.DiscoverCrimeScene(alarm.Id, 10500));
                store.DiscoverCrimeScene(alarm.Id, 11000);
                Check.Near(10501, alarm.Scene.DiscoveredAt);
                Check.True(alarm.Scene.Occupied(10501)); Check.True(alarm.Scene.Occupied(14100));
                Check.True(!alarm.Scene.Occupied(14101));
                store.DiscoverCrimeScene(alarm.Id, 12000); Check.Near(10501, alarm.Scene.DiscoveredAt);
                Check.Equal(0, store.ObserveCrimeScenes(1, -1, 12000).Count);
            }));
            cases.Add(("Scene survives reload and rejects discovery before departure transactionally", () => {
                CivilianIncident alarm; var store = Scene(out alarm);
                Check.Throws<ArgumentException>(() => store.DiscoverCrimeScene(alarm.Id, 200));
                store.ObserveCrimeScenes(1, -1, 500);
                Check.Throws<ArgumentException>(() => store.DiscoverCrimeScene(alarm.Id, 499));
                Dispatch(store, alarm); store.DiscoverCrimeScene(alarm.Id, 1000);
                var copy = new WorldStateStore(0); copy.Restore(store.Snapshot());
                var snapshot = copy.Snapshot();
                Check.True(snapshot.AlarmIncidents[0].Scene.Occupied(1000));
                snapshot.AlarmIncidents[0].Scene.DiscoveredAt = 499;
                Check.Throws<ArgumentException>(() => copy.Restore(snapshot));
                Check.Near(1000, copy.Snapshot().AlarmIncidents[0].Scene.DiscoveredAt);
                var missingArrival = copy.Snapshot(); missingArrival.Garrisons[0].Reports[0].HasArrival = false;
                missingArrival.Garrisons[0].Reports[0].FirstArrivalAt = 0;
                Check.Throws<ArgumentException>(() => copy.Restore(missingArrival));
            }));
            cases.Add(("Dead en-route guards cannot discover but completed arrivals survive late catch-up", () => {
                CivilianIncident alarm; var store = Scene(out alarm);
                store.ObserveCrimeScenes(1, -1, 200); Dispatch(store, alarm);
                var roster = store.GetGuards(1); roster.Kill(roster.Guards[0].Id, 300);
                Check.Throws<ArgumentException>(() => store.DiscoverCrimeScene(alarm.Id, 1000));
                CivilianIncident other; var late = Scene(out other);
                late.ObserveCrimeScenes(1, -1, 200); Dispatch(late, other);
                late.GetGuards(1).Advance(50000);
                var restored = new WorldStateStore(0); restored.Restore(late.Snapshot());
                restored.DiscoverCrimeScene(other.Id, 50000);
                Check.Near(700, restored.Snapshot().AlarmIncidents[0].Scene.DiscoveredAt);
                late.DiscoverCrimeScene(other.Id, 50000);
                Check.Near(700, other.Scene.DiscoveredAt); Check.True(!other.Scene.Occupied(50000));
            }));
            cases.Add(("Only confirmed unwitnessed home deaths can create a scene", () => {
                var store = new WorldStateStore(1); var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
                var victim = town.Households[0].Residents[0]; var witness = town.Households[1].Residents[0];
                var alarm = store.RecordViolence(1, victim.Id, 100); store.LocateViolence(alarm.Id, 7, 0, 0, 0);
                Check.Throws<ArgumentException>(() => store.RegisterUnwitnessedScene(alarm.Id));
                store.Witness(alarm.Id, witness.Id, 100); Population.MarkDead(town, victim.Id);
                Check.Throws<ArgumentException>(() => store.RegisterUnwitnessedScene(alarm.Id));
                Check.Equal(0, store.ObserveCrimeScenes(1, -1, 500).Count);
            }));
            cases.Add(("Investigation guards occupy for one hour without extending repeated reports", () => {
                var roster = GuardRoster.Create(1, 1, 1, new[] { new SavedRoutePoint() });
                var guard = roster.Guards[0];
                roster.CacheBuildingApproach(7, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { guard.Id, new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 100 } } } });
                roster.DispatchBuilding("discovery", 7, 100, 1, 2, 10, true);
                Check.Near(600, guard.ArrivesAt); Check.Near(4200, guard.SearchUntil);
                var legacy = GuardRoster.CopyValidated(roster);
                legacy.Guards[0].SearchUntil = legacy.Guards[0].ArrivesAt + 21600;
                Check.Near(4200, GuardRoster.CopyValidated(legacy).Guards[0].SearchUntil);
                Check.Near(22200, legacy.Guards[0].SearchUntil);
                var copy = GuardRoster.CopyValidated(roster);
                copy.DispatchBuilding("discovery", 7, 1000, 1, 2, 10, true);
                Check.Near(4200, copy.Guards[0].SearchUntil);
                Check.Throws<ArgumentException>(() => copy.DispatchBuilding("discovery", 7, 1000, 1, 2, 10));
                copy.Advance(4199); Check.Equal(GuardDuty.Searching, copy.Guards[0].Duty);
                copy.Advance(4200); Check.Equal(GuardDuty.Returning, copy.Guards[0].Duty);
                copy.Advance(4700); Check.Equal(GuardDuty.Posted, copy.Guards[0].Duty);
                roster.EnterBuilding(guard.Id, 7, 600, new GuardPoint3());
                roster.ObserveInterior(guard.Id, new GuardPoint3(), 1, 601, true, new GuardPoint3 { X = 2 });
                Check.Near(4200, guard.SearchUntil);
                Check.Near(4200, GuardRoster.CopyValidated(roster).Guards[0].SearchUntil);
            }));
            cases.Add(("All scene responders leave at first arrival occupation deadline", () => {
                var posts = new[] { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } };
                var roster = GuardRoster.Create(1, 2, 2, posts);
                var routes = new Dictionary<string, List<SavedRoutePoint>>();
                routes[roster.Guards[0].Id] = new List<SavedRoutePoint> { posts[0], new SavedRoutePoint { X = 100 } };
                routes[roster.Guards[1].Id] = new List<SavedRoutePoint> { posts[1], new SavedRoutePoint { X = 100 } };
                roster.CacheBuildingApproach(7, new SavedRoutePoint { X = 100 }, routes);
                roster.DispatchBuilding("discovery:multi", 7, 100, 2, 2, 10, true);
                double first = Math.Min(roster.Guards[0].ArrivesAt, roster.Guards[1].ArrivesAt);
                Check.Near(first + UnwitnessedScene.OccupationSeconds, roster.Reports[0].InvestigationUntil);
                Check.Near(roster.Reports[0].InvestigationUntil, roster.Guards[0].SearchUntil);
                Check.Near(roster.Reports[0].InvestigationUntil, roster.Guards[1].SearchUntil);
                var restored = GuardRoster.CopyValidated(roster);
                Check.Near(roster.Reports[0].InvestigationUntil, restored.Reports[0].InvestigationUntil);
                Check.Near(roster.Reports[0].InvestigationUntil, restored.Guards[1].SearchUntil);
            }));
        }
    }
}
