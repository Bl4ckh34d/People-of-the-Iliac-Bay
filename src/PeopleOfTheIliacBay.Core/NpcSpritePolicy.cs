using System;

namespace PeopleOfTheIliacBay.Core
{
    // Presentation only. Callers supply physical states; artwork never starts an action.
    public static class NpcSpritePolicy
    {
        public static string Choose(bool alive,bool sleeping,bool seated,bool swimming,bool jumping,bool climbing,bool attacking,bool running)
        {
            if(!alive||sleeping||seated)return null;
            if(swimming)return "swim";
            if(jumping)return "jump";
            if(climbing)return "climb";
            if(attacking)return "melee";
            return running?"run":null;
        }
        public static bool MatchesProvider(string expected,string actual)
        {return string.Equals(expected??"",actual??"",StringComparison.OrdinalIgnoreCase);}
        public static int Phase(double seconds,int count,double fps,bool loop)
        {
            if(count<1||fps<=0||double.IsNaN(seconds)||double.IsInfinity(seconds))return 0;
            double frame=Math.Floor(Math.Max(0,seconds)*fps);
            return loop?(int)(frame%count):(int)Math.Min(count-1,frame);
        }
        public static int JumpPhase(double progress)
        {return progress<0.18?0:progress<0.42?1:progress<0.82?2:3;}
    }
}
