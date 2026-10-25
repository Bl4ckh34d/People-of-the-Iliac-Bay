using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class HouseholdInnServiceAdmission
    {
        public int ModelVersion=1,Revision,SimulationRevision;
        public string Id,HouseholdDayId,SourceFingerprint,Outcome="pending",FirstSeatId,SecondSeatId;
        public NpcInnServiceCatalog Catalog;
        public HouseholdDayPlan SourcePlan;
        public NpcInnServiceTariff Tariff;
        public NpcInnServiceIdentity FirstIdentity,SecondIdentity;
        public NpcInnConsumptionReceipt FirstReceipt,SecondReceipt;
        public double ArrivalAt,CounterAt,SeatedAt,EndsAt,ReturnAt;
        public double? ReleasedAt;
        public bool Materialized;

        public static HouseholdInnServiceAdmission Copy(HouseholdInnServiceAdmission source)
        {
            if(source==null || source.ModelVersion!=1 || source.Revision<0 || source.SimulationRevision<0 ||
                source.Id!=source.HouseholdDayId+":inn-service" || source.Outcome!="pending" && source.Outcome!="purchased" && source.Outcome!="refused" ||
                !HouseholdJointProgress.Time(source.ArrivalAt) || !HouseholdJointProgress.Time(source.CounterAt) ||
                !HouseholdJointProgress.Time(source.SeatedAt) || !HouseholdJointProgress.Time(source.EndsAt) ||
                !HouseholdJointProgress.Time(source.ReturnAt) || source.CounterAt<source.ArrivalAt || source.SeatedAt<source.CounterAt ||
                source.EndsAt<source.SeatedAt || source.ReturnAt<source.EndsAt ||
                source.ReleasedAt.HasValue && (source.Materialized?!HouseholdJointProgress.Time(source.ReleasedAt.Value) ||
                    source.ReleasedAt.Value<source.ArrivalAt:source.ReleasedAt.Value!=source.ReturnAt))
                throw new ArgumentException("Invalid household Inn service chronology or outcome.");
            HouseholdInnVisitProvenance.Validate(source.HouseholdDayId,source.SourceFingerprint);
            var catalog=NpcInnServiceCatalog.Copy(source.Catalog);
            var first=NpcInnServiceIdentity.Copy(source.FirstIdentity);var second=NpcInnServiceIdentity.Copy(source.SecondIdentity);
            if(first.BuyerId==second.BuyerId || source.Tariff==null || source.Tariff.Provenance!="native-calendar" ||
                source.Tariff.AbsoluteDay!=Math.Floor(source.CounterAt/86400d) || source.Tariff.RegionIndex!=catalog.RegionIndex ||
                source.Tariff.HolidayId<0 || source.Tariff.HolidayId>53)
                throw new ArgumentException("Invalid native household service tariff or participants.");
            foreach(var identity in new[]{first,second})
                if(identity.HouseholdDayId!=source.HouseholdDayId || identity.HouseholdSourceFingerprint!=source.SourceFingerprint ||
                    identity.SettlementId!=catalog.SettlementId || identity.BuildingKey!=catalog.BuildingKey ||
                    identity.StartsAt!=source.ArrivalAt || identity.EndsAt<=source.CounterAt)
                    throw new ArgumentException("Shared service identity differs from its source or service window.");
            var a=NpcInnConsumption.CopyServiceReceipt(source.FirstReceipt,first);
            var b=NpcInnConsumption.CopyServiceReceipt(source.SecondReceipt,second);
            foreach(var receipt in new[]{a,b})if(receipt!=null)
            {
                if(receipt.KeeperId!=catalog.KeeperId)throw new ArgumentException("Shared service receipt has another keeper.");
                if(receipt.MenuIndex>=0)
                {
                    var quote=NpcInnServiceCatalog.Quote(catalog,receipt.MenuIndex,source.CounterAt,source.Tariff);
                    if(receipt.Kind!=quote.Kind || receipt.ProductName!=quote.ProductName || receipt.QuotedPriceGold!=quote.PriceGold)
                        throw new ArgumentException("Shared service receipt differs from the retained native quote.");
                }
            }
            bool paid=a!=null && b!=null && a.Outcome==NpcInnConsumptionOutcome.Purchased && b.Outcome==NpcInnConsumptionOutcome.Purchased;
            if(source.Outcome=="pending" && (a!=null || b!=null || source.Materialized))
                throw new ArgumentException("Pending service cannot contain settled receipts or a physical handback.");
            if(source.Outcome=="purchased" && !paid || source.Outcome=="refused" && (a==null || b==null || paid ||
                source.FirstSeatId!=null || source.SecondSeatId!=null) || source.Outcome!="refused" &&
                !catalog.Pairs.Exists(p=>p.First.Id==source.FirstSeatId && p.Second.Id==source.SecondSeatId))
                throw new ArgumentException("Household service lacks its exact pair or corresponding receipts.");
            return new HouseholdInnServiceAdmission{ModelVersion=source.ModelVersion,Revision=source.Revision,
                SimulationRevision=source.SimulationRevision,Id=source.Id,HouseholdDayId=source.HouseholdDayId,
                SourceFingerprint=source.SourceFingerprint,Outcome=source.Outcome,FirstSeatId=source.FirstSeatId,SecondSeatId=source.SecondSeatId,
                Catalog=catalog,SourcePlan=HouseholdDayPlan.Copy(source.SourcePlan),Tariff=new NpcInnServiceTariff{AbsoluteDay=source.Tariff.AbsoluteDay,RegionIndex=source.Tariff.RegionIndex,
                    HolidayId=source.Tariff.HolidayId},FirstIdentity=first,SecondIdentity=second,FirstReceipt=a,SecondReceipt=b,
                ArrivalAt=source.ArrivalAt,CounterAt=source.CounterAt,SeatedAt=source.SeatedAt,EndsAt=source.EndsAt,ReturnAt=source.ReturnAt,
                ReleasedAt=source.ReleasedAt,Materialized=source.Materialized};
        }
    }

    public sealed partial class WorldStateStore
    {
        public HouseholdInnServiceAdmission ReadHouseholdInnServiceAdmission(string dayId)
        {var found=state.HouseholdInnServices.Find(s=>s.HouseholdDayId==dayId);return found==null?null:HouseholdInnServiceAdmission.Copy(found);}
        private NpcInnConsumptionReceipt ReadProjectedHouseholdReceipt(string buyer)
        {
            var cursor=IndexedInnConsumptionCursor(buyer);
            if(cursor==null || cursor.SourceKind!="world-time-projection")return null;
            var service=state.HouseholdInnServices.Find(s=>s.HouseholdDayId==cursor.HouseholdDayId && s.Outcome!="pending");
            var identity=service==null?null:buyer==service.FirstIdentity.BuyerId?service.FirstIdentity:
                buyer==service.SecondIdentity.BuyerId?service.SecondIdentity:null;
            var receipt=identity==null?null:buyer==service.FirstIdentity.BuyerId?service.FirstReceipt:service.SecondReceipt;
            return NpcInnConsumption.SameReceipt(receipt,cursor.Receipt)?NpcInnConsumption.CopyServiceReceipt(receipt,identity):null;
        }

        public HouseholdInnServiceAdmission AdvanceHouseholdInnService(string dayId,double now,
            Func<int,double,NpcInnServiceTariff> nativeTariff,Func<NpcInnServiceCatalog,NpcInnServiceSeatPair,bool> canReservePair=null)
        {
            Require(ValidTime(now) && nativeTariff!=null,"Shared Inn service needs a clock and trusted calendar adapter.");
            var existing=state.HouseholdInnServices.Find(s=>s.HouseholdDayId==dayId);
            if(existing!=null && existing.Outcome!="pending")return HouseholdInnServiceAdmission.Copy(existing);
            var sim=state.HouseholdJointSimulations.Find(s=>s.Active && s.Base.HouseholdDayId==dayId);
            if(sim==null || sim.RequiresRecovery || sim.PauseReason!=null || now<sim.AdvancedThrough ||
                sim.Phase!=HouseholdJointPhase.AtInn && sim.Phase!=HouseholdJointPhase.Visiting)return null;
            var actual=RequireSimulationAnchor(sim);HouseholdDayPlan plan;
            try{plan=ResolveJointSource(state,actual);}catch(ArgumentException){return null;}
            if(!JointSimulationParentsFree(actual,now))return null;
            var registered=ReadNpcInnServiceCatalog(actual.SettlementId,plan.JointInn.InnKey);
            if(registered==null)return null;
            bool keeperAlive;
            if(!KnownNpcIdentity(state,registered.KeeperId,out keeperAlive) || !keeperAlive)return null;
            var current=existing==null?CreateHouseholdInnAdmission(plan,sim,registered,nativeTariff,canReservePair):HouseholdInnServiceAdmission.Copy(existing);
            if(current==null || registered.Fingerprint!=current.Catalog.Fingerprint)return null;
            if(existing!=null && sim.AdvancedThrough>current.ArrivalAt && sim.PauseReason==null)
            {
                double shift=sim.AdvancedThrough-current.ArrivalAt;
                if(current.SeatedAt+shift>=current.EndsAt)
                {state.HouseholdInnServices.Remove(existing);return null;}
                current.ArrivalAt+=shift;current.CounterAt+=shift;current.SeatedAt+=shift;
                current.FirstIdentity.StartsAt=current.SecondIdentity.StartsAt=current.ArrivalAt;
                current.Tariff=nativeTariff(current.Catalog.RegionIndex,current.CounterAt);current.SimulationRevision=sim.Revision;
                var shifted=HouseholdInnServiceAdmission.Copy(current);
                state.HouseholdInnServices[state.HouseholdInnServices.IndexOf(existing)]=shifted;existing=shifted;
            }
            var policy=Core.HouseholdJointProgress.Copy(actual);policy.ObservedAt=current.CounterAt;
            if(JointDuties(state,plan,policy,null)!=JointDutyState.Compatible)return null;
            if(existing==null)
            {
                Require(state.HouseholdInnServices.Count<100000,"Household Inn service ledger is full.");
                state.HouseholdInnServices.Add(HouseholdInnServiceAdmission.Copy(current));existing=state.HouseholdInnServices[state.HouseholdInnServices.Count-1];
            }
            if(now<current.CounterAt)return HouseholdInnServiceAdmission.Copy(current);

            var first=IndexedNpcInventory(current.FirstIdentity.BuyerId);var second=IndexedNpcInventory(current.SecondIdentity.BuyerId);
            if(first==null || second==null)return HouseholdInnServiceAdmission.Copy(current);
            var a=ReadNpcInnServiceConsumption(current.FirstIdentity);var b=ReadNpcInnServiceConsumption(current.SecondIdentity);
            var firstQuote=QuoteHouseholdMenu(current,current.FirstIdentity,first,a);
            var secondQuote=QuoteHouseholdMenu(current,current.SecondIdentity,second,b);
            int needed=(a==null?firstQuote.PriceGold:0)+(b==null?secondQuote.PriceGold:0);
            bool available=HouseholdPaymentCanAdvance(current.FirstIdentity,a) && HouseholdPaymentCanAdvance(current.SecondIdentity,b) &&
                (a==null || a.Outcome==NpcInnConsumptionOutcome.Purchased) && (b==null || b.Outcome==NpcInnConsumptionOutcome.Purchased);
            bool affordable=available && (a!=null || first.WalletGold>=firstQuote.PriceGold) && (b!=null || second.WalletGold>=secondQuote.PriceGold);
            current.FirstReceipt=a??ProposeHouseholdMember(current.FirstIdentity,current.Catalog.KeeperId,current.CounterAt,firstQuote,
                affordable,available && first.WalletGold<firstQuote.PriceGold);
            current.SecondReceipt=b??ProposeHouseholdMember(current.SecondIdentity,current.Catalog.KeeperId,current.CounterAt,secondQuote,
                affordable,available && second.WalletGold<secondQuote.PriceGold);
            bool purchased=current.FirstReceipt.Outcome==NpcInnConsumptionOutcome.Purchased && current.SecondReceipt.Outcome==NpcInnConsumptionOutcome.Purchased;
            current.Outcome=purchased?"purchased":"refused";current.Revision++;
            if(!purchased)
            {
                current.FirstSeatId=current.SecondSeatId=null;current.SeatedAt=current.EndsAt=current.CounterAt;
                current.ReturnAt=current.CounterAt+RouteLength(current.Catalog.EntryToCounter)*HouseholdInteriorPace(plan);
            }
            if(now>=current.ReturnAt)current.ReleasedAt=current.ReturnAt;
            var validated=HouseholdInnServiceAdmission.Copy(current);
            // Stage both receipts before debiting the buyers. Native tavern meals have no seller stock.
            // No callback, serialization or partial buyer debit occurs between publication writes.
            var firstCursor=a==null && HouseholdPaymentCanAdvance(current.FirstIdentity,null)?
                NpcInnConsumption.CreateProjectedCursor(current.FirstIdentity,current.FirstReceipt):null;
            var secondCursor=b==null && HouseholdPaymentCanAdvance(current.SecondIdentity,null)?
                NpcInnConsumption.CreateProjectedCursor(current.SecondIdentity,current.SecondReceipt):null;
            if(affordable && needed>0)
            {
                var updates=new List<NpcInventoryUpdate>();
                if(a==null)updates.Add(new NpcInventoryUpdate{PersonId=first.PersonId,ExpectedRevision=first.Revision,
                    WalletGold=first.WalletGold-firstQuote.PriceGold,Items=CopyNpcInventory(first).Items});
                if(b==null)updates.Add(new NpcInventoryUpdate{PersonId=second.PersonId,ExpectedRevision=second.Revision,
                    WalletGold=second.WalletGold-secondQuote.PriceGold,Items=CopyNpcInventory(second).Items});
                if(!CommitNpcInventoryTransaction("household-service:"+current.HouseholdDayId,updates))return HouseholdInnServiceAdmission.Copy(existing);
            }
            if(firstCursor!=null)PublishInnConsumptionCursor(IndexedInnConsumptionCursor(firstCursor.BuyerId),firstCursor);
            if(secondCursor!=null)PublishInnConsumptionCursor(IndexedInnConsumptionCursor(secondCursor.BuyerId),secondCursor);
            state.HouseholdInnServices[state.HouseholdInnServices.IndexOf(existing)]=validated;
            state.HouseholdInnServices.RemoveAll(s=>s.Id!=validated.Id && s.ReleasedAt.HasValue &&
                !state.NpcInnConsumptionCursors.Exists(c=>c.HouseholdDayId==s.HouseholdDayId && c.SourceKind=="world-time-projection"));
            return HouseholdInnServiceAdmission.Copy(validated);
        }

        private HouseholdInnServiceAdmission CreateHouseholdInnAdmission(HouseholdDayPlan plan,HouseholdJointSimulation sim,
            NpcInnServiceCatalog catalog,Func<int,double,NpcInnServiceTariff> calendar,Func<NpcInnServiceCatalog,NpcInnServiceSeatPair,bool> canReservePair)
        {
            double arrival=Math.Max(sim.AdvancedThrough,Math.Max(sim.StartedAt,sim.PhaseSince)),pace=HouseholdInteriorPace(plan);
            double counter=arrival+RouteLength(catalog.EntryToCounter)*pace;
            var a=HouseholdServiceIdentity(plan,plan.First.ResidentId,arrival);
            var b=HouseholdServiceIdentity(plan,plan.Second.ResidentId,arrival);
            if(counter>=Math.Min(a.EndsAt,b.EndsAt))return null;
            var tariff=calendar(catalog.RegionIndex,counter);
            // Quoting validates the native day/region/hours before admission.
            NpcInnServiceCatalog.Quote(catalog,0,counter,tariff);
            foreach(var pair in catalog.Pairs)
            {
                if(canReservePair!=null && !canReservePair(catalog,pair))continue;
                if(state.ObservedVisits.Exists(v=>v.SettlementId==catalog.SettlementId && v.BuildingKey==catalog.BuildingKey &&
                    v.ResidentId!=plan.First.ResidentId && v.ResidentId!=plan.Second.ResidentId && v.EndsAt>arrival &&
                    v.Legs.Exists(l=>l.SeatId==pair.First.Id || l.SeatId==pair.Second.Id)))continue;
                double seated=counter+Math.Max(RouteLength(pair.FirstCounterToSeat),RouteLength(pair.SecondCounterToSeat))*pace;
                double end=Math.Min(a.EndsAt,b.EndsAt);
                if(seated>=end)continue;
                double leave=end+Math.Max(RouteLength(pair.FirstSeatToEntry),RouteLength(pair.SecondSeatToEntry))*pace;
                bool conflict=state.HouseholdInnServices.Exists(s=>s.Catalog.SettlementId==catalog.SettlementId && s.Catalog.BuildingKey==catalog.BuildingKey &&
                    s.Outcome!="refused" && s.ArrivalAt<leave && arrival<(s.ReleasedAt??(s.Materialized?double.MaxValue:s.ReturnAt)) &&
                    (s.FirstSeatId==pair.First.Id || s.FirstSeatId==pair.Second.Id || s.SecondSeatId==pair.First.Id || s.SecondSeatId==pair.Second.Id));
                if(conflict)continue;
                return HouseholdInnServiceAdmission.Copy(new HouseholdInnServiceAdmission{Id=plan.Id+":inn-service",HouseholdDayId=plan.Id,
                    SourceFingerprint=sim.Base.SourceFingerprint,SimulationRevision=sim.Revision,Catalog=catalog,SourcePlan=plan,Tariff=tariff,
                    FirstIdentity=a,SecondIdentity=b,FirstSeatId=pair.First.Id,SecondSeatId=pair.Second.Id,
                    ArrivalAt=arrival,CounterAt=counter,SeatedAt=seated,EndsAt=end,ReturnAt=leave});
            }
            return null;
        }
        private static NpcInnServiceIdentity HouseholdServiceIdentity(HouseholdDayPlan plan,string buyer,double arrival)
        {
            var outing=buyer==plan.First.ResidentId?plan.JointInn.First:plan.JointInn.Second;
            var window=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())[0];
            return new NpcInnServiceIdentity{BuyerId=buyer,SettlementId=plan.SettlementId,BuildingKey=plan.JointInn.InnKey,
                HouseholdDayId=plan.Id,HouseholdSourceFingerprint=HouseholdJointMovement.SourceFingerprint(plan),
                StartsAt=arrival,EndsAt=plan.AbsoluteDay*86400d+window.LeaveAt};
        }
        private static double HouseholdInteriorPace(HouseholdDayPlan plan)
        {
            double slower=0;
            foreach(var outing in new[]{plan.JointInn.First,plan.JointInn.Second})
            {
                var stop=outing.Itinerary.Stops[0];double length=0;
                for(int i=1;i<stop.Route.Count;i++)
                {double x=stop.Route[i].X-stop.Route[i-1].X,z=stop.Route[i].Z-stop.Route[i-1].Z;length+=Math.Sqrt(x*x+z*z);}
                Require(length>0,"Shared service needs measured source walking pace.");slower=Math.Max(slower,stop.WalkSeconds/length);
            }
            return slower;
        }
        private static double RouteLength(List<GuardPoint3> points)
        {
            double length=0;for(int i=1;i<points.Count;i++)
            {double x=points[i].X-points[i-1].X,y=points[i].Y-points[i-1].Y,z=points[i].Z-points[i-1].Z;length+=Math.Sqrt(x*x+y*y+z*z);}
            return length;
        }
        private NpcInnMenuQuote QuoteHouseholdMenu(HouseholdInnServiceAdmission service,NpcInnServiceIdentity identity,
            NpcInventoryState wallet,NpcInnConsumptionReceipt prior)
        {
            var prices=new List<int>();for(int i=0;i<11;i++)prices.Add(NpcInnServiceCatalog.Quote(service.Catalog,i,service.CounterAt,service.Tariff).PriceGold);
            var resident=ResolveResident(identity.BuyerId);
            int index=prior!=null && prior.MenuIndex>=0?prior.MenuIndex:NpcInnConsumption.SelectMenuIndex(prices,wallet.WalletGold,resident.AppearanceSeed);
            return NpcInnServiceCatalog.Quote(service.Catalog,index,service.CounterAt,service.Tariff);
        }
        private static NpcInnConsumptionReceipt ProposeHouseholdMember(NpcInnServiceIdentity identity,string keeper,double now,
            NpcInnMenuQuote quote,bool affordable,bool insufficient)
        {
            return affordable || insufficient?NpcInnConsumption.CreateServicePurchase(identity,keeper,now,quote,affordable):
                NpcInnConsumption.CreateServiceRefusal(identity,keeper,now,"shared visit unavailable");
        }
        private bool HouseholdPaymentCanAdvance(NpcInnServiceIdentity identity,NpcInnConsumptionReceipt settled)
        {
            var previous=IndexedInnConsumptionCursor(identity.BuyerId);
            return settled!=null || previous==null || previous.PlannedEndAt<identity.EndsAt;
        }
        private static List<HouseholdInnServiceAdmission> CopyHouseholdInnServices(WorldState source,WorldState copied)
        {
            var result=new List<HouseholdInnServiceAdmission>();if(source.Version<74)return result;
            Require(source.HouseholdInnServices!=null && source.HouseholdInnServices.Count<=100000,"Missing or unbounded household service ledger.");
            var owners=new HashSet<string>(StringComparer.Ordinal);
            foreach(var value in source.HouseholdInnServices)
            {
                var item=HouseholdInnServiceAdmission.Copy(value);Require(owners.Add(item.HouseholdDayId),"Duplicate household service owner.");
                var plan=copied.HouseholdDays.Find(p=>p.Id==item.HouseholdDayId)??(item.ReleasedAt.HasValue?item.SourcePlan:null);
                Require(plan!=null && plan.JointInn!=null && HouseholdJointMovement.SourceFingerprint(plan)==item.SourceFingerprint &&
                    item.SourcePlan.Id==plan.Id && HouseholdJointMovement.SourceFingerprint(item.SourcePlan)==item.SourceFingerprint &&
                    plan.SettlementId==item.Catalog.SettlementId && plan.JointInn.InnKey==item.Catalog.BuildingKey &&
                    plan.First.ResidentId==item.FirstIdentity.BuyerId && plan.Second.ResidentId==item.SecondIdentity.BuyerId,
                    "Shared service lost its exact household source.");
                ValidateInnCatalogKeeper(copied,item.Catalog,false);
                var sim=copied.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==item.HouseholdDayId);
                Require(item.ReleasedAt.HasValue || sim!=null && sim.Base.SourceFingerprint==item.SourceFingerprint && sim.Revision>=item.SimulationRevision,
                    "Shared service lost its simulation owner.");
                double pace=HouseholdInteriorPace(plan);
                Require(CloseServiceTime(item.FirstIdentity.EndsAt,HouseholdServiceIdentity(plan,item.FirstIdentity.BuyerId,item.ArrivalAt).EndsAt) &&
                    CloseServiceTime(item.SecondIdentity.EndsAt,HouseholdServiceIdentity(plan,item.SecondIdentity.BuyerId,item.ArrivalAt).EndsAt) &&
                    CloseServiceTime(item.CounterAt,item.ArrivalAt+RouteLength(item.Catalog.EntryToCounter)*pace),
                    "Shared service chronology differs from canonical source routes.");
                if(item.Outcome=="refused")Require(item.SeatedAt==item.CounterAt && item.EndsAt==item.CounterAt &&
                    CloseServiceTime(item.ReturnAt,item.CounterAt+RouteLength(item.Catalog.EntryToCounter)*pace),"Invalid refused shared service return.");
                else
                {
                    var pair=item.Catalog.Pairs.Find(p=>p.First.Id==item.FirstSeatId && p.Second.Id==item.SecondSeatId);
                    Require(CloseServiceTime(item.SeatedAt,item.CounterAt+Math.Max(RouteLength(pair.FirstCounterToSeat),RouteLength(pair.SecondCounterToSeat))*pace) &&
                        item.EndsAt==Math.Min(item.FirstIdentity.EndsAt,item.SecondIdentity.EndsAt) &&
                        CloseServiceTime(item.ReturnAt,item.EndsAt+Math.Max(RouteLength(pair.FirstSeatToEntry),RouteLength(pair.SecondSeatToEntry))*pace),
                        "Invalid shared service dwell or egress chronology.");
                }
                foreach(var receipt in new[]{item.FirstReceipt,item.SecondReceipt})if(receipt!=null && receipt.Outcome==NpcInnConsumptionOutcome.Purchased)
                {
                    var cursor=copied.NpcInnConsumptionCursors.Find(c=>c.BuyerId==receipt.BuyerId);
                    Require(cursor!=null && (NpcInnConsumption.SameReceipt(cursor.Receipt,receipt) ||
                        item.ReleasedAt.HasValue && cursor.Receipt.CompletedAt>=receipt.CompletedAt),"Shared service receipt differs from its payment ledger.");
                }
                foreach(var prior in result)
                    if(prior.Catalog.SettlementId==item.Catalog.SettlementId && prior.Catalog.BuildingKey==item.Catalog.BuildingKey &&
                        prior.Outcome!="refused" && item.Outcome!="refused" && prior.ArrivalAt<(item.ReleasedAt??(item.Materialized?double.MaxValue:item.ReturnAt)) &&
                        item.ArrivalAt<(prior.ReleasedAt??(prior.Materialized?double.MaxValue:prior.ReturnAt)))
                        Require(prior.FirstSeatId!=item.FirstSeatId && prior.FirstSeatId!=item.SecondSeatId &&
                            prior.SecondSeatId!=item.FirstSeatId && prior.SecondSeatId!=item.SecondSeatId,"Overlapping shared service seat reservations.");
                result.Add(item);
            }
            foreach(var cursor in copied.NpcInnConsumptionCursors)if(cursor.SourceKind=="world-time-projection")
            {
                var service=result.Find(s=>s.HouseholdDayId==cursor.HouseholdDayId);
                var receipt=service==null?null:cursor.BuyerId==service.FirstIdentity.BuyerId?service.FirstReceipt:
                    cursor.BuyerId==service.SecondIdentity.BuyerId?service.SecondReceipt:null;
                Require(service!=null && cursor.ServiceStartsAt==service.ArrivalAt &&
                    NpcInnConsumption.SameReceipt(cursor.Receipt,receipt) && CloseServiceTime(receipt.CompletedAt,service.CounterAt),
                    "Projected service cursor lost its exact admission and counter receipt.");
            }
            foreach(var sim in copied.HouseholdJointSimulations)if(sim.ModelVersion==2)
            {
                var service=result.Find(s=>s.Id==sim.ServiceAdmissionId && s.HouseholdDayId==sim.Base.HouseholdDayId);
                Require(sim.ServiceAdmissionId==null?sim.ServiceReturnReason=="service-unavailable" || sim.ServiceReturnReason=="observed-visit-completed" &&
                    sim.Base.Phase==HouseholdJointPhase.Visiting:service!=null,
                    "Simulation lost its service admission.");
                if(sim.ServiceReturnReason=="service-completed")Require(service!=null && service.Outcome!="pending" &&
                    sim.ReturnStartedAt>=service.ReturnAt,"Simulation returned before completed Inn egress.");
            }
            return result;
        }
        private static bool CloseServiceTime(double a,double b){return Math.Abs(a-b)<=Math.Max(0.000001,Math.Abs(a)*0.00000000000001);}
    }
}
