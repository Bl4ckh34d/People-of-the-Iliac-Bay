using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationClock
    {
        public static string Context(int hour,int minute,string date,int year,bool isNight)
        {
            if(hour<0 || hour>23 || minute<0 || minute>59) throw new System.ArgumentOutOfRangeException(nameof(hour));
            // NPCs know the current half-hour, not a minute-precise watch.
            // Floor keeps midnight on the same authoritative calendar date.
            minute=minute<30?0:30;
            int twelveHour=hour%12;
            if(twelveHour==0)twelveHour=12;
            string clock=hour.ToString("00",CultureInfo.InvariantCulture)+":"+minute.ToString("00",CultureInfo.InvariantCulture);
            return "Current game time, approximately: "+twelveHour.ToString("0",CultureInfo.InvariantCulture)+":"+
                minute.ToString("00",CultureInfo.InvariantCulture)+(hour<12?" AM":" PM")+" (24-hour "+clock+") on "+
                LocalDialogue.NormalizeContext(date,120)+", 3E"+year.ToString(CultureInfo.InvariantCulture)+". It is "+
                (isNight?"nighttime now":"daytime now")+". Use this live clock for time-sensitive decisions. "+
                "Earlier conversation timestamps and earlier descriptions of night or day do not override it. ";
        }

        public static string Timestamp(int hour,int minute,int day,string weekday,string month,int year)
        {
            int ending=day%100;
            string suffix=ending>=11 && ending<=13 ? "th" : day%10==1 ? "st" : day%10==2 ? "nd" : day%10==3 ? "rd" : "th";
            return hour.ToString("00",CultureInfo.InvariantCulture)+":"+minute.ToString("00",CultureInfo.InvariantCulture)+", "+
                day.ToString(CultureInfo.InvariantCulture)+suffix+" "+weekday+", "+month+", "+year.ToString(CultureInfo.InvariantCulture);
        }
        public static string Answer(string time, string date, int year)
        { return "It is " + time + " on " + date + ", 3E" + year.ToString(CultureInfo.InvariantCulture) + "."; }
    }
}
