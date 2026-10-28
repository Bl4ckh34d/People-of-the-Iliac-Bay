using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeDungeonObserverGround
    {
        internal static Vector3 Read()
        {return GameManager.Instance.PlayerMotor.FindGroundPosition();}
    }
}
