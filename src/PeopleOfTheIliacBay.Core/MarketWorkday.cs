using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class MarketWorkday
    {
        public static int ArrivalMinute(uint personSeed,int day)
        { return 360+(int)(StableSeed.Combine(personSeed,unchecked((uint)day),7829,0)%31); }
        public static int ClosingMinute(uint personSeed,int day,bool hasCustomer)
        { return (hasCustomer?1080:1065)+(int)(StableSeed.Combine(personSeed,unchecked((uint)day),7879,0)%16); }
        public static bool MayClose(double now,int closingMinute,bool talking,bool transacting)
        { return !talking && !transacting && now%86400>=closingMinute*60d; }
    }
}
