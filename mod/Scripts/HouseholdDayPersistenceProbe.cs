using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Detached native codec acceptance. Does not publish to the live store,
    // move actors, change world time, buy anything or write a save file.
    internal static class HouseholdDayPersistenceProbe
    {
        private sealed class RouteSelectionDiagnostics
        {
            internal int ParentPairs,NoSavedTavern,TavernRoutes,HomeDoorReject,OtherHomeReject,InnDoorReject,TimeReject;
            internal double NearestHomeGap=double.PositiveInfinity,NearestInnGap=double.PositiveInfinity;
            internal readonly Dictionary<string,int> PlannerRejects=new Dictionary<string,int>();
            internal void PlannerReject(string reason)
            {
                reason=string.IsNullOrEmpty(reason)?"unspecified":reason;
                int count;PlannerRejects.TryGetValue(reason,out count);PlannerRejects[reason]=count+1;
            }
        }
        internal static void Verify(WorldState snapshot,BuildingDirectory directory,TownGeometry geometry,int wake,int sleep,double now)
        {
            HouseholdDayPlan expected;
            var request=CreateRequest(snapshot,directory,geometry,wake,sleep,now,out expected);
            VerifyRequest(snapshot,directory,request,expected,wake,sleep);
        }
        internal static HouseholdDayRequest CreateRequest(WorldState snapshot,BuildingDirectory directory,TownGeometry geometry,
            int wake,int sleep,double now,out HouseholdDayPlan expected)
        {
            HouseholdDayRequest request;
            if(!TryCreateRequest(snapshot,directory,geometry,wake,sleep,now,out request,out expected))
                throw new InvalidOperationException("No actual two-parent household with a saved native Inn route is suitable for codec acceptance.");
            return request;
        }
        internal static bool TryCreateRequest(WorldState snapshot,BuildingDirectory directory,TownGeometry geometry,
            int wake,int sleep,double now,out HouseholdDayRequest request,out HouseholdDayPlan expected)
        {
            var population=snapshot.Settlements.Find(t=>t.SettlementId==directory.MapID);
            var routines=snapshot.Routines.Find(t=>t.SettlementId==directory.MapID);
            if(population==null || routines==null)throw new InvalidOperationException("No native household source.");
            request=null;expected=null;
            var diagnostics=new RouteSelectionDiagnostics();
            foreach(var home in population.Households)
            {
                foreach(var child in home.Residents)
                {
                    if(!child.IsAlive || child.AgeGroup!=ResidentAgeGroup.Child)continue;
                    var a=home.Residents.Find(p=>p.Id==child.MotherId);var b=home.Residents.Find(p=>p.Id==child.FatherId);
                    if(a==null || b==null || snapshot.ResidentTrips.Exists(t=>t.ResidentId==a.Id || t.ResidentId==b.Id))continue;
                    var first=routines.Residents.Find(r=>r.ResidentId==a.Id);var second=routines.Residents.Find(r=>r.ResidentId==b.Id);
                    if(first==null || second==null || first.Itinerary==null || second.Itinerary==null)continue;
                    diagnostics.ParentPairs++;
                    int routesBefore=diagnostics.TavernRoutes;
                    var joint=SavedInnPath(first,second,directory,geometry,home.BuildingKey,wake,sleep,diagnostics);
                    if(joint==null){if(diagnostics.TavernRoutes==routesBefore)diagnostics.NoSavedTavern++;continue;}
                    int day=(int)Math.Floor(now/86400)+1;
                    while(day<(int)Math.Floor(now/86400)+1001 && !HouseholdDayPlanner.SharedDay(snapshot.WorldSeed,directory.MapID,home,day))day++;
                    var proposed=new HouseholdDayRequest{Household=home,SettlementId=directory.MapID,WorldSeed=snapshot.WorldSeed,
                        AbsoluteDay=day,WakeMinute=wake,SleepMinute=sleep,PlannedAt=day*86400d+wake*60d+60,
                        FirstRoutine=first,SecondRoutine=second,JointInn=joint};
                    HouseholdDayPlan plan;string reason;
                    if(!HouseholdDayPlanner.TryCreate(proposed,out plan,out reason))
                    {diagnostics.PlannerReject(reason);continue;}
                    request=proposed;expected=plan;break;
                }
                if(request!=null)break;
            }
            if(request==null)
            {
                var planner=new List<string>();
                foreach(var pair in diagnostics.PlannerRejects)planner.Add(pair.Key+"="+pair.Value);
                planner.Sort(StringComparer.Ordinal);
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_ROUTE_SELECTION: parentPairs="+diagnostics.ParentPairs+
                    "; noSavedTavern="+diagnostics.NoSavedTavern+"; tavernRoutes="+diagnostics.TavernRoutes+
                    "; homeDoorReject="+diagnostics.HomeDoorReject+"; otherHomeReject="+diagnostics.OtherHomeReject+
                    "; innDoorReject="+diagnostics.InnDoorReject+"; timeReject="+diagnostics.TimeReject+
                    "; nearestHomeGap="+diagnostics.NearestHomeGap.ToString("F3")+
                    "; nearestInnGap="+diagnostics.NearestInnGap.ToString("F3")+
                    "; planner="+string.Join(",",planner.ToArray()));
            }
            return request!=null;
        }
        private static void VerifyRequest(WorldState snapshot,BuildingDirectory directory,HouseholdDayRequest request,
            HouseholdDayPlan expected,int wake,int sleep)
        {
            var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(snapshot);
            string publishReason;
            if(!store.TryPublishHouseholdDay(expected,request.PlannedAt,out publishReason))
                throw new InvalidOperationException("Controlled native household publication failed: "+publishReason);
            var serialized=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var decoded=SaveLoadManager.Deserialize(typeof(WorldState),serialized) as WorldState;
            if(decoded==null || decoded.Version!=WorldState.CurrentVersion)throw new InvalidOperationException("Native household codec changed schema.");
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore(decoded);
            var actual=restored.ReadHouseholdDay(directory.MapID,request.Household.BuildingKey,request.AbsoluteDay);
            if(actual==null || SaveLoadManager.Serialize(typeof(HouseholdDayPlan),actual)!=SaveLoadManager.Serialize(typeof(HouseholdDayPlan),expected))
                throw new InvalidOperationException("Native codec changed paired household identity, selections or joint route/timing.");
            DailySchedule firstSchedule,secondSchedule;
            if(!restored.TryHouseholdDaySchedules(directory.MapID,request.Household.BuildingKey,request.AbsoluteDay,
                out firstSchedule,out secondSchedule,out publishReason))throw new InvalidOperationException(publishReason);
            var firstVisit=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(actual.JointInn.First.DepartureSeconds,
                actual.JointInn.First.Itinerary).ScheduledVisits())[0];
            var secondVisit=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(actual.JointInn.Second.DepartureSeconds,
                actual.JointInn.Second.Itinerary).ScheduledVisits())[0];
            double sample=request.AbsoluteDay*86400d+(Math.Max(firstVisit.EnterAt,secondVisit.EnterAt)+Math.Min(firstVisit.LeaveAt,secondVisit.LeaveAt))/2;
            if(firstSchedule.EvaluateSeconds(sample).BuildingKey!=actual.JointInn.InnKey ||
                secondSchedule.EvaluateSeconds(sample).BuildingKey!=actual.JointInn.InnKey ||
                !firstSchedule.ScheduledVisits(sample).Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex && v.BuildingKey==actual.JointInn.InnKey))
                throw new InvalidOperationException("Joint visit lacks both exact dated schedule states and its distinct identity.");
            var baseline=ResidentRoutineDay.Schedule(actual.HomeKey,request.FirstRoutine,wake,sleep);
            var outside=firstSchedule.EvaluateSeconds(sample+86400);var ordinary=baseline.EvaluateSeconds(sample+86400);
            if(outside.Activity!=ordinary.Activity || outside.BuildingKey!=ordinary.BuildingKey ||
                Math.Abs(outside.Position.X-ordinary.Position.X)>0.00001 || Math.Abs(outside.Position.Z-ordinary.Position.Z)>0.00001)
                throw new InvalidOperationException("Native dated joint event repeated on the next game day.");
            DailySchedule ignoredA,ignoredB;
            if(restored.TryHouseholdDaySchedules(directory.MapID,actual.HomeKey,actual.AbsoluteDay+1,out ignoredA,out ignoredB,out publishReason))
                throw new InvalidOperationException("Native date lookup replayed the paired event.");
            if(!HouseholdAccess.RoutineLocked(request.Household,p=>p.Id==actual.First.ResidentId?firstSchedule.EvaluateSeconds(sample):
                p.Id==actual.Second.ResidentId?secondSchedule.EvaluateSeconds(sample):new ActivitySnapshot(Activity.Home,actual.HomeKey,default(RoutePoint),0)))
                throw new InvalidOperationException("Controlled child-only home was not locked.");
            VerifyRecoveryProvenance(restored,actual,request,sample);
            VerifyJointProgress(restored,actual);
            VerifySimulationCodec(restored,actual);
            VerifySharedInnCodec(restored,actual);
            VerifyAppointmentPriorityAndSuppression(restored,actual,request,firstVisit);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_DAY_PERSISTENCE_PASS: schema="+WorldState.CurrentVersion+
                "; household="+actual.HomeKey+"; jointInn="+actual.JointInn.InnKey+
                "; atomicPair=true; exactDate=true; nativeSerializer=true; appointmentPriority=true; monotonicSuppression=true; typedFamilyRecovery=true; sharedProgress=true; physicalFamily=false; nativeDoorInteraction=false");
        }
        private static void VerifySimulationCodec(WorldStateStore source,HouseholdDayPlan plan)
        {
            // Detached points exercise the native codec and owner transition.
            // They are not real body placement or live offscreen acceptance.
            var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(source.Snapshot());
            plan=store.ReadHouseholdDay(plan.SettlementId,plan.HomeKey,plan.AbsoluteDay);
            var initial=HouseholdJointMovement.Create(plan,plan.PlannedAt).Snapshot();store.SaveHouseholdJointProgress(initial);
            double start=plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            double duration=Math.Max(plan.JointInn.First.Itinerary.Stops[0].WalkSeconds,plan.JointInn.Second.Itinerary.Stops[0].WalkSeconds);
            store.BeginHouseholdJointSimulation(plan.Id,initial.Sequence,start);
            var projected=store.AdvanceHouseholdJointSimulation(plan.Id,start+duration*0.5);
            if(projected.Phase!=HouseholdJointPhase.Outbound || projected.First.Distance<=0 || projected.Second.Distance<=0 ||
                store.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey).Sequence!=0 ||
                store.GetHouseholdJointMovement(plan.SettlementId,plan.HomeKey)!=null)
                throw new InvalidOperationException("Native simulation fixture mixed projected and actual owners.");
            var decoded=SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())) as WorldState;
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore(decoded);
            var saved=restored.ReadHouseholdJointSimulation(plan.SettlementId,plan.HomeKey);
            if(saved==null || !saved.Active || saved.Provenance!="world-time-projection" || saved.Base.Sequence!=0 ||
                saved.Base.Phase!=HouseholdJointPhase.Waiting || saved.Revision!=projected.Revision ||
                Math.Abs(saved.First.Distance-projected.First.Distance)>0.000001 ||
                Math.Abs(saved.Second.Distance-projected.Second.Distance)>0.000001 ||
                restored.GetHouseholdJointMovement(plan.SettlementId,plan.HomeKey)!=null)
                throw new InvalidOperationException("Native codec changed shared simulation provenance or ownership.");
            var materialized=restored.ReconstructHouseholdJointSimulation(plan.Id,saved.Revision,saved.AdvancedThrough,
                new HouseholdJointPlacement{ResidentId=saved.Base.FirstResidentId,Position=saved.First.Position},
                new HouseholdJointPlacement{ResidentId=saved.Base.SecondResidentId,Position=saved.Second.Position});
            if(materialized.Sequence!=1 || materialized.Phase!=HouseholdJointPhase.Outbound || materialized.EnteredAt!=0 ||
                restored.ReadHouseholdJointSimulation(plan.SettlementId,plan.HomeKey).Active)
                throw new InvalidOperationException("Controlled native materialization did not transfer both owners atomically.");
            decoded=SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),restored.Snapshot())) as WorldState;
            restored.Restore(decoded);saved=restored.ReadHouseholdJointSimulation(plan.SettlementId,plan.HomeKey);
            if(saved.Active || !saved.ReconstructedAt.HasValue || saved.Base.Sequence!=0 ||
                restored.GetHouseholdJointMovement(plan.SettlementId,plan.HomeKey)==null ||
                restored.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey).Sequence!=1)
                throw new InvalidOperationException("Native consumed projection reclaimed actual movement after reload.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_SIMULATION_CODEC_PASS: schema="+WorldState.CurrentVersion+
                "; separateProjection=true; immutableActualAnchor=true; exclusiveOwner=true; nativeSerializer=true; consumedHistory=true; controlledPoints=true; physicalFamily=false; liveFamily=false");
        }
        private static void VerifySharedInnCodec(WorldStateStore source,HouseholdDayPlan plan)
        {
            // Controlled codec fixture, not a physical Inn visit. Deliberately
            // use no-purchase receipts: this probe must never invent service.
            var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(source.Snapshot());
            var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());
            double clock=plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            motion.Depart(clock,plan.JointInn.First.Itinerary.Stops[0].Route[0],plan.JointInn.Second.Itinerary.Stops[0].Route[0]);
            store.SaveHouseholdJointProgress(motion.Snapshot());
            for(int i=0;motion.Snapshot().Phase==HouseholdJointPhase.Outbound;i++)
            {
                if(i>=100000)throw new InvalidOperationException("Controlled Inn codec route did not complete.");
                var progress=motion.Snapshot();var targets=motion.Targets(true,true);
                motion.Observe(clock+=0.125,Step(progress.First.Position,targets.First,0.25),Step(progress.Second.Position,targets.Second,0.25));
                store.SaveHouseholdJointProgress(motion.Snapshot());
            }
            motion.EnterInn(clock+=1);store.SaveHouseholdJointProgress(motion.Snapshot());double entered=clock;
            var receipts=new List<NpcInnConsumptionReceipt>();
            foreach(string buyer in new[]{plan.First.ResidentId,plan.Second.ResidentId})
            {
                var outing=buyer==plan.First.ResidentId?plan.JointInn.First:plan.JointInn.Second;
                var timing=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())[0];
                double end=plan.AbsoluteDay*86400d+timing.LeaveAt;
                var observed=new ObservedVisit{ResidentId=buyer,SettlementId=plan.SettlementId,BuildingKey=plan.JointInn.InnKey,
                    HouseholdDayId=plan.Id,HouseholdSourceFingerprint=HouseholdJointMovement.SourceFingerprint(plan),
                    ObservedAt=entered,PlannedEndAt=end,ResumeUntil=end,SecondsPerMetre=1,
                    Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg{Route=new List<GuardPoint3>{new GuardPoint3()},
                        DwellSeconds=60,AfterPhase=ShopVisitPhase.Haggling},new ObservedVisitLeg{
                        Route=new List<GuardPoint3>{new GuardPoint3()},AfterPhase=ShopVisitPhase.Complete}}};
                store.StoreObservedVisit(observed);
                NpcInnConsumptionReceipt receipt;
                if(!store.TryRecordNpcInnNoPurchase(buyer,null,entered+1,NpcInnConsumptionOutcome.Unavailable,
                    "Controlled codec fixture; no native service",out receipt))
                    throw new InvalidOperationException("Controlled family Inn receipt could not settle.");
                receipts.Add(receipt);
            }
            var decoded=SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())) as WorldState;
            foreach(var observed in decoded.ObservedVisits)
                if(observed.HouseholdDayId==plan.Id)
                {
                    var before=store.GetObservedVisit(observed.ResidentId);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_CODEC_DIAGNOSTIC: beforeEnd="+before.PlannedEndAt.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                        "; afterEnd="+observed.PlannedEndAt.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                        "; beforeId="+NpcInnConsumption.VisitId(before)+"; afterId="+NpcInnConsumption.VisitId(observed)+
                        "; receipt="+SaveLoadManager.Serialize(typeof(NpcInnConsumptionReceipt),observed.InnConsumption));
                }
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore(decoded);
            if(receipts[0].VisitId==receipts[1].VisitId || Math.Abs(restored.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey).EnteredAt-entered)>0.000001)
                throw new InvalidOperationException("Native codec lost paired Inn entry or separate buyer identity.");
            foreach(var receipt in receipts)
            {
                var observed=restored.GetObservedVisit(receipt.BuyerId);var actual=restored.ReadNpcInnConsumption(receipt.BuyerId);
                if(observed==null || observed.HouseholdDayId!=plan.Id || observed.HouseholdSourceFingerprint!=HouseholdJointMovement.SourceFingerprint(plan) ||
                    actual==null || actual.VisitId!=receipt.VisitId || actual.Outcome!=NpcInnConsumptionOutcome.Unavailable || actual.ChargedGold!=0)
                    throw new InvalidOperationException("Native codec changed source-bound Inn receipt.");
                NpcInnConsumptionReceipt replay;
                if(restored.TryRecordNpcInnNoPurchase(receipt.BuyerId,null,entered+2,NpcInnConsumptionOutcome.Unavailable,
                    "Controlled codec fixture; no native service",out replay))throw new InvalidOperationException("Native family Inn callback replayed after restore.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_CODEC_PASS: schema="+WorldState.CurrentVersion+
                "; nativeSerializer=true; sourceBound=true; separateBuyers=true; noPurchaseReceipts=true; replayBlocked=true; controlledEntry=true; physicalInn=false");
        }
        private static void VerifyJointProgress(WorldStateStore source,HouseholdDayPlan plan)
        {
            // Detached codec fixture only. The supplied points are controlled
            // samples on actual saved native geometry, not observed actor motion.
            var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(source.Snapshot());
            var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());
            var a=plan.JointInn.First.Itinerary.Stops[0].Route;var b=plan.JointInn.Second.Itinerary.Stops[0].Route;
            double departure=plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            motion.Depart(departure,a[0],b[0]);store.SaveHouseholdJointProgress(motion.Snapshot());
            var targets=motion.Targets(true,true);
            var first=Step(a[0],targets.First,0.25);var second=Step(b[0],targets.Second,0.2);
            motion.Observe(departure+1,first,second);store.SaveHouseholdJointProgress(motion.Snapshot());
            string expected=SaveLoadManager.Serialize(typeof(HouseholdJointProgress),motion.Snapshot());
            string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var decoded=SaveLoadManager.Deserialize(typeof(WorldState),json) as WorldState;
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore(decoded);
            var actual=restored.GetHouseholdJointMovement(plan.SettlementId,plan.HomeKey);
            if(actual==null || SaveLoadManager.Serialize(typeof(HouseholdJointProgress),actual.Snapshot())!=expected ||
                actual.Snapshot().Sequence!=2 || actual.Snapshot().Phase!=HouseholdJointPhase.Outbound)
                throw new InvalidOperationException("Native codec changed shared source identity, phase or paired route cursors.");
            var before=actual.Snapshot();actual.Observe(departure+3600,first,second);
            var after=actual.Snapshot();double firstDelta=Math.Abs(after.First.Distance-before.First.Distance);
            double secondDelta=Math.Abs(after.Second.Distance-before.Second.Distance);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_JOINT_ROUNDTRIP_DELTA: firstMetres="+firstDelta.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                "; secondMetres="+secondDelta.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                "; firstSegment="+before.First.Segment+"/"+after.First.Segment+"; secondSegment="+before.Second.Segment+"/"+after.Second.Segment+
                "; toleranceMetres=1e-6");
            // Native route/snapshot geometry already uses a 1e-6m roundtrip
            // tolerance. Keep phase and segment identity exact; do not confuse
            // serializer decimal rounding with real elapsed-time movement.
            if(firstDelta>0.000001 || secondDelta>0.000001 || before.Phase!=after.Phase ||
                before.First.Segment!=after.First.Segment || before.Second.Segment!=after.Second.Segment ||
                Math.Abs(before.First.Position.X-after.First.Position.X)>0.000001 || Math.Abs(before.First.Position.Z-after.First.Position.Z)>0.000001 ||
                Math.Abs(before.Second.Position.X-after.Second.Position.X)>0.000001 || Math.Abs(before.Second.Position.Z-after.Second.Position.Z)>0.000001)
                throw new InvalidOperationException("Elapsed time advanced a detached shared physical checkpoint.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_JOINT_PROGRESS_PASS: schema="+WorldState.CurrentVersion+
                "; nativeSerializer=true; sourceBound=true; pairedCursors=true; timeDoesNotMoveActors=true; controlledPoints=true; physicalFamily=false");
        }
        private static SavedRoutePoint Step(SavedRoutePoint from,SavedRoutePoint to,double metres)
        {
            double x=to.X-from.X,z=to.Z-from.Z,length=Math.Sqrt(x*x+z*z),scale=length>0?Math.Min(1,metres/length):0;
            return new SavedRoutePoint{X=from.X+x*scale,Z=from.Z+z*scale};
        }
        private static void VerifyRecoveryProvenance(WorldStateStore store,HouseholdDayPlan plan,HouseholdDayRequest request,double now)
        {
            double length=0;var route=plan.JointInn.First.Itinerary.ExitRoute;
            for(int index=1;index<route.Count;index++)
            {double x=route[index].X-route[index-1].X,z=route[index].Z-route[index-1].Z;length+=Math.Sqrt(x*x+z*z);}
            if(length<=0)throw new InvalidOperationException("Native family fixture has no real return distance.");
            double secondsPerMetre=plan.JointInn.First.Itinerary.ExitWalkSeconds/length;
            RoutineRecoveryPlan recovery;string reason;RoutineRecoveryFailure failure;
            if(!RoutineRecoveryPlanner.TryCreateWithHouseholdDay(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                request.WorldSeed,plan.First.ResidentId,store.Snapshot().Appointments,null,now,plan.JointInn.InnKey,now,secondsPerMetre,
                out recovery,out reason,out failure,false,true))
                throw new InvalidOperationException("Native typed family recovery failed: "+reason+" / "+failure);
            if(!recovery.HouseholdJointSkipped || recovery.HouseholdDayId!=plan.Id || recovery.Legs.Exists(l=>l.HouseholdJoint) ||
                recovery.Legs[recovery.Legs.Count-1].BuildingKey!=plan.HomeKey || recovery.EndsAt<=now)
                throw new InvalidOperationException("Native family recovery lost provenance or measured home return.");
            store.SaveRoutineRecovery(recovery);
            if(store.GetRoutineRecovery(recovery.ResidentId,plan.AbsoluteDay,recovery.RoutineFingerprint,recovery.AppointmentFingerprint)!=null)
                throw new InvalidOperationException("Legacy recovery lookup activated a typed household record.");
            var json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var decoded=SaveLoadManager.Deserialize(typeof(WorldState),json) as WorldState;
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore(decoded);
            var actual=restored.GetHouseholdRoutineRecovery(recovery.ResidentId,plan.AbsoluteDay);
            if(actual==null || SaveLoadManager.Serialize(typeof(RoutineRecoveryPlan),actual)!=SaveLoadManager.Serialize(typeof(RoutineRecoveryPlan),recovery) ||
                actual.Evaluate(actual.EndsAt).BuildingKey!=plan.HomeKey ||
                restored.GetHouseholdRoutineRecovery(recovery.ResidentId,plan.AbsoluteDay+1)!=null)
                throw new InvalidOperationException("Native FullSerializer changed or replayed the typed family continuation.");
        }
        private static void VerifyAppointmentPriorityAndSuppression(WorldStateStore store,HouseholdDayPlan plan,
            HouseholdDayRequest request,ResidentVisitSnapshot visit)
        {
            // Controlled projection only: this record is not booked with the live
            // player and creates no attendance, dialogue or experience receipt.
            var booking=new AppointmentRecord{Id="native-family-appointment",ResidentId=plan.First.ResidentId,
                Purpose="meet",Status="accepted",StartsAt=plan.AbsoluteDay*86400d+visit.EnterAt+300,
                EndsAt=plan.AbsoluteDay*86400d+visit.EnterAt+900,
                Place=new AppointmentPlace{Kind="building",SettlementId=plan.SettlementId,BuildingKey=plan.JointInn.InnKey}};
            HouseholdAppointmentDay composed;string reason;
            if(!HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                request.WorldSeed,new[]{booking},null,request.PlannedAt,out composed,out reason) || !composed.Plan.JointSuppressed)
                throw new InvalidOperationException("Native required appointment did not suppress both joint legs: "+reason);
            double at=(booking.StartsAt+booking.EndsAt)/2;
            if(composed.First.EvaluateSeconds(at).BuildingKey!=plan.JointInn.InnKey ||
                composed.First.ScheduledVisits(at).Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex) ||
                composed.Second.ScheduledVisits(at).Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex) ||
                booking.Status!="accepted")
                throw new InvalidOperationException("Native paired projection changed the meeting or retained a one-sided family event.");
            if(!store.SuppressHouseholdJoint(plan.Id,request.PlannedAt+1))
                throw new InvalidOperationException("Controlled future joint suppression failed.");
            var json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var decoded=SaveLoadManager.Deserialize(typeof(WorldState),json) as WorldState;
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore(decoded);
            var cancelled=restored.ReadHouseholdDay(plan.SettlementId,plan.HomeKey,plan.AbsoluteDay);
            if(cancelled==null || !cancelled.JointSuppressed || cancelled.JointInn==null ||
                cancelled.JointInn.First.Itinerary.Stops[0].Route.Count!=plan.JointInn.First.Itinerary.Stops[0].Route.Count)
                throw new InvalidOperationException("Native serializer lost one-way suppression or its real return route.");
            if(!HouseholdDayAppointments.TryCompose(cancelled,request.Household,request.FirstRoutine,request.SecondRoutine,
                request.WorldSeed,new AppointmentRecord[0],null,request.PlannedAt+2,out composed,out reason) ||
                !composed.Plan.JointSuppressed || composed.First.ScheduledVisits(at).Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex) ||
                composed.Second.ScheduledVisits(at).Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex))
                throw new InvalidOperationException("Clearing a native appointment reactivated a cancelled family outing.");
        }
        private static HouseholdJointInn SavedInnPath(ResidentRoutine first,ResidentRoutine second,BuildingDirectory directory,
            TownGeometry geometry,int home,int wake,int sleep,RouteSelectionDiagnostics diagnostics)
        {
            TownGeometry.Entrance homeDoor;if(!geometry.Entrances.TryGetValue(home,out homeDoor))return null;
            var secondOutings=ResidentRoutineDay.CopyOutings(second);
            if(secondOutings.Count==0)return null;
            var secondHome=secondOutings[0].Itinerary.Stops[0].Route[0];
            foreach(var outing in ResidentRoutineDay.CopyOutings(first))
            {
                var path=new List<SavedRoutePoint>();double walking=0;
                foreach(var stop in outing.Itinerary.Stops)
                {
                    foreach(var p in stop.Route)
                        if(path.Count==0 || path[path.Count-1].X!=p.X || path[path.Count-1].Z!=p.Z)
                            path.Add(new SavedRoutePoint{X=p.X,Z=p.Z});
                    walking+=stop.WalkSeconds;
                    BuildingSummary summary;TownGeometry.Entrance door;DailyServiceWindow window;
                    if(path.Count<2 || path.Count>10000 || !directory.GetBuildingSummary(stop.BuildingKey,out summary) ||
                        summary.BuildingType!=DFLocation.BuildingTypes.Tavern || !geometry.Entrances.TryGetValue(stop.BuildingKey,out door) ||
                        !DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[(int)summary.BuildingType],
                            PlayerActivate.closeHours[(int)summary.BuildingType],out window))continue;
                    diagnostics.TavernRoutes++;
                    var start=path[0];var end=path[path.Count-1];
                    double homeGap=Math.Sqrt(Math.Pow(start.X-homeDoor.LocalDoor.x,2)+Math.Pow(start.Z-homeDoor.LocalDoor.z,2));
                    double innGap=Math.Sqrt(Math.Pow(end.X-door.LocalDoor.x,2)+Math.Pow(end.Z-door.LocalDoor.z,2));
                    diagnostics.NearestHomeGap=Math.Min(diagnostics.NearestHomeGap,homeGap);
                    diagnostics.NearestInnGap=Math.Min(diagnostics.NearestInnGap,innGap);
                    if(Math.Abs(start.X-homeDoor.LocalDoor.x)>0.01 || Math.Abs(start.Z-homeDoor.LocalDoor.z)>0.01)
                    {diagnostics.HomeDoorReject++;continue;}
                    if(Math.Abs(start.X-secondHome.X)>0.00001 || Math.Abs(start.Z-secondHome.Z)>0.00001)
                    {diagnostics.OtherHomeReject++;continue;}
                    if(Math.Abs(end.X-door.LocalDoor.x)>0.01 || Math.Abs(end.Z-door.LocalDoor.z)>0.01)
                    {diagnostics.InnDoorReject++;continue;}
                    var reverse=new List<SavedRoutePoint>();for(int i=path.Count-1;i>=0;i--)reverse.Add(new SavedRoutePoint{X=path[i].X,Z=path[i].Z});
                    var itinerary=new ResidentDestination{Stops=new List<ResidentVisitStop>{new ResidentVisitStop{BuildingKey=stop.BuildingKey,
                        Purpose=ResidentVisitPurpose.Social,OpenMinute=window.OpenMinute,CloseMinute=window.CloseMinute,
                        WalkSeconds=walking,DwellSeconds=1800,Route=new List<SavedRoutePoint>(path)}},ExitRoute=reverse,ExitWalkSeconds=walking};
                    var journey=ResidentVisitJourney.Create(16*3600,itinerary);
                    if(journey.EndsAt>sleep*60d || 16*3600<wake*60d)
                    {diagnostics.TimeReject++;continue;}
                    return new HouseholdJointInn{InnKey=stop.BuildingKey,First=new ResidentDailyOuting{DepartureSeconds=16*3600,Itinerary=itinerary},
                        Second=new ResidentDailyOuting{DepartureSeconds=16*3600,Itinerary=ResidentVisitJourney.Copy(itinerary)}};
                }
            }
            return null;
        }
    }
}
