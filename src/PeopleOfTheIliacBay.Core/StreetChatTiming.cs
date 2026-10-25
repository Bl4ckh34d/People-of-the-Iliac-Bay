using System;
namespace PeopleOfTheIliacBay.Core
{
    public static class StreetChatTiming
    {
        private static uint Seed(uint a,uint b,uint window)
        { return StableSeed.Combine(Math.Min(a,b),Math.Max(a,b),window,0x63686174); }
        public static bool ShouldMeet(uint a,uint b,uint window) {return Seed(a,b,window)%8==0;}
        public static int Duration(uint a,uint b,uint window) {return 120+(int)(Seed(a,b,window)%241);}
    }
}
