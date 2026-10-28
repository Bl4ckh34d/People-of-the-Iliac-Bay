namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeDungeonSceneReadiness
    {
        internal static bool AllowsAdvance(bool inside,bool dungeonReady,bool initializing,bool exteriorReady,bool repositioning)
        {return !initializing && !repositioning && (inside?dungeonReady:exteriorReady);}
    }
}
