using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class GuardSearchRecruitmentTests
    {
        private static WorldStateStore Create(out CivilianIncident alarm,out GuardRecord caller,
            int guards=8,IList<SavedRoutePoint> posts=null,bool departure=false,int initial=1,double clearAt=200)
        {
            var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8},1);
            var victim=town.Households[0].Residents[0];alarm=store.RecordViolence(1,victim.Id,100);
            store.LocateViolence(alarm.Id,-1,0,0,0);Population.MarkDead(town,victim.Id);
            store.ConfirmCorpse(alarm.Id,100,-1,0,0,0);
            if(departure)store.ObserveOutdoorCorpse(alarm.Id,false,150);
            if(posts==null){posts=new List<SavedRoutePoint>();for(int i=0;i<guards;i++)posts.Add(new SavedRoutePoint{X=i*5});}
            var roster=store.GetOrCreateGuards(1,guards,posts);var routes=new Dictionary<string,List<SavedRoutePoint>>();
            foreach(var guard in roster.Guards)routes.Add(guard.Id,new List<SavedRoutePoint>{guard.Post,new SavedRoutePoint()});
            var sent=roster.DispatchOutdoorInvestigation("outdoor:"+alarm.Id,200,routes,
                new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=20},new SavedRoutePoint()},initial,1,1);
            caller=sent[0];roster.Advance(200);
            if(departure)roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,150,200,
                new Dictionary<string,List<SavedRoutePoint>>(),roster.Reports[0].SearchRoute,1,1);
            roster.Advance(clearAt);
            Check.True(store.ClearOutdoorCorpse(alarm.Id,caller.Id,0,0,clearAt));return store;
        }

        private static bool Join(GuardRoster roster,string caller,string candidate,double now,
            bool visible=true,double? suppliedDistance=null,List<SavedRoutePoint> join=null,List<SavedRoutePoint> entry=null)
        {
            var from=roster.Position(candidate,now);var contact=roster.Position(caller,now);
            var source=roster.Guards.Find(row=>row.Id==caller);
            var report=roster.Reports.Find(row=>row.Id==source.IncidentId);
            if(report==null)report=roster.Reports[0];
            double dx=from.X-contact.X,dz=from.Z-contact.Z;
            GuardRecord accepted;
            return roster.TryRecruitCorpseSearchGuard(caller,candidate,now,suppliedDistance??Math.Sqrt(dx*dx+dz*dz),visible,
                join??new List<SavedRoutePoint>{from,contact},
                entry??new List<SavedRoutePoint>{contact,report.Entrance},1,1,out accepted);
        }

        public static void Add(List<(string,Action)> cases)
        {
            cases.Add(("An engaged guard retains native last-seen pursuit after the witness search expires",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller);var roster=store.GetGuards(1);
                roster.Engage(caller.Id,1900);Check.Near(2000,roster.Reports[0].InvestigationUntil);
                Check.True(!roster.CorpseResponseExpired(caller.Id,2100));
                roster.ObserveEngagement(caller.Id,new SavedRoutePoint(),1,2100,true,new SavedRoutePoint{X=5});
                Check.True(!roster.CorpseResponseExpired(caller.Id,2399));
                Check.True(roster.CorpseResponseExpired(caller.Id,2400));
                Check.Near(2000,roster.Reports[0].InvestigationUntil);
                Check.True(!roster.CanRecruitCorpseSearchGuard(caller.Id,roster.Guards[1].Id,2100,5,true));
                store.Restore(store.Snapshot());Check.True(store.GetGuards(1).CorpseResponseExpired(caller.Id,2400));
            })));
            cases.Add(("Leaving a long-running corpse inquiry cannot install a retroactive departure deadline",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller,1,
                    new[]{new SavedRoutePoint{X=4500}},clearAt:4700);
                var roster=store.GetGuards(1);var report=roster.Reports[0];double until=report.InvestigationUntil;
                roster.DispatchDepartureInvestigation(report.Id,4800,4800,new Dictionary<string,List<SavedRoutePoint>>(),
                    report.SearchRoute,1,1);
                Check.True(!report.DepartureInvestigation,"an impossible return budget cannot replace the live inquiry");
                Check.Near(until,report.InvestigationUntil);store.Restore(store.Snapshot());
            })));
            cases.Add(("Late corpse cleanup cannot recruit beyond the original case return horizon",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller,clearAt:3600);
                var roster=store.GetGuards(1);var report=roster.Reports[0];var candidate=roster.Guards[1];
                Check.Near(3800,report.InvestigationUntil);
                Check.True(!Join(roster,caller.Id,candidate.Id,3600),"late cleanup leaves no return reserve beyond the original hour");
                Check.Equal(1,report.GuardIds.Count);Check.Equal(GuardDuty.Posted,candidate.Duty);
                store.Restore(store.Snapshot());
            })));
            cases.Add(("Legacy schema64 small cases do not silently acquire recruitment behavior",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller);
                var legacy=store.Snapshot();legacy.Version=64;legacy.Garrisons[0].Reports[0].CorpseSearchVersion=0;
                var restored=new WorldStateStore(0);restored.Restore(legacy);var roster=restored.GetGuards(1);
                Check.True(!roster.CanRecruitCorpseSearchGuard(caller.Id,roster.Guards[1].Id,200,5,true));
                Check.True(!Join(roster,caller.Id,roster.Guards[1].Id,200));
                Check.Equal(1,roster.Reports[0].GuardIds.Count);Check.Equal(0,roster.Reports[0].CorpseSearchVersion);
                restored.Restore(restored.Snapshot());
            })));
            cases.Add(("Corpse search chains share one case and six total participants without renewing its deadline",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller);var roster=store.GetGuards(1);
                var ids=new List<string>();foreach(var guard in roster.Guards)ids.Add(guard.Id);
                string previous=caller.Id;double deadline=roster.Reports[0].InvestigationUntil;
                for(int i=1;i<6;i++)
                {
                    Check.True(Join(roster,previous,ids[i],200+i));
                    var recruited=roster.Guards.Find(row=>row.Id==ids[i]);
                    Check.Equal(caller.IncidentId,recruited.IncidentId);Check.Near(deadline,recruited.SearchUntil);
                    Check.True(recruited.Duty!=GuardDuty.Engaged && recruited.LastSeenSuspect==null,
                        "contact does not invent a suspect sighting or start combat");previous=ids[i];
                }
                Check.True(!Join(roster,previous,ids[6],207));
                Check.Equal(6,roster.Reports[0].GuardIds.Count);Check.Equal(1,roster.Reports.Count);Check.Equal(8,roster.Guards.Count);
                Check.Near(deadline,roster.Reports[0].InvestigationUntil);
                store.Restore(store.Snapshot());roster=store.GetGuards(1);
                Check.Equal(6,roster.Reports[0].GuardIds.Count);Check.Near(deadline,roster.Reports[0].InvestigationUntil);
                Check.True(!roster.KnowsPlayerCrime(ids[1]),"a corpse search does not identify the player as murderer");
            })));
            cases.Add(("New corpse cases cap their first dispatch at six",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller,initial:8);
                Check.Equal(6,store.GetGuards(1).Reports[0].GuardIds.Count);store.Restore(store.Snapshot());
            })));
            cases.Add(("Search recruitment requires real visible contact within thirty-five metres",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller,3,
                    new[]{new SavedRoutePoint(),new SavedRoutePoint{X=34.9},new SavedRoutePoint{X=35.1}});
                var roster=store.GetGuards(1);var inside=roster.Guards[1];var outside=roster.Guards[2];
                Check.True(!Join(roster,caller.Id,inside.Id,200,visible:false));
                Check.True(!Join(roster,caller.Id,outside.Id,200));
                Check.True(!Join(roster,caller.Id,outside.Id,200,suppliedDistance:1));
                Check.True(Join(roster,caller.Id,inside.Id,200));
            })));
            cases.Add(("A recruited search guard walks through contact to the patrol entry without teleporting",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller);var roster=store.GetGuards(1);
                var candidate=roster.Guards[2];double now=205;var start=roster.Position(candidate.Id,now);
                Check.True(Join(roster,caller.Id,candidate.Id,now));double arrival=candidate.ArrivesAt;
                Check.Near(start.X,roster.Position(candidate.Id,now).X);
                var middle=roster.Position(candidate.Id,(now+arrival)/2);Check.True(middle.X>0 && middle.X<start.X);
                var before=roster.Position(candidate.Id,arrival-0.000001);var after=roster.Position(candidate.Id,arrival);
                Check.Near(before.X,after.X,0.001);Check.Near(before.Z,after.Z,0.001);
                store.Restore(store.Snapshot());roster=store.GetGuards(1);roster.Advance(2300);
                Check.Equal(GuardDuty.Posted,roster.Guards.Find(row=>row.Id==candidate.Id).Duty);
            })));
            cases.Add(("Malformed or late search join routes leave membership and candidate unchanged",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller);var roster=store.GetGuards(1);
                var candidate=roster.Guards[1];var from=roster.Position(candidate.Id,200);var contact=roster.Position(caller.Id,200);
                Check.True(!Join(roster,caller.Id,candidate.Id,200,join:new List<SavedRoutePoint>{new SavedRoutePoint{X=100},contact}));
                Check.True(!Join(roster,caller.Id,candidate.Id,200,entry:new List<SavedRoutePoint>{contact,new SavedRoutePoint{X=99}}));
                Check.True(!Join(roster,caller.Id,candidate.Id,200,join:new List<SavedRoutePoint>{from,new SavedRoutePoint{X=2000},contact}));
                Check.Equal(GuardDuty.Posted,candidate.Duty);Check.Equal(1,roster.Reports[0].GuardIds.Count);
                Check.True(!Join(roster,caller.Id,candidate.Id,2000));
            })));
            cases.Add(("Search join keeps the original deadline and reserves the recruit's return",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller,departure:true);var roster=store.GetGuards(1);
                var candidate=roster.Guards[6];double deadline=roster.Reports[0].InvestigationUntil;
                var from=roster.Position(candidate.Id,200);var contact=roster.Position(caller.Id,200);
                Check.True(!Join(roster,caller.Id,candidate.Id,200,
                    join:new List<SavedRoutePoint>{from,new SavedRoutePoint{X=885},contact}),
                    "arrival alone is insufficient when return would exceed the one-hour case bound");
                Check.True(Join(roster,caller.Id,candidate.Id,200,
                    join:new List<SavedRoutePoint>{from,new SavedRoutePoint{X=200},contact}));
                Check.Near(deadline,roster.Reports[0].InvestigationUntil);
                Check.True(roster.Reports[0].DepartureReturnBudget>40);
                store.Restore(store.Snapshot());store.GetGuards(1).Advance(3750);
                foreach(var guard in store.GetGuards(1).Guards)Check.Equal(GuardDuty.Posted,guard.Duty);
            })));
            cases.Add(("Dead or occupied guards cannot join or relay a corpse search",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller);var roster=store.GetGuards(1);
                Check.True(!Join(roster,caller.Id,caller.Id,200));
                var dead=roster.Guards[1];roster.Kill(dead.Id,200);Check.True(!Join(roster,caller.Id,dead.Id,200));
                var busy=roster.Guards[2];roster.Dispatch("busy",200,new Dictionary<string,List<SavedRoutePoint>>{
                    {busy.Id,new List<SavedRoutePoint>{busy.Post,new SavedRoutePoint{X=11}}}},1,1,1);
                Check.True(!Join(roster,caller.Id,busy.Id,200));
                var free=roster.Guards[3];roster.Kill(caller.Id,200);Check.True(!Join(roster,caller.Id,free.Id,200));
                Check.Equal(1,roster.Reports[0].GuardIds.Count);store.Restore(store.Snapshot());
            })));
            cases.Add(("Search route inputs are detached and future case fields cannot hide under schema64",(Action)(()=>{
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller);var roster=store.GetGuards(1);
                var candidate=roster.Guards[1];var path=new List<SavedRoutePoint>{new SavedRoutePoint{X=5},new SavedRoutePoint()};
                var entry=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint()};
                Check.True(Join(roster,caller.Id,candidate.Id,200,join:path,entry:entry));
                path[0].X=99;entry[0].X=99;Check.Near(5,candidate.Route[0].X);
                var old=store.Snapshot();old.Version=64;Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(old));
            })));
            cases.Add(("Legacy schema64 cases keep their original members without recruiting beyond six",(Action)(()=>{
                var posts=new List<SavedRoutePoint>();for(int i=0;i<9;i++)posts.Add(new SavedRoutePoint{X=i*3});
                CivilianIncident alarm;GuardRecord caller;var store=Create(out alarm,out caller,9,posts,initial:8);
                var legacy=store.Snapshot();legacy.Version=64;var report=legacy.Garrisons[0].Reports[0];report.CorpseSearchVersion=0;
                for(int i=6;i<8;i++)
                {
                    var guard=legacy.Garrisons[0].Guards[i];
                    guard.IncidentId=report.Id;guard.Route=new List<SavedRoutePoint>{guard.Post,new SavedRoutePoint()};
                    guard.ResponseOrigin=new SavedRoutePoint{X=guard.Post.X};
                    guard.ReturnPath=new List<SavedRoutePoint>{new SavedRoutePoint(),guard.Post};guard.ReturnRate=1;
                    guard.DepartedAt=200;guard.ArrivesAt=200+guard.Post.X;guard.SearchUntil=2000;guard.Duty=GuardDuty.Responding;
                    report.GuardIds.Add(guard.Id);report.Arrivals.Add(new GuardArrivalEvidence{GuardId=guard.Id,ScheduledAt=guard.ArrivesAt});
                }
                var restored=new WorldStateStore(0);restored.Restore(legacy);var roster=restored.GetGuards(1);
                Check.Equal(8,roster.Reports[0].GuardIds.Count);Check.Equal(0,roster.Reports[0].CorpseSearchVersion);
                Check.True(!Join(roster,caller.Id,roster.Guards[8].Id,200));restored.Restore(restored.Snapshot());
                Check.Equal(8,restored.GetGuards(1).Reports[0].GuardIds.Count);
            })));
        }
    }
}
