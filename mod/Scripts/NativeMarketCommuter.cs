using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // One authored static body remains the inventory, dialogue and damage body.
    // Scene changes may reparent it, but never create a second managed resident.
    internal sealed class NativeMarketCommuter : MonoBehaviour
    {
        internal StaticNPC Npc;
        internal NativePerson Person;
        internal BuildingDirectory Directory;
        internal Transform OriginalParent;
        internal Vector3 PostLocal,OriginalLocal,Offset;
        internal Quaternion OriginalRotation;
        internal bool OriginalActive,Initialized,InsideHome,LeftWork,AtPost;
        internal int Day=-1,ClosingMinute;
        internal int SceneKey=int.MinValue;
        internal double LastNow=-1;
        internal double HomeExitSeconds;
        internal string PostId,Phase;
        internal string ResponseIncident;
        internal MarketPostRole Role;
        internal List<Vector3> Route;
        internal List<Vector3> Commute;
        internal ResidentRoutine Routine;
        internal int Next;
        internal Vector3 RouteGoal;
        internal Vector3 DirectoryOrigin;
        internal float RetryAt;
    }
}
