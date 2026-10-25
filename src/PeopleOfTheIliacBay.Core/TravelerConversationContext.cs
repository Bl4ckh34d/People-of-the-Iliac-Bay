using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // Read-only self-knowledge. An origin settlement, Inn reservation and
    // observed current location are separate facts, never a generated home.
    public static class TravelerConversationContext
    {
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
            string identity="You are a "+RoleLabel(store,personId)+" from "+Place(settlementName,person.HomeSettlement,"an unnamed settlement")+". ";
            var visit=LocalVisit(store,personId,settlement,now);
            string lodging="No local Inn booking is verified.";
            if(visit!=null)
            {
                string inn=Place(buildingName,visit.TavernKey,"the recorded Inn");
                var journey=store.TravelerJourneyFor(visit.Id);
                bool currentRoom=visit.Admission==TravelerAdmission.Admitted && store.TravelerBed(visit.Id,now)!=null;
                if(currentRoom)lodging="Your paid room is at "+inn+"; checkout "+When(journey.CheckOutAt,now)+".";
                else if(visit.Admission==TravelerAdmission.Admitted && now>=journey.CheckOutAt)
                    lodging="Your earlier room booking at "+inn+" has ended.";
                else if(visit.Admission==TravelerAdmission.Refused)
                    lodging="Your room request at "+inn+" was refused; no room is booked there.";
                else if(visit.Admission==TravelerAdmission.Pending)
                    lodging="Your planned Inn stop is "+inn+"; no room booking is confirmed.";
                else lodging="The recorded Inn is "+inn+", but no current room is verified.";
            }
            return identity+lodging+" No permanent house or household is verified; do not invent an address or family.";
        }

        public static string PlannedActivity(WorldStateStore store,string personId,int settlement,double now,
            Func<int,string> buildingName)
        {
            if(store==null || store.Traveler(personId)==null)return string.Empty;
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
            if(phase==TravelerPhase.NotArrived || phase==TravelerPhase.Arriving)
                return "Your scheduled destination is "+stop+". A planned arrival does not prove physical entry or a room booking.";
            if(phase==TravelerPhase.AtInn || phase==TravelerPhase.Sleeping)
                return "Your ordinary itinerary calls for an Inn stay. Observed position and activity determine whether you are physically inside or asleep.";
            if(phase==TravelerPhase.WaitingForGate)
                return "Your stay has ended; the travel plan waits for the settlement gate. Do not claim you have departed.";
            return "Your ordinary travel plan leaves this settlement. No onward settlement is recorded; do not infer one from your origin.";
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
