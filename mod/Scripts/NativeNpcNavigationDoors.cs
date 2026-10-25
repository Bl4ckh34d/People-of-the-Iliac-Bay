using System;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Let a moving native NavMesh actor use the same ordinary interior doors as
    // existing resident movement. Locked and action-owned doors stay closed.
    internal static class NativeNpcNavigationDoors
    {
        public static void OpenAhead(Vector3 ground,Vector3 steeringTarget)
        {
            var world=NativeNavigationWorld.Current;
            var game=GameManager.Instance;
            if(!world || !world.Ready || string.IsNullOrEmpty(world.Scene) || !game || !game.PlayerEnterExit ||
                !game.PlayerEnterExit.IsPlayerInside ||
                !(world.Scene.StartsWith("interior-",StringComparison.Ordinal) ||
                  world.Scene.StartsWith("dungeon-",StringComparison.Ordinal)) ||
                (steeringTarget-ground).sqrMagnitude<0.000001f)return;

            InteriorGeometry.OpenOrdinaryDoors(ground,steeringTarget);
        }
    }
}
