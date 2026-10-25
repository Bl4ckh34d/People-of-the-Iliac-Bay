using System.Collections.Generic;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Only this NPC's colliders; retain their individual original states.
    public sealed class SeatBodyCollision : MonoBehaviour
    {
        private readonly List<Collider> disabled=new List<Collider>();
        private bool suspended;
        public void Suspend()
        {
            if(suspended)return;suspended=true;
            foreach(var collider in GetComponentsInChildren<Collider>(true))
                if(collider.enabled){disabled.Add(collider);collider.enabled=false;}
        }
        public void Restore()
        {
            foreach(var collider in disabled)if(collider)collider.enabled=true;
            disabled.Clear();suspended=false;
        }
        private void OnDisable(){Restore();}
        private void OnDestroy(){Restore();}
        public static void Set(ResidentActor actor,bool seated)
        {
            var state=actor.GetComponent<SeatBodyCollision>();
            if(seated){if(!state)state=actor.gameObject.AddComponent<SeatBodyCollision>();state.Suspend();}
            else if(state)state.Restore();
        }
    }
}
