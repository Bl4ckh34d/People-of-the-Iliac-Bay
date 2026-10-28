using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // Read-only self-knowledge. An origin settlement, Inn reservation and
    // observed current location are separate facts. Bound guests retain their
    // actual origin family; older standalone fixtures have no verified family.
    public static class TravelerConversationContext
    {
        public static bool IsCurrentVisit(WorldStateStore store,string personId,int settlement,double now,bool observed=false)
        {
            var person=store==null ? null : store.Traveler(personId);
            if(person==null || settlement<0 || person.HomeKey>=0 && settlement==person.HomeSettlement ||
                double.IsNaN(now) || double.IsInfinity(now) || now<0) return false;
            foreach(var visit in store.TravelerVisitsFor(settlement))
            {
                if(visit.PersonId!=personId) continue;
                var journey=store.TravelerJourneyFor(visit.Id);
                if(now>=journey.EntryAt && (now<journey.ExitAt || observed || store.ActiveNpcAction(personId)!=null || store.ActiveNpcTask(personId)!=null))
                    return true;
            }
            return false;
        }
        public static string RoleLabel(WorldStateStore store,string personId)
        {
            if(store==null || store.Traveler(personId)==null)return string.Empty;
            switch(store.ResolveNpcArchetype(personId))
            {
                case NpcArchetype.Warrior:return "warrior traveler";
                case NpcArchetype.Mage:return "mage traveler";
                case NpcArchetype.Thief:return "rogue traveler";
                default:return "ordinary traveler";
            }
        }

        public static string IdentityAndLodging(WorldStateStore store,string personId,int settlement,double now,
            Func<int,string> settlementName,Func<int,string> buildingName)
        {
            var person=store==null?null:store.Traveler(personId);if(person==null)return string.Empty;
            if(person.HomeKey>=0 && settlement==person.HomeSettlement)return string.Empty;
            string identity="You are a "+RoleLabel(store,personId)+" from "+Place(settlementName,person.HomeSettlement,"an unnamed settlement")+". ";
            var visit=LocalVisit(store,personId,settlement,now);
            string lodging="No local Inn booking is verified.";
            if(visit!=null)
            {
                string inn=Place(buildingName,visit.TavernKey,"the recorded Inn");
                var journey=store.TravelerJourneyFor(visit.Id);
                bool currentRoom=visit.Admission==TravelerAdmission.Admitted && store.TravelerBed(visit.Id,now)!=null;
                if(currentRoom)lodging="Your booked bed is at "+inn+"; checkout "+When(journey.CheckOutAt,now)+".";
                else if(visit.Admission==TravelerAdmission.Admitted && now>=journey.CheckOutAt)
                    lodging="Your earlier bed booking at "+inn+" has ended.";
                else if(visit.Diversion!=null && visit.Diversion.Camping)
                    lodging=now<journey.CheckInAt?"The Inns could not provide a usable bed; you are walking to an actual camp outside the settlement.":
                        now<journey.CheckOutAt?"The Inns could not provide a usable bed; you are camping outside the settlement until your planned departure. No Inn bed is booked.":
                        "Your outside camp stay has ended; observed position determines whether your departure is complete. No Inn bed is booked.";
                else if(visit.Admission==TravelerAdmission.Refused)
                    lodging="Your bed request at "+inn+" was refused; no bed is booked there.";
                else if(visit.Admission==TravelerAdmission.Pending)
                    lodging="Your planned Inn stop is "+inn+"; no bed booking is confirmed.";
                else lodging="The recorded Inn is "+inn+", but no current bed is verified.";
            }
            var home=store.TravelerOriginHousehold(personId);
            string family=home==null ? " No permanent house or household is verified; do not invent an address or family." :
                " Your permanent home is the "+home.Surname+" family home in your origin settlement.";
            var resident=store.TravelerOriginResident(personId);
            if(home!=null && resident!=null) foreach(var member in home.Residents)
            {
                if(member.Id==personId) continue;
                string relation=member.Id==resident.PartnerId ? "partner" : member.Id==resident.MotherId ? "mother" :
                    member.Id==resident.FatherId ? "father" : member.MotherId==personId || member.FatherId==personId ? "child" : "household member";
                family+=" "+member.FullName+" is your "+relation+(member.IsAlive ? "." : " and is deceased.");
            }
            return identity+lodging+family;
        }

        public static string PlannedActivity(WorldStateStore store,string personId,int settlement,double now,
            Func<int,string> buildingName)
        {
            var person=store==null ? null : store.Traveler(personId);
            if(person==null || person.HomeKey>=0 && settlement==person.HomeSettlement)return string.Empty;
            var visit=LocalVisit(store,personId,settlement,now);
            string stop=visit==null?"no local stop is recorded":Place(buildingName,visit.TavernKey,"the recorded Inn");
            var action=store.ActiveNpcAction(personId);
            if(action!=null)
                return action.ReturningToRoutine?
                    "The agreed action ended; you are physically rejoining your traveler routine. Do not claim arrival before it is observed.":
                    "Your accepted "+action.Kind+" action interrupts the ordinary traveler plan. Recorded stop: "+stop+". Current action and observed position take precedence.";
            if(store.ActiveNpcTask(personId)!=null)
                return "An accepted task interrupts the ordinary traveler plan. Recorded stop: "+stop+". Do not infer your current location from that stop.";
            if(visit==null)return "No local travel itinerary or onward destination is recorded.";
            var journey=store.TravelerJourneyFor(visit.Id);
            var phase=journey.Evaluate(now).Phase;
            string purpose="Your journey purpose is "+Purpose(visit.Purpose)+". ";
            if(store.TravelerAwaitingLodgingDecision(visit.Id,now))
                return purpose+"Your preferred Inn could not provide a usable bed. You are seeking another usable Inn or lawful shelter; no other bed or arrival is confirmed.";
            if(visit.Diversion!=null && visit.Diversion.Camping)
                return purpose+(now<journey.CheckInAt?"Your measured route leads to an outside camp. Observed position determines whether you have arrived.":
                    now<journey.CheckOutAt?"Your ordinary itinerary calls for an outside camp stay. Observed position and activity determine whether you are there or asleep.":
                    "Your outside camp stay has ended; the measured route rejoins your recorded settlement exit. Observed position determines whether you have departed.");
            if(phase==TravelerPhase.NotArrived || phase==TravelerPhase.Arriving)
                return purpose+"Your scheduled destination is "+stop+". A planned arrival does not prove physical entry or a room booking.";
            if(phase==TravelerPhase.AtInn || phase==TravelerPhase.Sleeping)
                return purpose+"Your ordinary itinerary calls for an Inn stay. Observed position and activity determine whether you are physically inside or asleep.";
            if(phase==TravelerPhase.WaitingForGate)
                return "Your stay has ended; the travel plan waits for the settlement gate. Do not claim you have departed.";
            return store.TravelerOriginHousehold(personId)==null ?
                "Your ordinary travel plan leaves this settlement. No onward settlement is recorded; do not infer one from your origin." :
                "Your visit has ended; your travel plan returns to your reserved origin household. Observed position determines whether you have departed.";
        }
        private static string Purpose(ResidentVisitPurpose purpose)
        {
            switch(purpose)
            {
                case ResidentVisitPurpose.Shop:return "shopping";
                case ResidentVisitPurpose.Temple:return "a temple visit";
                case ResidentVisitPurpose.Guild:return "guild business";
                case ResidentVisitPurpose.Castle:return "a castle visit";
                case ResidentVisitPurpose.Social:return "a social visit";
                default:return "an Inn stay";
            }
        }

        private static TravelerVisit LocalVisit(WorldStateStore store,string personId,int settlement,double now)
        {
            TravelerVisit past=null,future=null;
            if(settlement<0 || double.IsNaN(now) || double.IsInfinity(now) || now<0)return null;
            foreach(var visit in store.TravelerVisitsFor(settlement))
            {
                if(visit.PersonId!=personId)continue;
                if(visit.DesiredArrival<=now)
                {if(past==null || visit.DesiredArrival>past.DesiredArrival)past=visit;}
                else if(future==null || visit.DesiredArrival<future.DesiredArrival)future=visit;
            }
            return past??future;
        }

        private static string Place(Func<int,string> name,int key,string fallback)
        {
            string value=name==null?null:name(key);
            if(string.IsNullOrWhiteSpace(value))return fallback;
            value=value.Trim().Replace('\r',' ').Replace('\n',' ');
            return value.Length<=64?value:value.Substring(0,61)+"...";
        }

        private static string When(double time,double now)
        {
            int days=(int)(Math.Floor(time/86400d)-Math.Floor(now/86400d));
            string day=days==0?"today":days==1?"tomorrow":"in "+days.ToString(CultureInfo.InvariantCulture)+" days";
            int minute=(int)(time%86400/60);
            return (minute/60).ToString("00",CultureInfo.InvariantCulture)+":"+(minute%60).ToString("00",CultureInfo.InvariantCulture)+" "+day;
        }
    }
}
