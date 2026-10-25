using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationReentry
    {
        public static string Prompt(double previousAt,double now)
        {
            if(double.IsNaN(previousAt) || double.IsInfinity(previousAt) || previousAt<0 ||
                double.IsNaN(now) || double.IsInfinity(now) || now<previousAt)
                throw new ArgumentException("Invalid conversation reentry time.");
            double elapsed=Math.Floor(now-previousAt);
            bool sameDay=Math.Floor(previousAt/86400)==Math.Floor(now/86400);
            string timing=elapsed.ToString("0",CultureInfo.InvariantCulture)+" game seconds have passed; "+
                (sameDay ? "it is the same game day. " :
                Math.Floor(elapsed/86400).ToString("0",CultureInfo.InvariantCulture)+" game day(s) have elapsed; the calendar day changed. ");
            return "[The player has approached again without saying anything new. "+timing+
                (elapsed<600 ? "This is a brief interruption: continue the last topic or give a very short familiar acknowledgement. Do not ask what brings them back, imply another day or long absence, restart introductions, or repeat a weather greeting. " :
                "Give a brief familiar returning greeting that follows your shared history and actual elapsed time. ")+
                "Use at most two sentences, shaped by current disposition and mood. Use the player's known name naturally. "+
                "Do not invent player speech, treat them as a stranger, reapply old insults, or promise actions not confirmed by the schedule.]";
        }
    }
}
