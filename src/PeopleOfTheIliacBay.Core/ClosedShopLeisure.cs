using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum ClosedShopPastime { Conversation, Stroll, Browse, Inn }

    // Stable per resident, destination and opening. No random work in Update.
    public static class ClosedShopLeisure
    {
        public static uint Seed(uint resident,int building,double opening)
        {
            unchecked
            {
                uint value=resident^((uint)building*0x9e3779b9u)^((uint)(opening/60)*0x85ebca6bu);
                value^=value>>16;value*=0x7feb352du;value^=value>>15;value*=0x846ca68bu;
                return value^(value>>16);
            }
        }
        public static double ReturnAt(uint resident,int building,double opening,double visitEnd)
        {
            if(double.IsNaN(opening)||double.IsInfinity(opening)||opening<0 ||
                double.IsNaN(visitEnd)||double.IsInfinity(visitEnd)||visitEnd<opening)
                throw new ArgumentException("Invalid closed-shop visit window.");
            // Keep enough of this visit to enter; never consume the next stop.
            double allowance=Math.Min(45*60,Math.Max(0,visitEnd-opening-120));
            return opening+Math.Min(allowance,Seed(resident,building,opening)%2701);
        }
        public static ClosedShopPastime Choose(uint resident,int building,double opening,bool inn,bool display)
        {
            uint choice=Seed(resident,building,opening)%6;
            if(choice==0 && inn)return ClosedShopPastime.Inn;
            if(choice==1 && display)return ClosedShopPastime.Browse;
            return choice<4?ClosedShopPastime.Conversation:ClosedShopPastime.Stroll;
        }
        public static string Context(string shop,double opening,double returnAt,double now,string activity)
        {
            string status=now<opening?"is still closed":"is now open";
            string next=now<returnAt?"I intend to return after this short diversion":"I am returning to my original destination";
            return "Currently "+activity+". Original destination: "+shop+", closed on arrival; it "+status+
                ". "+next+".";
        }
    }
}
