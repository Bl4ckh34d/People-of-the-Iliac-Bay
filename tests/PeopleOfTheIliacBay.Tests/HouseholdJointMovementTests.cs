using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdJointMovementTests
    {
        private static SavedRoutePoint Point(double x,double z=0){return new SavedRoutePoint{X=x,Z=z};}
        internal static HouseholdDayPlan Plan()
        {return HouseholdDayPlanTests.Create(HouseholdDayAppointmentTests.SharedRequest());}
        private static double Departure(HouseholdDayPlan plan)
        {return plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);}
        private static HouseholdJointMovement Depart(HouseholdDayPlan plan)
        {
            var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);
            motion.Depart(Departure(plan),Point(0),Point(0));return motion;
        }
        private static void WalkToInn(HouseholdJointMovement motion,HouseholdDayPlan plan)
        {for(int i=1;i<=20;i++)motion.Observe(Departure(plan)+i,Point(i*0.5),Point(i*0.5));}
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Native avoidance can leave the route centre without inventing a body jump",()=>{
                var plan=Plan();var motion=Depart(plan);
                for(int i=1;i<=4;i++)motion.Observe(Departure(plan)+i,Point(i*0.25,i*0.2),Point(i*0.25,i*0.2));
                Check.Near(1,motion.Snapshot().First.Position.X);Check.Near(0.8,motion.Snapshot().First.Position.Z);
                Check.Near(1,motion.Snapshot().First.Distance);
                Check.True(HouseholdJointMovement.Restore(plan,motion.Snapshot())!=null);
            }));
            cases.Add(("Walking corner is reached within forty centimetres after a real approach",()=>{
                var plan=Plan();var route=new List<SavedRoutePoint>{Point(0),Point(1),Point(1,1),Point(10)};
                plan.JointInn.First.Itinerary.Stops[0].Route=route;plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>(route);
                var motion=Depart(plan);motion.Observe(Departure(plan)+1,Point(0.5),Point(0.5));
                Check.Equal(0,motion.Snapshot().First.Segment);
                motion.Observe(Departure(plan)+2,Point(0.7),Point(0.7));Check.Equal(1,motion.Snapshot().First.Segment);
                motion.Observe(Departure(plan)+3,Point(0.7),Point(0.7));Check.Equal(1,motion.Snapshot().First.Segment);
                motion.Observe(Departure(plan)+4,Point(0.2),Point(0.2));Check.Equal(0,motion.Snapshot().First.Segment);
            }));
            cases.Add(("Native float endpoint rounding advances a corner only after actual directed motion",()=>{
                var plan=Plan();var route=new List<SavedRoutePoint>{Point(0),Point(512),Point(512.12348),Point(512.12348,1),Point(10)};
                plan.JointInn.First.Itinerary.Stops[0].Route=route;plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>(route);
                var motion=Depart(plan);
                for(int i=1;i<=1024;i++)motion.Observe(Departure(plan)+i,Point(i*0.5),Point(i*0.5));
                Check.Equal(1,motion.Snapshot().First.Segment);
                double native=(double)(float)512.12348;
                motion.Observe(Departure(plan)+1025,Point(native),Point(native));
                Check.Equal(2,motion.Snapshot().First.Segment);Check.Near(native,motion.Snapshot().First.Position.X);
                // A paused native body remains on the same rounded endpoint.
                // Rounding behind the incoming segment must not undo the
                // corner that its real directed approach already completed.
                for(int i=1;i<=20;i++)motion.Observe(Departure(plan)+1025+i,Point(native),Point(native));
                Check.Equal(2,motion.Snapshot().First.Segment);
                Check.True(HouseholdJointMovement.Restore(plan,motion.Snapshot())!=null);
                motion.Observe(Departure(plan)+1046,Point(native-0.5),Point(native-0.5));
                Check.Equal(1,motion.Snapshot().First.Segment);
            }));
            cases.Add(("Native endpoint tolerance cannot advance a stationary submillimetre loop",()=>{
                var plan=Plan();var route=new List<SavedRoutePoint>{Point(0),Point(0.0005),Point(0.0005,0.0005),Point(0,0.0005),Point(0),Point(10)};
                plan.JointInn.First.Itinerary.Stops[0].Route=route;plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>(route);
                var motion=Depart(plan);
                for(int i=1;i<=20;i++)motion.Observe(Departure(plan)+i,Point(0),Point(0));
                Check.Equal(0,motion.Snapshot().First.Segment);Check.Near(0,motion.Snapshot().First.Distance);
            }));
            cases.Add(("Paired physical observation preview leaves its owner unchanged until durable commit",()=>{
                var plan=Plan();var motion=Depart(plan);var before=motion.Snapshot();
                var candidate=motion.PreviewObservation(Departure(plan)+1,Point(0.25),Point(0.2));
                Check.Equal(before.Sequence+1,candidate.Sequence);Check.Near(0.25,candidate.First.Distance);
                Check.Equal(before.Sequence,motion.Snapshot().Sequence);Check.Near(0,motion.Snapshot().First.Distance);
                candidate.First.Position.X=999;Check.Near(0,motion.Snapshot().First.Position.X);
                Check.Throws<ArgumentException>(()=>motion.PreviewObservation(Departure(plan)+1,Point(0.25),Point(2)));
                Check.Equal(before.Sequence,motion.Snapshot().Sequence);Check.Near(0,motion.Snapshot().Second.Distance);
            }));
            cases.Add(("Observed reverse drift across an actual corner restores the preceding local segment",()=>{
                var plan=Plan();var route=new List<SavedRoutePoint>{Point(0),Point(1),Point(1,1),Point(10)};
                plan.JointInn.First.Itinerary.Stops[0].Route=route;plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>(route);
                var motion=Depart(plan);motion.Observe(Departure(plan)+1,Point(0.5),Point(0.5));
                motion.Observe(Departure(plan)+2,Point(1),Point(1));Check.Equal(1,motion.Snapshot().First.Segment);
                motion.Observe(Departure(plan)+3,Point(0.5),Point(0.5));
                Check.Equal(0,motion.Snapshot().First.Segment);Check.Near(0.5,motion.Snapshot().First.Distance);
                Check.True(HouseholdJointMovement.Restore(plan,motion.Snapshot())!=null);
            }));
            cases.Add(("Explicit bounded rejoin closes actual pair separation without moving the leader",()=>{
                var plan=Plan();var motion=Depart(plan);
                for(int i=1;i<=6;i++)motion.Observe(Departure(plan)+i,Point(i*0.5),Point(0));
                Check.True(motion.Targets(true,true).NeedsRejoin);
                var rejoin=motion.RejoinTargets(true,true);Check.True(!rejoin.MoveFirst);Check.True(rejoin.MoveSecond);
                motion.Observe(Departure(plan)+7,Point(3),Point(0.5));motion.Observe(Departure(plan)+8,Point(3),Point(1));
                Check.True(!motion.Targets(true,true).NeedsRejoin);
                Check.True(!motion.Targets(true,true).MoveFirst);Check.True(motion.Targets(true,true).MoveSecond);
            }));
            cases.Add(("Real short segment crossing advances once and roundtrips at the corner",()=>{
                var plan=Plan();var route=new List<SavedRoutePoint>{Point(0),Point(0.1),Point(0.1,0.2),Point(10)};
                plan.JointInn.First.Itinerary.Stops[0].Route=route;
                plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>(route);
                var motion=Depart(plan);motion.Observe(Departure(plan)+1,Point(0.1),Point(0.1));
                Check.Equal(1,motion.Snapshot().First.Segment);
                for(int i=2;i<=5;i++)motion.Observe(Departure(plan)+i,Point(0.1),Point(0.1));
                Check.Equal(1,motion.Snapshot().First.Segment);Check.Near(0.1,motion.Snapshot().First.Distance);
                var restored=HouseholdJointMovement.Restore(plan,motion.Snapshot());
                restored.Observe(Departure(plan)+6,Point(0.1,0.2),Point(0.1,0.2));
                Check.Equal(2,restored.Snapshot().First.Segment);Check.Near(0.3,restored.Snapshot().First.Distance);
            }));
            cases.Add(("Two actual door arrivals can stand apart without inventing endpoint progress",()=>{
                var plan=Plan();var motion=Depart(plan);
                for(int i=1;i<=19;i++)motion.Observe(Departure(plan)+i,Point(i*0.5),Point(i*0.5));
                motion.Observe(Departure(plan)+20,Point(9.8,0.26),Point(9.8,-0.26));
                Check.Equal(HouseholdJointPhase.AtInn,motion.Snapshot().Phase);
                Check.Near(9.8,motion.Snapshot().First.Distance);
                Check.True(HouseholdJointMovement.Restore(plan,motion.Snapshot())!=null);
            }));
            cases.Add(("Stationary observations cannot walk a dense short-segment loop",()=>{
                var plan=Plan();var route=new List<SavedRoutePoint>{Point(0),Point(0.1),Point(0.1,0.1),Point(0,0.1),Point(0),Point(10)};
                plan.JointInn.First.Itinerary.Stops[0].Route=route;
                plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>(route);
                var motion=Depart(plan);
                for(int i=1;i<=20;i++)motion.Observe(Departure(plan)+i,Point(0),Point(0));
                Check.Equal(0,motion.Snapshot().First.Segment);Check.Near(0,motion.Snapshot().First.Distance);
                Check.Near(0.1,motion.Targets(true,true).First.X);
            }));
            cases.Add(("Shared walkers reject divergent detours with only common endpoints",()=>{
                var plan=Plan();plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>{Point(0),Point(0,20),Point(10,20),Point(10)};
                Check.Throws<ArgumentException>(()=>HouseholdJointMovement.Create(plan,plan.PlannedAt));
            }));
            cases.Add(("Restore cannot award a metre of unobserved lead within one segment",()=>{
                var plan=Plan();var forged=Depart(plan).Snapshot();forged.First.Distance=1;
                Check.Throws<ArgumentException>(()=>HouseholdJointMovement.Restore(plan,forged));
            }));
            cases.Add(("Backward physical drift changes current lead without erasing traversed segment identity",()=>{
                var plan=Plan();var motion=Depart(plan);
                motion.Observe(Departure(plan)+1,Point(0.5),Point(0));motion.Observe(Departure(plan)+2,Point(1),Point(0));
                motion.Observe(Departure(plan)+3,Point(0.5),Point(0));motion.Observe(Departure(plan)+4,Point(0),Point(0));
                Check.Near(0,motion.Snapshot().First.Distance);Check.True(motion.Targets(true,true).MoveFirst);
            }));
            cases.Add(("Cancelled outbound source holds both actors for explicit physical recovery",()=>{
                var plan=Plan();var motion=Depart(plan);motion.Observe(Departure(plan)+1,Point(0.5),Point(0.5));
                var saved=motion.Snapshot();plan.Status="cancelled";
                var restored=HouseholdJointMovement.Restore(plan,saved);
                Check.True(!restored.Targets(true,true).MoveFirst);Check.True(!restored.Targets(true,true).MoveSecond);
                Check.Throws<ArgumentException>(()=>restored.Observe(Departure(plan)+2,Point(1),Point(1)));
            }));
            cases.Add(("Joint departure waits for later participant and requires both actual home positions",()=>{
                var plan=Plan();plan.JointInn.Second.DepartureSeconds+=60;
                var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);
                Check.Throws<ArgumentException>(()=>motion.Depart(Departure(plan)-1,Point(0),Point(0)));
                Check.Throws<ArgumentException>(()=>motion.Depart(Departure(plan),Point(4),Point(0)));
                Check.Equal(HouseholdJointPhase.Waiting,motion.Snapshot().Phase);
                motion.Depart(Departure(plan),Point(0),Point(0));
                Check.Equal(HouseholdJointPhase.Outbound,motion.Snapshot().Phase);
            }));
            cases.Add(("Joint motion holds both for absent or conversing participant and limits faster lead",()=>{
                var plan=Plan();var motion=Depart(plan);
                Check.True(!motion.Targets(true,false).MoveFirst);Check.True(!motion.Targets(false,true).MoveSecond);
                Check.True(!motion.Targets(true,true,true).MoveFirst);
                motion.Observe(Departure(plan)+1,Point(0.5),Point(0));
                motion.Observe(Departure(plan)+2,Point(1),Point(0));
                Check.True(motion.Targets(true,true).MoveFirst);
                motion.Observe(Departure(plan)+3,Point(1.5),Point(0));
                motion.Observe(Departure(plan)+4,Point(2),Point(0));
                var targets=motion.Targets(true,true);
                Check.True(!targets.MoveFirst);Check.True(targets.MoveSecond);
                motion.Observe(Departure(plan)+5,Point(2),Point(0.5));
                Check.True(motion.Targets(true,true).MoveFirst);
            }));
            cases.Add(("Elapsed game time alone cannot advance shared physical progress",()=>{
                var plan=Plan();var motion=Depart(plan);var before=motion.Snapshot();
                motion.Observe(Departure(plan)+3600,Point(0),Point(0));var after=motion.Snapshot();
                Check.Near(before.First.Distance,after.First.Distance);Check.Near(0,after.Second.Distance);
                Check.Equal(HouseholdJointPhase.Outbound,after.Phase);
            }));
            cases.Add(("Impossible second actor jump rejects the entire shared observation",()=>{
                var plan=Plan();var motion=Depart(plan);var before=motion.Snapshot();
                Check.Throws<ArgumentException>(()=>motion.Observe(Departure(plan)+1,Point(0.5),Point(4)));
                Check.Near(before.First.Position.X,motion.Snapshot().First.Position.X);
                Check.Near(before.ObservedAt,motion.Snapshot().ObservedAt);
                Check.Throws<ArgumentException>(()=>motion.Observe(Departure(plan)-1,Point(0),Point(0)));
            }));
            cases.Add(("Inn phase requires both observed arrivals and explicit physical entry",()=>{
                var plan=Plan();var motion=Depart(plan);
                for(int i=1;i<=20;i++)motion.Observe(Departure(plan)+i,Point(i*0.5),Point(Math.Min(9,i*0.5)));
                Check.Equal(HouseholdJointPhase.Outbound,motion.Snapshot().Phase);
                Check.Throws<ArgumentException>(()=>motion.EnterInn(Departure(plan)+21));
                motion.Observe(Departure(plan)+21,Point(10),Point(9.5));
                motion.Observe(Departure(plan)+22,Point(10),Point(10));
                Check.Equal(HouseholdJointPhase.AtInn,motion.Snapshot().Phase);
                motion.EnterInn(Departure(plan)+23);Check.Equal(HouseholdJointPhase.Visiting,motion.Snapshot().Phase);
            }));
            cases.Add(("Joint return resets both actual cursors and completes only at home",()=>{
                var plan=Plan();var motion=Depart(plan);WalkToInn(motion,plan);
                motion.EnterInn(Departure(plan)+21);
                motion.BeginReturn(Departure(plan)+22,Point(10),Point(10));
                Check.Equal(HouseholdJointPhase.Returning,motion.Snapshot().Phase);
                for(int i=1;i<=20;i++)motion.Observe(Departure(plan)+22+i,Point(10-i*0.5),Point(10-i*0.5));
                Check.Equal(HouseholdJointPhase.Completed,motion.Snapshot().Phase);
                Check.True(!motion.Targets(true,true).MoveFirst);
            }));
            cases.Add(("Shared movement roundtrip restores actual positions without advancing from wall clock",()=>{
                var plan=Plan();var motion=Depart(plan);motion.Observe(Departure(plan)+1,Point(0.5),Point(0.25));
                var options=new JsonSerializerOptions{IncludeFields=true};
                var saved=JsonSerializer.Deserialize<HouseholdJointProgress>(JsonSerializer.Serialize(motion.Snapshot(),options),options);
                var restored=HouseholdJointMovement.Restore(plan,saved);
                Check.Near(0.5,restored.Snapshot().First.Position.X);Check.Near(0.25,restored.Snapshot().Second.Distance);
                saved.First.Position.X=900;Check.Near(0.5,restored.Snapshot().First.Position.X);
                var owned=restored.Snapshot();owned.Second.Distance=9;Check.Near(0.25,restored.Snapshot().Second.Distance);
            }));
            cases.Add(("Joint movement restore rejects forged cursor, changed source and invented arrived phase",()=>{
                var plan=Plan();var saved=Depart(plan).Snapshot();saved.First.Segment=50;
                Check.Throws<ArgumentException>(()=>HouseholdJointMovement.Restore(plan,saved));
                saved=Depart(plan).Snapshot();saved.First.Distance=9;
                Check.Throws<ArgumentException>(()=>HouseholdJointMovement.Restore(plan,saved));
                saved=Depart(plan).Snapshot();saved.Phase=HouseholdJointPhase.AtInn;
                Check.Throws<ArgumentException>(()=>HouseholdJointMovement.Restore(plan,saved));
                saved=Depart(plan).Snapshot();plan.JointInn.First.Itinerary.Stops[0].WalkSeconds+=1;
                Check.Throws<ArgumentException>(()=>HouseholdJointMovement.Restore(plan,saved));
            }));
            cases.Add(("Shared route cursor cannot jump across a loop to a later nearby segment",()=>{
                var plan=Plan();var route=new List<SavedRoutePoint>{Point(0),Point(5),Point(5,5),Point(0,5),Point(0,0.1),Point(10)};
                plan.JointInn.First.Itinerary.Stops[0].Route=route;
                plan.JointInn.Second.Itinerary.Stops[0].Route=new List<SavedRoutePoint>(route);
                var motion=Depart(plan);motion.Observe(Departure(plan)+1,Point(0,0.1),Point(0,0.1));
                Check.Equal(0,motion.Snapshot().First.Segment);Check.Near(0,motion.Snapshot().First.Distance);
                Check.Near(5,motion.Targets(true,true).First.X);
            }));
            cases.Add(("Cancelled plan retains active return geometry but cannot create new shared travel",()=>{
                var plan=Plan();var motion=Depart(plan);WalkToInn(motion,plan);var saved=motion.Snapshot();
                plan.Status="cancelled";plan.JointSuppressed=true;
                Check.Throws<ArgumentException>(()=>HouseholdJointMovement.Create(plan,plan.PlannedAt));
                var restored=HouseholdJointMovement.Restore(plan,saved);
                restored.BeginReturn(Departure(plan)+21,Point(10),Point(10));
                Check.Equal(HouseholdJointPhase.Returning,restored.Snapshot().Phase);
            }));
        }
    }
}
