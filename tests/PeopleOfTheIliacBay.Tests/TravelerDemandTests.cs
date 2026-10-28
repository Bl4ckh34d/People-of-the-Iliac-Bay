using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class TravelerDemandTests
    {
        private static readonly TravelerOrigin[] Origins={ new TravelerOrigin { SettlementId=9,Race=1 },new TravelerOrigin { SettlementId=3,Race=2 } };
        private static TravelerProposal Make(int day=100,int slot=0,bool walled=true,TravelerOrigin[] origins=null)
        { return TravelerDemand.Create(42,1,7,day,slot,origins??Origins,1,4,walled,
            new[] { new RoutePoint(0,0),new RoutePoint(130,0) },12/1.3,
            (origin,seed,sex)=>origin.SettlementId+"-"+seed+"-"+sex); }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Traveler self knowledge distinguishes pending booked and expired Inn beds without inventing a house",(Action)(()=>{
                var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
                store.RegisterLodgingBeds(1,7,new[]{new LodgingBed {Id="bed"}});
                TravelerProposal proposal=null;
                for(int slot=0;slot<1000;slot++)
                {proposal=Make(slot:slot);if(TravelerAdventurerPolicy.Archetype(proposal.Person.AppearanceSeed)==NpcArchetype.Mage)break;}
                store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);
                var journey=store.TravelerJourneyFor(proposal.Visit.Id);
                string pending=TravelerConversationContext.IdentityAndLodging(store,proposal.Person.Id,1,journey.CheckInAt-1,
                    key=>"Actual Origin",key=>"Actual Inn");
                Check.True(pending.Contains("mage traveler") && pending.Contains("Actual Origin") && pending.Contains("no bed booking is confirmed"));
                Check.True(!pending.Contains("Your booked bed"));
                Check.True(store.TryAdmitTraveler(proposal.Visit.Id,journey.CheckInAt));
                string booked=TravelerConversationContext.IdentityAndLodging(store,proposal.Person.Id,1,journey.CheckInAt+1,
                    key=>"Actual Origin",key=>"Actual Inn");
                Check.True(booked.Contains("Your booked bed is at Actual Inn"));
                Check.True(booked.Contains("No permanent house or household is verified"));
                string expired=TravelerConversationContext.IdentityAndLodging(store,proposal.Person.Id,1,journey.CheckOutAt+1,
                    key=>"Actual Origin",key=>"Actual Inn");
                Check.True(expired.Contains("earlier bed booking at Actual Inn has ended") && !expired.Contains("Your booked bed"));
                string departed=TravelerConversationContext.PlannedActivity(store,proposal.Person.Id,1,journey.ExitAt+1,key=>"Actual Inn");
                Check.True(departed.Contains("No onward settlement is recorded") && !departed.Contains("return home"));
                Check.True(store.ReadNpcProgress(proposal.Person.Id)==null);Check.Equal(0,store.Snapshot().Settlements[0].Households.Count);
            })));
            cases.Add(("Traveler context leaves citizen identity and family context unchanged",(Action)(()=>{
                var store=new WorldStateStore(42);var resident=store.GetOrCreate(1,new[]{10},1).Households[0].Residents[0];
                Check.Equal("",TravelerConversationContext.RoleLabel(store,resident.Id));
                Check.Equal("",TravelerConversationContext.IdentityAndLodging(store,resident.Id,1,100,null,null));
                Check.Equal("",TravelerConversationContext.PlannedActivity(store,resident.Id,1,100,null));
            })));
            cases.Add(("An expired Inn visit retains a living Follow owner through execution and physical return",(Action)(()=>{
                var action=new NpcActionJob {Kind=NpcActionKind.Follow,Phase=NpcActionPhase.Executing};
                Check.True(TravelerActionRoutine.KeepsVisitAlive(true,action,false));
                action.Phase=NpcActionPhase.Cancelled;action.ReturningToRoutine=true;
                Check.True(TravelerActionRoutine.KeepsVisitAlive(true,action,false));
                action.ReturningToRoutine=false;
                Check.True(!TravelerActionRoutine.KeepsVisitAlive(true,action,false));
                Check.True(TravelerActionRoutine.KeepsVisitAlive(true,null,true));
                Check.True(!TravelerActionRoutine.KeepsVisitAlive(false,action,true));
            })));
            cases.Add(("Traveler return targets the actual Inn only until the stay or gate wait ends",(Action)(()=>{
                var proposal=Make();var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
                store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);
                var journey=store.TravelerJourneyFor(proposal.Visit.Id);
                Check.True(TravelerActionRoutine.ReturnsToInn(TravelerAdmission.Pending,journey,journey.CheckInAt));
                Check.True(TravelerActionRoutine.ReturnsToInn(TravelerAdmission.Admitted,journey,journey.CheckInAt+1));
                Check.True(!TravelerActionRoutine.ReturnsToInn(TravelerAdmission.Admitted,journey,journey.LeaveInnAt));
                Check.True(!TravelerActionRoutine.ReturnsToInn(TravelerAdmission.Pending,journey,journey.ExitAt));
                Check.True(!TravelerActionRoutine.ReturnsToInn(TravelerAdmission.Refused,journey,journey.CheckInAt+1));
            })));
            cases.Add(("A generated traveler's accepted Follow survives Inn departure and restore until physical return releases it",(Action)(()=>{
                var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
                var proposal=Make();store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);
                var journey=store.TravelerJourneyFor(proposal.Visit.Id);double accepted=journey.ExitAt-100;
                var action=new NpcActionJob {Id="traveler-expiry",ProposalId="traveler-expiry",ResidentId=proposal.Person.Id,
                    Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                    Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=accepted,UpdatedAt=accepted,
                    FinishAt=accepted+300,DeadlineAt=accepted+360,
                    LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=1,WorldSpaceId="settlement:1",X=3,Z=4}};
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,action,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(action.Id,1,accepted+1,NpcActionObservation.Arrival()));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());double after=journey.ExitAt+1;
                Check.Equal(0,restored.TravelerVisitsDuring(1,after,after+1).Count);
                var current=restored.ActiveNpcAction(proposal.Person.Id);
                Check.True(TravelerActionRoutine.KeepsVisitAlive(restored.Traveler(proposal.Person.Id).IsAlive,current,false));
                Check.True(restored.ObserveNpcAction(current.Id,current.Revision,after,NpcActionObservation.Cancelled("Player dismissal.")));
                current=restored.ReadNpcAction(current.Id);
                Check.True(restored.SaveNpcActionPosition(current.Id,current.Revision,current.LastPosition,true,after));
                var returning=new WorldStateStore(0);returning.Restore(restored.Snapshot());
                current=returning.ActiveNpcAction(proposal.Person.Id);
                Check.True(current!=null && current.ReturningToRoutine);
                Check.True(TravelerActionRoutine.KeepsVisitAlive(true,current,false));
                var border=proposal.Visit.Route[0];var arrived=NpcActionExecutor.CopyTarget(current.LastPosition);
                arrived.X=border.X;arrived.Z=border.Z;
                Check.True(returning.SaveNpcActionPosition(current.Id,current.Revision,arrived,false,after+100));
                Check.True(returning.ActiveNpcAction(proposal.Person.Id)==null);
                Check.Equal(-1,returning.ResolveResident(1,proposal.Person.Id).HomeKey);
            })));
            cases.Add(("Inn demand creates a stable ordinary majority and all three rare adventurer classes",(Action)(()=>{
                var counts=new Dictionary<NpcArchetype,int>();
                for(int index=0;index<1000;index++)
                {
                    int day=100+index/2,slot=index%2;
                    var proposal=Make(day,slot);
                    var archetype=TravelerAdventurerPolicy.Archetype(proposal.Person.AppearanceSeed);
                    Check.Equal(archetype,TravelerAdventurerPolicy.Archetype(Make(day,slot).Person.AppearanceSeed));
                    counts[archetype]=counts.TryGetValue(archetype,out var count)?count+1:1;
                }
                Check.True(counts[NpcArchetype.Citizen]>850 && counts[NpcArchetype.Citizen]<950);
                Check.True(counts[NpcArchetype.Warrior]>10 && counts[NpcArchetype.Mage]>10 && counts[NpcArchetype.Thief]>10);
                Check.Equal(4,counts.Count);
            })));
            cases.Add(("Traveler profile identity exists before injury or training and survives restore without a ledger row",(Action)(()=>{
                var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
                TravelerProposal proposal=null;
                for(int slot=0;slot<1000;slot++)
                {
                    proposal=Make(slot:slot);
                    if(TravelerAdventurerPolicy.IsAdventurer(TravelerAdventurerPolicy.Archetype(proposal.Person.AppearanceSeed)))break;
                }
                store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);
                var archetype=TravelerAdventurerPolicy.Archetype(proposal.Person.AppearanceSeed);
                Check.True(TravelerAdventurerPolicy.IsAdventurer(archetype));
                Check.Equal(archetype,store.ResolveNpcArchetype(proposal.Person.Id));
                Check.Equal(archetype,store.ReadNpcActor(proposal.Person.Id,store.ResolveNpcArchetype(proposal.Person.Id)).Profile.Archetype);
                Check.True(store.ReadNpcProgress(proposal.Person.Id)==null);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.Equal(archetype,restored.ResolveNpcArchetype(proposal.Person.Id));
                Check.True(restored.ReadNpcProgress(proposal.Person.Id)==null);
            })));
            cases.Add(("Traveler profile resolution preserves pinned identity and never turns resident families into adventurers",(Action)(()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10},1);
                foreach(var resident in town.Households[0].Residents)
                    Check.Equal(NpcArchetype.Citizen,store.ResolveNpcArchetype(resident.Id));
                var proposal=Make();store.RegisterTraveler(proposal.Person);
                Check.True(store.AwardNpcSkillExperience(proposal.Person.Id,NpcArchetype.Healer,0,1,0));
                Check.Equal(NpcArchetype.Healer,store.ResolveNpcArchetype(proposal.Person.Id));
                Check.Equal(NpcArchetype.Guard,store.ResolveNpcArchetype(town.Households[0].Residents[0].Id,NpcArchetype.Guard));
            })));
            cases.Add(("Generated visitor can accept player Follow through the existing managed action boundary",(Action)(()=>{
                var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);store.GetOrCreate(2,new int[0]);
                var proposal=Make();store.RegisterTraveler(proposal.Person);
                Check.True(store.ResolveResident(1,proposal.Person.Id)==null);
                store.RegisterTravelerVisit(proposal.Visit);
                var resident=store.ResolveResident(1,proposal.Person.Id);
                Check.True(resident!=null,"Registered Inn traveler has no managed actor identity.");
                Check.Equal(-1,resident.HomeKey);Check.Equal(proposal.Person.Name,resident.FullName);
                Check.True(store.ResolveResident(2,proposal.Person.Id)==null);
                var action=new NpcActionJob {Id="traveler-follow",ProposalId="traveler-follow",ResidentId=proposal.Person.Id,
                    Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=NpcDirectedRelationship.PlayerTargetId},
                    Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,FinishAt=400,DeadlineAt=460,
                    LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0}};
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,action,true,true,true,out reason),reason);
                Check.Equal(NpcActionKind.Follow,store.ActiveNpcAction(proposal.Person.Id).Kind);
                // The projection grants action capability, not a second mutable
                // life record. Runtime death continues through KillTraveler.
                resident.IsAlive=false;
                Check.True(store.Traveler(proposal.Person.Id).IsAlive);
                Check.True(store.ResolveResident(1,proposal.Person.Id).IsAlive);
                Check.True(store.KillTraveler(proposal.Person.Id,101*86400d));
                Check.True(!store.ResolveResident(1,proposal.Person.Id).IsAlive);
            })));
            cases.Add(("First native condition pins the generated traveler archetype and preserves it through restore",(Action)(()=>{
                var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
                TravelerProposal proposal=null;
                for(int slot=0;slot<1000;slot++)
                {
                    proposal=Make(slot:slot);
                    if(TravelerAdventurerPolicy.Archetype(proposal.Person.AppearanceSeed)==NpcArchetype.Mage)break;
                }
                store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);
                Check.Equal(NpcArchetype.Mage,store.ResolveNpcArchetype(proposal.Person.Id));
                Check.True(store.TryCaptureNpcConditions(store.CaptureNpcVitalsWriteEpoch(),proposal.Person.Id,"native-condition-packet"),
                    "Registered Inn traveler cannot retain native conditions.");
                Check.Equal(NpcArchetype.Mage,store.ReadNpcProgress(proposal.Person.Id).Archetype);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.Equal(NpcArchetype.Mage,restored.ResolveNpcArchetype(proposal.Person.Id));
                Check.Equal("native-condition-packet",restored.ReadNpcProgress(proposal.Person.Id).NativeConditions);
            })));
            cases.Add(("Traveler demand is stable across origin ordering and creates unique daily identities",()=>{
                var first=Make(); var second=Make(origins:new[] { Origins[1],Origins[0] });
                Check.Equal(first.Person.Id,second.Person.Id); Check.Equal(first.Person.Name,second.Person.Name);
                Check.Equal(first.Visit.DesiredArrival,second.Visit.DesiredArrival);
                Check.True(first.Person.Id!=Make(slot:1).Person.Id && first.Person.Id!=Make(day:101).Person.Id);
                Check.True(first.Person.HomeSettlement!=1); Check.Equal(first.Person.Id,first.Visit.PersonId);
                Check.Equal(1200d,Math.Round(first.Visit.CommuteSeconds));
            }));
            cases.Add(("Production inn demand reconstructs only the current lodging window and never duplicates or revives guests",(Action)(()=>{
                var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
                Check.Equal(10,TravelerDemand.EnsureInnTravelers(store,1,7,100,(day,slot)=>Make(day,slot)));
                Check.Equal(0,TravelerDemand.EnsureInnTravelers(store,1,7,100,(day,slot)=>Make(day,slot)));
                var saved=store.Snapshot();
                Check.Equal(10,saved.Travelers.Count);Check.Equal(10,saved.TravelerVisits.Count);
                foreach(var visit in saved.TravelerVisits)
                    Check.True(visit.DesiredArrival>=96*86400d && visit.DesiredArrival<101*86400d);
                string dead=saved.Travelers[0].Id;
                Check.True(store.KillTraveler(dead,101*86400d));
                var copy=new WorldStateStore(0);copy.Restore(store.Snapshot());
                Check.Equal(0,TravelerDemand.EnsureInnTravelers(copy,1,7,100,(day,slot)=>Make(day,slot)));
                Check.True(!copy.Traveler(dead).IsAlive);
                Check.Equal(2,TravelerDemand.EnsureInnTravelers(copy,1,7,101,(day,slot)=>Make(day,slot)));
                Check.Equal(12,copy.Snapshot().Travelers.Count);
            })));
            cases.Add(("Traveler demand varies stays and arrivals within city gate hours",()=>{
                var arrivals=new HashSet<double>(); var stays=new HashSet<int>(); bool night=false;
                for(int i=0;i<64;i++)
                {
                    var visit=Make(slot:i).Visit; var time=visit.DesiredArrival%86400;
                    Check.True(time>=21600 && time<64800); Check.True(visit.Nights>=1 && visit.Nights<=4);
                    arrivals.Add(time); stays.Add(visit.Nights);
                    var free=Make(slot:i,walled:false).Visit.DesiredArrival%86400;
                    night|=free<21600 || free>=64800;
                }
                Check.True(arrivals.Count>50 && stays.Count==4 && night);
            }));
            cases.Add(("Traveler demand rejects empty or local origin pools and invalid days",()=>{
                Check.Throws<ArgumentException>(()=>Make(origins:new TravelerOrigin[0]));
                Check.Throws<ArgumentException>(()=>Make(origins:new[] { new TravelerOrigin { SettlementId=1,Race=1 } }));
                Check.Throws<ArgumentException>(()=>Make(day:-1));
            }));
        }
    }
}
