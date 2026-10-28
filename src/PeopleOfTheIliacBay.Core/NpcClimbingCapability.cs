using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcClimbingCapability
    {
        // Same bounded skill/luck calculation as native FormulaHelper. Its
        // public entry point requires PlayerEntity, so NPCs use their own values.
        public static int StartChance(int liveSkill,int liveLuck,bool enhanced)
        {
            int skill=Math.Max(5,Math.Min(95,enhanced?liveSkill*2:liveSkill));
            return (int)(70+30*(skill/100d)+Math.Max(0,Math.Min(100,liveLuck))/10d);
        }
        public static double Speed(double walkSpeed,bool enhanced)
        {return walkSpeed/3*(enhanced?2:1);}
    }
}
