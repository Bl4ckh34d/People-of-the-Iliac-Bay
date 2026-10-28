using PeopleOfTheIliacBay.Core;
using System;
using DaggerfallWorkshop;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ResidentActor
    {
        internal WorldStateStore CompanionStateStore {get{return stateStore;}}
        internal static double CompanionNow()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}
    }
}
