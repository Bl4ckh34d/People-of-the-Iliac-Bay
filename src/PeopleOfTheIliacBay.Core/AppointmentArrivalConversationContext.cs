using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public static class AppointmentArrivalConversationContext
    {
        public static string Format(string purpose,string place,string agreedDateTime,double patienceMinutes,bool arrivedLate)
        {
            if(string.IsNullOrWhiteSpace(purpose) || string.IsNullOrWhiteSpace(place) ||
                string.IsNullOrWhiteSpace(agreedDateTime) || double.IsNaN(patienceMinutes) || double.IsInfinity(patienceMinutes) ||
                patienceMinutes<10 || patienceMinutes>150)return string.Empty;
            string arrival=arrivedLate?"The player arrived late, but within that agreed wait limit. ":
                "The player arrived within the agreed waiting period. ";
            return "Confirmed meeting: "+purpose.Trim()+" at "+place.Trim()+" on "+agreedDateTime.Trim()+". "+
                "Your agreed wait limit was "+patienceMinutes.ToString("0.##",CultureInfo.InvariantCulture)+" minutes. "+arrival+
                "Open by acknowledging this specific meeting, not with a routine greeting. Do not claim the meeting is completed or invent anything either of you has said.";
        }

        public static string FallbackReply(string purpose,string place,string agreedDateTime)
        {
            if(string.IsNullOrWhiteSpace(purpose) || string.IsNullOrWhiteSpace(place) || string.IsNullOrWhiteSpace(agreedDateTime))
                return string.Empty;
            return "There you are. We agreed to meet at "+place.Trim()+" on "+agreedDateTime.Trim()+" for "+purpose.Trim()+". Shall we begin?";
        }
    }
}
