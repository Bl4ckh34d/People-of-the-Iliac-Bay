using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Passed for one movement call; never disables any Unity collider.
    public sealed class SelectedSeatAccess
    {
        public readonly FurnitureSeatCatalog Catalog;
        public readonly FurnitureSeatSlot Slot;
        public readonly string Owner;
        internal SelectedSeatAccess(FurnitureSeatCatalog catalog,FurnitureSeatSlot slot,string owner)
        {Catalog=catalog;Slot=slot;Owner=owner;}
        public bool Refresh() {return Catalog&&Catalog.TryClaim(Owner,Slot.Id,Time.unscaledTime);}
        public void Release() {if(Catalog)Catalog.Release(Owner);}
        internal bool Valid {get{return Catalog&&Catalog.Owns(Owner,Slot.Id,Time.unscaledTime);}}
        internal bool Permits(Vector3 from,Vector3 to)
        {
            if(!Valid)return false;
            return Vector3.Distance(from,Slot.Ground)<=1.6f&&Vector3.Distance(to,Slot.Ground)<=1.6f;
        }
        public bool Advance(ResidentActor actor,bool leaving=false)
        {
            if(!actor||actor.Resident.Id!=Owner||!Refresh())return false;
            var target=leaving?Slot.Approach:Slot.Ground;
            var crowd=actor.GetComponent<CrowdMotion>();
            if(!crowd||!crowd.HasPlacement||!Permits(actor.transform.position-Vector3.up,target))return false;
            crowd.ResumeFromPause();
            actor.Place(target,Slot.Facing,true,waitAtOccupiedTarget:true,seatAccess:this);
            var actual=actor.transform.position-Vector3.up;
            // The approach can be only just outside the chair footprint. Keep
            // its exit permission until the body, not merely its destination,
            // is clear enough for ordinary pathfinding to take over.
            bool arrived=Vector3.Distance(actual,target)<0.10f &&
                (!leaving || !FurnitureSeatCatalog.Blocks(actual,actual,CrowdMotion.BodyRadius));
            if(arrived&&leaving)SeatBodyCollision.Set(actor,false);
            return arrived;
        }
    }
}
