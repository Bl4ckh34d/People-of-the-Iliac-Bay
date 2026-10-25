using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Calculated presentation intent. Only a separate paired native placement
    // may turn these routes into fresh observed visits.
    public sealed class HouseholdInnProjection
    {
        public string ResidentId,SeatId;
        public GuardPoint3 Position;
        public double SecondsPerMetre;
        public List<ObservedVisitLeg> Legs;
    }
    public sealed class HouseholdInnPlacement {public string ResidentId;public GuardPoint3 Position;}
    public sealed partial class WorldStateStore
    {
        private ObservedVisit CapturedHouseholdInnVisit(HouseholdJointSimulation sim,string resident)
        {
            if(sim==null || sim.Base.Phase!=HouseholdJointPhase.Visiting)return null;
            var visit=IndexedObservedVisit(resident);
            return visit!=null && visit.HouseholdDayId==sim.Base.HouseholdDayId && visit.HouseholdSourceFingerprint==sim.Base.SourceFingerprint &&
                visit.ObservedAt<=sim.StartedAt?visit:null;
        }
        public HouseholdInnProjection ProjectHouseholdInnMember(string dayId,string resident,double now)
        {
            var service=state.HouseholdInnServices.Find(s=>s.HouseholdDayId==dayId && !s.Materialized);
            var sim=state.HouseholdJointSimulations.Find(s=>s.Active && s.Base.HouseholdDayId==dayId);
            if(sim!=null)now=HouseholdInnVisitProvenance.CanonicalInstant(now,sim.AdvancedThrough);
            var captured=CapturedHouseholdInnVisit(sim,resident);
            if(captured!=null && sim.PauseReason==null && sim.Phase==HouseholdJointPhase.Visiting && now>=sim.AdvancedThrough)
            {
                double clock=HouseholdInnVisitProvenance.CanonicalInstant(now-sim.InnClockOffset,captured.ObservedAt);
                var capturedLegs=captured.RemainingLegs(clock);
                if(capturedLegs.Count==0)return null;
                return new HouseholdInnProjection{ResidentId=resident,SeatId=captured.HouseholdSeatId,SecondsPerMetre=captured.SecondsPerMetre,
                    Legs=capturedLegs,Position=ClonePoint(capturedLegs[0].Route[0])};
            }
            double serviceClock=sim==null?now:now-sim.InnClockOffset;
            if(service==null || sim==null || !ValidTime(now) || serviceClock<service.ArrivalAt || serviceClock>=service.ReturnAt ||
                sim.Phase!=HouseholdJointPhase.AtInn && sim.Phase!=HouseholdJointPhase.Visiting ||
                resident!=service.FirstIdentity.BuyerId && resident!=service.SecondIdentity.BuyerId)return null;
            var pair=service.Catalog.Pairs.Find(p=>p.First.Id==service.FirstSeatId && p.Second.Id==service.SecondSeatId);
            bool first=resident==service.FirstIdentity.BuyerId;
            double pace=HouseholdInteriorPace(service.SourcePlan),start;
            var legs=new List<ObservedVisitLeg>();string seat=null;
            if(service.Outcome=="refused")
            {
                start=service.CounterAt;var route=new List<GuardPoint3>(service.Catalog.EntryToCounter);route.Reverse();
                legs.Add(new ObservedVisitLeg{Route=route,AfterPhase=ShopVisitPhase.Complete});
            }
            else
            {
                if(pair==null)return null;seat=first?pair.First.Id:pair.Second.Id;
                start=service.Outcome=="pending"?service.ArrivalAt:service.CounterAt;
                if(service.Outcome=="pending")legs.Add(new ObservedVisitLeg{Route=service.Catalog.EntryToCounter,AfterPhase=ShopVisitPhase.Haggling});
                var toSeat=first?pair.FirstCounterToSeat:pair.SecondCounterToSeat;
                legs.Add(new ObservedVisitLeg{Route=toSeat,AfterPhase=ShopVisitPhase.Browsing,SeatId=seat,
                    DwellSeconds=Math.Max(0,service.EndsAt-service.CounterAt-RouteLength(toSeat)*pace)});
                legs.Add(new ObservedVisitLeg{Route=first?pair.FirstSeatToEntry:pair.SecondSeatToEntry,AfterPhase=ShopVisitPhase.Complete});
            }
            var remaining=SliceHouseholdInnLegs(legs,start,service.Outcome=="pending"?Math.Min(serviceClock,service.CounterAt-0.000001):serviceClock,pace);
            return new HouseholdInnProjection{ResidentId=resident,SeatId=seat,SecondsPerMetre=pace,Legs=remaining,Position=ClonePoint(remaining[0].Route[0])};
        }
        private static GuardPoint3 ClonePoint(GuardPoint3 point){return new GuardPoint3{X=point.X,Y=point.Y,Z=point.Z};}
        private static List<ObservedVisitLeg> SliceHouseholdInnLegs(List<ObservedVisitLeg> source,double start,double now,double pace)
        {
            var result=new List<ObservedVisitLeg>();double remaining=Math.Max(0,now-start);
            for(int i=0;i<source.Count;i++)
            {
                var leg=source[i];double walk=RouteLength(leg.Route)*pace;
                if(result.Count==0 && remaining>=walk+leg.DwellSeconds && i<source.Count-1)
                {remaining-=walk+leg.DwellSeconds;continue;}
                var route=new List<GuardPoint3>();double dwell=leg.DwellSeconds;
                if(result.Count==0)
                {
                    if(remaining>=walk)
                    {route.Add(ClonePoint(leg.Route[leg.Route.Count-1]));dwell=Math.Max(0,dwell-(remaining-walk));}
                    else
                    {
                        double distance=remaining/pace;
                        for(int p=1;p<leg.Route.Count;p++)
                        {
                            var a=leg.Route[p-1];var b=leg.Route[p];double x=b.X-a.X,y=b.Y-a.Y,z=b.Z-a.Z;
                            double length=Math.Sqrt(x*x+y*y+z*z);
                            if(distance>length){distance-=length;continue;}
                            double f=length==0?0:distance/length;route.Add(new GuardPoint3{X=a.X+x*f,Y=a.Y+y*f,Z=a.Z+z*f});
                            for(int q=p;q<leg.Route.Count;q++)route.Add(ClonePoint(leg.Route[q]));break;
                        }
                        if(route.Count==0)route.Add(ClonePoint(leg.Route[leg.Route.Count-1]));
                    }
                }
                else foreach(var point in leg.Route)route.Add(ClonePoint(point));
                result.Add(new ObservedVisitLeg{Route=route,DwellSeconds=dwell,AfterPhase=leg.AfterPhase,SeatId=leg.SeatId});
            }
            return result;
        }
        public HouseholdJointProgress ReconstructHouseholdInnSimulation(string dayId,int expectedRevision,double now,
            HouseholdInnPlacement first,HouseholdInnPlacement second)
        {
            var sim=state.HouseholdJointSimulations.Find(s=>s.Active && s.Base.HouseholdDayId==dayId);
            var actual=RequireSimulationAnchor(sim);
            now=HouseholdInnVisitProvenance.CanonicalInstant(now,sim.AdvancedThrough);
            Require(sim.Revision==expectedRevision && sim.Revision<int.MaxValue && now>=sim.AdvancedThrough && JointSimulationParentsFree(actual,now),
                "Inn reconstruction lost its paired owner, revision or priority.");
            var service=state.HouseholdInnServices.Find(s=>s.HouseholdDayId==dayId);
            var firstCaptured=CapturedHouseholdInnVisit(sim,actual.FirstResidentId);var secondCaptured=CapturedHouseholdInnVisit(sim,actual.SecondResidentId);
            Require(service!=null && !service.Materialized || firstCaptured!=null && secondCaptured!=null,
                "Inn reconstruction requires a native service source or paired actual visit checkpoint.");
            var a=ProjectHouseholdInnMember(dayId,actual.FirstResidentId,now);var b=ProjectHouseholdInnMember(dayId,actual.SecondResidentId,now);
            Require(a!=null && b!=null && MatchesInnPlacement(first,a) && MatchesInnPlacement(second,b),
                "Both actual Inn bodies must match their own projected positions.");
            var next=Core.HouseholdJointProgress.Copy(actual);next.Sequence++;next.Phase=HouseholdJointPhase.Visiting;
            next.First=new HouseholdRouteCursor{Segment=sim.First.Segment,Distance=sim.First.Distance,Position=Core.HouseholdJointProgress.Point(sim.First.Position)};
            next.Second=new HouseholdRouteCursor{Segment=sim.Second.Segment,Distance=sim.Second.Distance,Position=Core.HouseholdJointProgress.Point(sim.Second.Position)};
            next.ObservedAt=now;next.DepartedAt=sim.DepartedAt;next.EnteredAt=actual.EnteredAt>0?actual.EnteredAt:now;
            var plan=state.HouseholdDays.Find(p=>p.Id==dayId);
            next=HouseholdJointMovement.Restore(plan,next).Snapshot();
            var av=firstCaptured!=null?ReplacedCapturedInnVisit(firstCaptured,a,first,now):PlacedInnVisit(service,a,first,now);
            var bv=secondCaptured!=null?ReplacedCapturedInnVisit(secondCaptured,b,second,now):PlacedInnVisit(service,b,second,now);
            ValidateHouseholdInnVisit(state,av,true,next);ValidateHouseholdInnVisit(state,bv,true,next);
            var consumed=HouseholdJointSimulation.Copy(sim);consumed.ReconstructedAt=now;consumed.Revision++;
            var admission=service==null?null:HouseholdInnServiceAdmission.Copy(service);
            if(admission!=null){admission.Materialized=true;admission.ReleasedAt=null;}
            // All detached route, receipt, participant and clock checks precede publication.
            state.HouseholdJointProgress[state.HouseholdJointProgress.IndexOf(actual)]=next;
            state.HouseholdJointSimulations[state.HouseholdJointSimulations.IndexOf(sim)]=consumed;
            if(service!=null)
            {if(service.Outcome=="pending")state.HouseholdInnServices.Remove(service);
                else state.HouseholdInnServices[state.HouseholdInnServices.IndexOf(service)]=admission;}
            PublishObservedVisit(av);PublishObservedVisit(bv);householdJointControllers.Remove(dayId);BumpHouseholdJointRevision();
            return Core.HouseholdJointProgress.Copy(next);
        }
        private static bool MatchesInnPlacement(HouseholdInnPlacement actual,HouseholdInnProjection expected)
        {
            if(actual==null || actual.ResidentId!=expected.ResidentId || actual.Position==null)return false;
            double x=actual.Position.X-expected.Position.X,y=actual.Position.Y-expected.Position.Y,z=actual.Position.Z-expected.Position.Z;
            return !double.IsNaN(x+y+z) && x*x+y*y+z*z<=0.0025;
        }
        private static ObservedVisit PlacedInnVisit(HouseholdInnServiceAdmission source,HouseholdInnProjection intent,HouseholdInnPlacement actual,double now)
        {
            var identity=intent.ResidentId==source.FirstIdentity.BuyerId?source.FirstIdentity:source.SecondIdentity;
            var receipt=intent.ResidentId==source.FirstIdentity.BuyerId?source.FirstReceipt:source.SecondReceipt;
            var legs=intent.Legs;legs[0].Route[0]=ClonePoint(actual.Position);
            return ObservedVisit.Copy(new ObservedVisit{ResidentId=actual.ResidentId,SettlementId=source.Catalog.SettlementId,
                BuildingKey=source.Catalog.BuildingKey,HouseholdDayId=source.HouseholdDayId,HouseholdSourceFingerprint=source.SourceFingerprint,
                HouseholdSeatId=intent.SeatId,ObservedAt=now,PlannedEndAt=identity.EndsAt,ResumeUntil=Math.Max(now,identity.EndsAt),
                SecondsPerMetre=intent.SecondsPerMetre,Legs=legs,InnConsumption=receipt});
        }
        private static ObservedVisit ReplacedCapturedInnVisit(ObservedVisit source,HouseholdInnProjection intent,HouseholdInnPlacement actual,double now)
        {
            var result=ObservedVisit.Copy(source);result.ObservedAt=now;result.ResumeUntil=Math.Max(now,result.ResumeUntil);
            result.Legs=intent.Legs;result.Legs[0].Route[0]=ClonePoint(actual.Position);return ObservedVisit.Copy(result);
        }
    }
}
